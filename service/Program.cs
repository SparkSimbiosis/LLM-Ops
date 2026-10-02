// Сервіс — це наш control plane. LiteLLM тільки ходить у модель, а рішення
// (яку модель брати, коли ретраїти, скільки коштує) робимо тут.
// MODEL=mock — дефолт, грошей не треба. MODEL=gpt-5-mini або claude-haiku + ключ у gateway/.env — реальна модель.

using System.Text;
using System.Text.Json;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();
var app = builder.Build();

// налаштування беремо з оточення (задаються в docker-compose.yml)
var gateway = Environment.GetEnvironmentVariable("GATEWAY_URL") ?? "http://gateway:4000";
var dbConn = Environment.GetEnvironmentVariable("DB_CONN")
    ?? "Host=postgres;Database=llmops;Username=llmops;Password=llmops";
var defaultModel = Environment.GetEnvironmentVariable("MODEL") ?? "mock";

app.MapPost("/chat", async (ChatIn body, IHttpClientFactory httpFactory) =>
{
    var requestId = Guid.NewGuid();
    var startedAt = DateTimeOffset.UtcNow;

    // guardrails (W4): тут перевірити вхід на PII / інʼєкції. поки нічого.
    // TODO(student, W4)

    // routing (W2): поки одна модель, а треба обирати за задачею
    var model = defaultModel;  // TODO(student, W2)

    // промпт (W1): активна версія з реєстру (таблиця prompts), читаємо рівно раз на запит —
    // той самий об'єкт іде і в модель, і в лог. порожній реєстр → чесний дефолт і версія 'none'.
    // недоступна БД — це не порожній реєстр: віддаємо 503, а не тихий дефолт.
    ActivePrompt prompt;
    try
    {
        prompt = await PromptRegistry.GetActiveAsync(dbConn) ?? PromptRegistry.Empty;
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "prompt registry read failed");
        return Results.Json(new { request_id = requestId, error = "prompt_registry_unavailable" }, statusCode: 503);
    }

    // cache (W3): перед викликом глянути в Redis — раптом вже відповідали
    // TODO(student, W3)

    // fallback (W4): якщо тут 429/5xx — піти на іншого провайдера. поки один виклик.
    // TODO(student, W4)
    var payload = JsonSerializer.Serialize(new
    {
        model,
        messages = new object[]
        {
            new { role = "system", content = prompt.Body },
            new { role = "user", content = body.Message }
        }
    });

    var http = httpFactory.CreateClient();
    var answer = "";
    string? toolCall = null;
    int promptTokens = 0, completionTokens = 0, status = 0; // 0 = відповіді не було
    try
    {
        var response = await http.PostAsync(
            $"{gateway}/v1/chat/completions",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        status = (int)response.StatusCode;
        var rawJson = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(rawJson);
        var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");
        answer = message.GetProperty("content").GetString() ?? "";

        // tools + HITL (W3/W4): якщо модель попросила інструмент — виконати;
        // перед незворотною дією (створити тікет) спитати людину. поки лише читаємо назву.
        // TODO(student, W3/W4)
        if (message.TryGetProperty("tool_calls", out var tools)
            && tools.ValueKind == JsonValueKind.Array && tools.GetArrayLength() > 0)
        {
            toolCall = tools[0].GetProperty("function").GetProperty("name").GetString();
        }

        var usage = doc.RootElement.GetProperty("usage");
        promptTokens = usage.GetProperty("prompt_tokens").GetInt32();
        completionTokens = usage.GetProperty("completion_tokens").GetInt32();
    }
    catch
    {
        // мережа/gateway недоступні або відповідь не розпарсилась (status лишиться 0/5xx).
        // TODO(student, W4): тут краще graceful degradation
        answer = "Сервіс тимчасово недоступний.";
    }

    var latencyMs = (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;

    // cost (W2): порахувати tokens * ціна і покласти в cost_usd
    decimal? costUsd = null;  // TODO(student, W2)

    // лог кожного запиту — з цього живе observability (W1) і cost (W2)
    await LogRequest(dbConn, requestId, model, prompt, latencyMs, promptTokens, completionTokens, costUsd, status);

    return Results.Json(new { request_id = requestId, content = answer, tool = toolCall, latency_ms = latencyMs });
});

// ці ендпоінти читає готова консоль. поверни потрібну форму — картки оживуть.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));                                    // ліфнес, не для консолі
app.MapGet("/observability", () => Results.Json(new { todo = "aggregate from requests table" }));  // W5: { p95_ms, requests, cache_hit_pct, error_rate_pct, fallback_events }
app.MapGet("/cost", () => Results.Json(new { todo = "sum cost_usd for today + budget" }));         // W2/W5: { today_usd, budget_usd }
app.MapGet("/providers", () => Results.Json(new { todo = "provider health" }));                    // W5: { providers: [ { name, status } ] }
app.MapGet("/approvals", () => Results.Json(new { todo = "pending HITL approvals" }));              // W4: { pending: [ { id, action } ] }

// реєстр промптів (W1): [ { name, version, active, version_id, sha256, created_at } ] для картки Prompt registry
app.MapGet("/prompts", async () =>
{
    try
    {
        var rows = await PromptRegistry.ListAsync(dbConn);
        return Results.Json(rows.Select(p => p.ToJson()));
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "prompt registry read failed");
        return Results.Json(new { error = "prompt_registry_unavailable" }, statusCode: 503);
    }
});

// promote / rollback. тіло не біндимо: curl без тіла і консоль із {} працюють однаково.
// необов'язкове {"quote": "..."} читаємо вручну — порожнє чи невалідне тіло просто ігноруємо.
// автентифікації поки немає — тому в журнал активацій чесно пишемо actor = anonymous.
app.MapPost("/prompts/{version}/activate", async (string version, HttpRequest req) =>
{
    string? quote = null;
    try
    {
        if (req.ContentLength > 0 || !string.IsNullOrEmpty(req.ContentType))
        {
            using var d = await JsonDocument.ParseAsync(req.Body);
            if (d.RootElement.ValueKind == JsonValueKind.Object
                && d.RootElement.TryGetProperty("quote", out var q) && q.ValueKind == JsonValueKind.String)
                quote = q.GetString();
        }
    }
    catch { /* тіло необов'язкове: без quote активація та сама */ }

    try
    {
        var result = await PromptRegistry.ActivateAsync(dbConn, version, actor: "anonymous", quote);
        return result is null
            ? Results.Json(new { error = "prompt_version_not_found", version }, statusCode: 404)
            : Results.Json(new
            {
                name = result.Name,
                version = result.Version,
                previous_version = result.PreviousVersion,
                versions = result.Versions.Select(p => p.ToJson())
            });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "prompt activation failed");
        return Results.Json(new { error = "prompt_registry_unavailable" }, statusCode: 503);
    }
});

app.Run("http://0.0.0.0:8080");

// пише один рядок у requests. якщо лог впав — запит користувача все одно віддаємо.
// версію промпта беремо з того самого об'єкта, що пішов у модель, — без повторного SELECT.
static async Task LogRequest(string conn, Guid id, string model, ActivePrompt prompt, int latency,
    int promptTokens, int completionTokens, decimal? cost, int status)
{
    try
    {
        await using var db = new NpgsqlConnection(conn);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO requests (request_id, model, prompt_name, prompt_version, prompt_version_id, prompt_sha256, "
            + "latency_ms, prompt_tokens, completion_tokens, cost_usd, status) "
            + "VALUES (@id, @model, @pname, @pv, @pvid, @psha, @lat, @pt, @ct, @cost, @status)", db);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("model", model);
        cmd.Parameters.AddWithValue("pname", prompt.Name);                                   // ключ у реєстрі, у модель не йде
        cmd.Parameters.AddWithValue("pv", prompt.Version);                                   // 'v1' / 'v2' / 'none'
        cmd.Parameters.AddWithValue("pvid", (object?)prompt.VersionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("psha", prompt.Sha256);
        cmd.Parameters.AddWithValue("lat", latency);
        cmd.Parameters.AddWithValue("pt", promptTokens);
        cmd.Parameters.AddWithValue("ct", completionTokens);
        cmd.Parameters.AddWithValue("cost", (object?)cost ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status", status.ToString());
        await cmd.ExecuteNonQueryAsync();
    }
    catch { /* не валимо запит через лог */ }
}

record ChatIn(string Message);
