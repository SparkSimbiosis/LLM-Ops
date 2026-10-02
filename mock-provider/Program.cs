// Фейковий OpenAI-провайдер. Даємо готовим.
// Сенс: весь core проходиться без реального ключа — безкоштовно і передбачувано.
// Вміє дві речі: падати на замовлення (для reliability) і відповідати по-різному
// залежно від system-промпта (щоб eval ловив, коли промпт зламали).
// Розмову тримає на кілька реплік — це демо-логіка, не справжня модель.

using System.Text.Json;
using System.Text.RegularExpressions;

var app = WebApplication.CreateBuilder(args).Build();

app.MapPost("/v1/chat/completions", async (HttpRequest req) =>
{
    using var doc = await JsonDocument.ParseAsync(req.Body);
    var root = doc.RootElement;
    var model = root.TryGetProperty("model", out var m) ? m.GetString() ?? "mock" : "mock";

    // беремо останнє повідомлення юзера і system-промпт
    string user = "", system = "";
    foreach (var msg in root.GetProperty("messages").EnumerateArray())
    {
        var role = msg.GetProperty("role").GetString();
        var content = msg.GetProperty("content").GetString() ?? "";
        if (role == "user") user = content;
        else if (role == "system") system = content;
    }

    // збої: або через query (?fail=503&delay=2000&garbage=1), або маркером у тексті
    // (маркер проходить і крізь gateway): __fail_503, __fail_429, __delay, __garbage
    int? fail = null; int delay = 0; bool garbage = false;
    var q = req.Query;
    if (q.TryGetValue("fail", out var fq) && int.TryParse(fq, out var fc)) fail = fc;
    if (q.TryGetValue("delay", out var dq) && int.TryParse(dq, out var dm)) delay = dm;
    if (q.ContainsKey("garbage")) garbage = true;
    if (user.Contains("__fail_503")) fail = 503;
    if (user.Contains("__fail_429")) fail = 429;
    if (user.Contains("__delay")) delay = 2000;
    if (user.Contains("__garbage")) garbage = true;

    if (fail is int code) return Results.StatusCode(code);
    if (delay > 0) await Task.Delay(delay);

    // «розумні» відповіді видаємо тільки коли промпт правильний — так eval ловить регресію
    var promptOk = system.Contains("support", StringComparison.OrdinalIgnoreCase);
    var (answer, tool) = Reply(user, promptOk, garbage);

    int promptTokens = Tokens(system) + Tokens(user), completionTokens = Tokens(answer);
    return Results.Json(new
    {
        id = "mock-" + Guid.NewGuid().ToString("N")[..8],
        @object = "chat.completion",
        model,
        choices = new[]
        {
            new
            {
                index = 0,
                message = new { role = "assistant", content = answer, tool_calls = tool },
                finish_reason = tool is null ? "stop" : "tool_calls"
            }
        },
        usage = new { prompt_tokens = promptTokens, completion_tokens = completionTokens, total_tokens = promptTokens + completionTokens }
    });
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run("http://0.0.0.0:9000");

// грубо: ~4 символи = 1 токен, щоб usage було схоже на правду
static int Tokens(string s) => string.IsNullOrEmpty(s) ? 0 : Math.Max(1, s.Length / 4);

// проста логіка інтентів. дії (order/refund/safety) не залежать від промпта — це
// поведінка. «розумні» відповіді залежать від promptOk, щоб було на чому ловити регресію.
static (string, object?[]?) Reply(string user, bool promptOk, bool garbage)
{
    if (garbage) return ("...", null);
    var u = user.ToLowerInvariant();

    // дії — незалежно від промпта
    if (u.Contains("ignore") && u.Contains("instruction"))
        return ("Вибачте, не можу виконати це прохання.", null);
    // ескалація сильніша за перегляд замовлення: «поверніть гроші за замовлення #123» → тікет
    if (u.Contains("поверн") || u.Contains("терміново") || u.Contains("refund"))
        return ("Створюю тікет і ескалюю на оператора.", Tool("create_ticket"));
    if (u.Contains("замовлен") || u.Contains("order") || u.Contains("#"))
        return ("Перевіряю статус вашого замовлення…", Tool("lookup_order"));

    // далі — залежить від промпта
    if (!promptOk) return ("не знаю", null);
    if (u.Contains("пароль") || u.Contains("вхід") || u.Contains("login"))
        return ("Щоб скинути пароль: відкрийте сторінку входу, натисніть «Забули пароль» і перевірте email.", null);
    if (u.Contains("що ти можеш") || u.Contains("можеш") || u.Contains("допомог"))
        return ("Можу допомогти зі входом, замовленнями та поверненнями. З чим саме допомогти?", null);
    if (u.Contains("привіт") || u.Contains("вітаю") || u.Contains("hello") || Regex.IsMatch(u, @"hi"))
        return ("Вітаю! Розкажіть, що сталося — допоможу розібратися.", null);
    if (u.Contains("дякую") || u.Contains("thanks"))
        return ("Будь ласка! Є ще щось, із чим допомогти?", null);
    return ("Уточніть, будь ласка, деталі — і я допоможу.", null);
}

static object[] Tool(string name) => new object[]
{
    new { id = "call_" + name, type = "function", function = new { name, arguments = "{}" } }
};
