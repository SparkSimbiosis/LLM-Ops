// Реєстр промптів (W1). Єдине місце, яке читає таблицю prompts і збирає з неї system-промпт:
// Program.cs тексту промпта не тримає, лише питає тут активну версію.
// Версії наповнює міграція db/schema.sql — сервіс їх тільки читає і перемикає active.

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Npgsql;

// версія, яка реально пішла в модель. Body — вже відрендерений текст system-повідомлення,
// Sha256 — від цього самого тексту (UTF-8, hex у нижньому регістрі): по логу видно,
// що саме бачила модель, навіть якщо тіло версії потім переписали руками.
record ActivePrompt(string Name, string Version, Guid? VersionId, string Body, string Sha256);

// рядок реєстру для GET /prompts. sha256 тут — від тіла версії; поки плейсхолдерів у тілі
// немає, він збігається з prompt_sha256 у requests.
record PromptRow(string Name, string Version, bool Active, Guid VersionId, string Sha256, DateTime CreatedAt)
{
    // форма відповіді: snake_case, як і решта API; картка консолі читає name / version / active
    public object ToJson() => new
    {
        name = Name, version = Version, active = Active,
        version_id = VersionId, sha256 = Sha256, created_at = CreatedAt
    };
}

record Activation(string Name, string Version, string? PreviousVersion, List<PromptRow> Versions);

static class PromptRegistry
{
    // промпт, який бере /chat. Назва — лише ключ у таблиці, у текст повідомлень вона не потрапляє.
    public const string Name = "support-system";

    // чесний дефолт, коли активної версії немає. Навмисно без слова «support»: mock тоді
    // відповідає «не знаю», evals червоніють — поломку реєстру видно, а не замасковано.
    public const string Fallback = "You are an assistant. No prompt version is active in the registry.";

    // що йде в модель і в лог, коли реєстр порожній: версія 'none', id версії немає
    public static readonly ActivePrompt Empty = new(Name, "none", null, Fallback, Sha256(Fallback));

    // змінних у шаблонах поки немає — Render для v1/v2 нічого не змінює
    static readonly IReadOnlyDictionary<string, string> NoVars = new Dictionary<string, string>();

    static readonly Regex Placeholder = new(@"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}", RegexOptions.Compiled);

    // активна версія, один SELECT на запит. null — активної немає (реєстр порожній або active зняли).
    // помилку БД не ковтаємо: недоступна база — це не порожній реєстр, вирішує Program.cs.
    public static async Task<ActivePrompt?> GetActiveAsync(string conn)
    {
        await using var db = new NpgsqlConnection(conn);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT version, body, version_id FROM prompts "
            + "WHERE name = @name AND active ORDER BY created_at DESC LIMIT 1", db);
        cmd.Parameters.AddWithValue("name", Name);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;

        var text = Render(r.GetString(1), NoVars);
        return new ActivePrompt(Name, r.GetString(0), r.GetGuid(2), text, Sha256(text));
    }

    // {{name}} → значення з vars за один прохід: підставлене значення вдруге не розбирається.
    // невідомий плейсхолдер — виняток, а не тихий порожній рядок у промпті.
    public static string Render(string template, IReadOnlyDictionary<string, string> vars) =>
        Placeholder.Replace(template, m => vars.TryGetValue(m.Groups[1].Value, out var value)
            ? value
            : throw new InvalidOperationException("unknown prompt placeholder: " + m.Groups[1].Value));

    public static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    public static async Task<List<PromptRow>> ListAsync(string conn)
    {
        await using var db = new NpgsqlConnection(conn);
        await db.OpenAsync();
        return await ReadAllAsync(db, null);
    }

    // promote / rollback: активною стає рівно одна версія — одна транзакція на все.
    // null — такої версії немає: нічого не змінено і подію активації не записано.
    public static async Task<Activation?> ActivateAsync(string conn, string version, string actor, string? quote)
    {
        await using var db = new NpgsqlConnection(conn);
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync();

        // блокуємо рядки цього промпта: паралельна активація чекає своєї черги,
        // а previous_version у журналі — справді та версія, яку ми замінили
        string? previous = null;
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT version, active FROM prompts WHERE name = @name ORDER BY created_at DESC FOR UPDATE", db, tx))
        {
            lockCmd.Parameters.AddWithValue("name", Name);
            await using var r = await lockCmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                if (previous is null && r.GetBoolean(1)) previous = r.GetString(0);
        }

        // один UPDATE: обрана версія → true, решта → false. EXISTS не дає «тихо» зняти
        // активність з усіх, коли версії немає (v99): тоді не оновиться жоден рядок.
        await using (var update = new NpgsqlCommand(
            "UPDATE prompts SET active = (version = @version) WHERE name = @name "
            + "AND EXISTS (SELECT 1 FROM prompts WHERE name = @name AND version = @version)", db, tx))
        {
            update.Parameters.AddWithValue("name", Name);
            update.Parameters.AddWithValue("version", version);
            if (await update.ExecuteNonQueryAsync() == 0)
            {
                await tx.RollbackAsync();
                return null;
            }
        }

        // audit trail — у тій самій транзакції, що й зміна active
        await using (var audit = new NpgsqlCommand(
            "INSERT INTO prompt_activations (name, version, previous_version, actor, approval_quote) "
            + "VALUES (@name, @version, @previous, @actor, @quote)", db, tx))
        {
            audit.Parameters.AddWithValue("name", Name);
            audit.Parameters.AddWithValue("version", version);
            audit.Parameters.AddWithValue("previous", (object?)previous ?? DBNull.Value);
            audit.Parameters.AddWithValue("actor", actor);
            audit.Parameters.AddWithValue("quote", (object?)quote ?? DBNull.Value);
            await audit.ExecuteNonQueryAsync();
        }

        var versions = await ReadAllAsync(db, tx);
        await tx.CommitAsync();
        return new Activation(Name, version, previous, versions);
    }

    static async Task<List<PromptRow>> ReadAllAsync(NpgsqlConnection db, NpgsqlTransaction? tx)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT name, version, active, version_id, body, created_at FROM prompts "
            + "ORDER BY name, created_at, version", db, tx);
        await using var r = await cmd.ExecuteReaderAsync();
        var rows = new List<PromptRow>();
        while (await r.ReadAsync())
            rows.Add(new PromptRow(r.GetString(0), r.GetString(1), r.GetBoolean(2), r.GetGuid(3),
                Sha256(r.GetString(4)), r.GetDateTime(5)));
        return rows;
    }
}
