using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arc.Core;

// Códigos de salida: 0 éxito · 1 error · 2 uso incorrecto · 3 espera expirada · 4 sin mensajes.
// Los tres últimos permiten a un agente ramificar sin analizar el texto de salida.
const int ExitOk = 0, ExitError = 1, ExitUsage = 2, ExitTimeout = 3, ExitEmpty = 4;

Console.OutputEncoding = new UTF8Encoding(false);

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine(Help.Text);
    return args.Length == 0 ? ExitUsage : ExitOk;
}

var command = args[0];
var flags = Flags.Parse(args.Skip(1));

var url = (flags.Value("url") ?? Environment.GetEnvironmentVariable("ARC_URL") ?? "http://127.0.0.1:8765").TrimEnd('/');
var agent = flags.Value("agent") ?? Environment.GetEnvironmentVariable("ARC_AGENT");
var token = flags.Value("token") ?? Environment.GetEnvironmentVariable("ARC_TOKEN");
var asJson = flags.Has("json");

if (string.IsNullOrWhiteSpace(agent))
{
    Console.Error.WriteLine("Falta la identidad del agente. Define ARC_AGENT o pasa --agent <nombre>.");
    return ExitUsage;
}

using var http = new HttpClient
{
    BaseAddress = new Uri(url),
    // Por encima del tope del hub: quien decide cuánto se espera es el servidor.
    Timeout = TimeSpan.FromSeconds(400)
};
http.DefaultRequestHeaders.Add("X-ARC-Agent", agent);
if (!string.IsNullOrWhiteSpace(token)) http.DefaultRequestHeaders.Add("X-ARC-Token", token);
if (Environment.GetEnvironmentVariable("ARC_PROVIDER") is { Length: > 0 } provider)
    http.DefaultRequestHeaders.Add("X-ARC-Provider", provider);

try
{
    return command switch
    {
        "ask" => await AskAsync(),
        "await" => await AwaitAsync(),
        "inbox" => await InboxAsync(),
        "respond" => await RespondAsync(),
        "note" => await NoteAsync(),
        "thread" => await ThreadAsync(),
        "agents" => await GetAsync("/v1/agents"),
        "health" => await GetAsync("/healthz"),
        _ => Fail($"Comando desconocido: '{command}'.\n\n{Help.Text}", ExitUsage)
    };
}
catch (HttpRequestException exception)
{
    return Fail($"No se pudo contactar con el hub en {url}: {exception.Message}", ExitError);
}
catch (TaskCanceledException)
{
    return Fail($"El hub en {url} no respondió a tiempo.", ExitError);
}

// ---------- Comandos ----------

async Task<int> AskAsync()
{
    var to = flags.Value("to");
    if (string.IsNullOrWhiteSpace(to)) return Fail("Falta --to <agente>.", ExitUsage);

    if (ReadBody() is not { } body) return ExitUsage;
    var wait = flags.Number("wait") ?? 120;

    var payload = new JsonObject { ["to"] = to, ["body"] = body };
    if (flags.Value("subject") is { Length: > 0 } subject) payload["subject"] = subject;
    if (flags.Value("thread") is { Length: > 0 } thread) payload["thread_id"] = thread;
    if (ReadRefs() is { } refs) payload["refs"] = refs;

    var started = DateTimeOffset.UtcNow;
    var response = await http.PostAsync($"/v1/requests?wait={wait}", Json(payload));
    var text = await response.Content.ReadAsStringAsync();

    if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Accepted)) return FailHttp(response, text);
    if (asJson) { Console.WriteLine(text); }

    var result = Deserialize<AskResult>(text);
    if (result is null) return Fail("Respuesta del hub ilegible.", ExitError);

    if (result.Outcome == "answered" && result.Response is { } answer)
    {
        if (!asJson)
        {
            var seconds = (int)(DateTimeOffset.UtcNow - started).TotalSeconds;
            Console.WriteLine($"Respondido por {answer.From} en {seconds}s  ({result.RequestId} · hilo {result.ThreadId})");
            if (answer.Refs is { } answerRefs) Console.WriteLine($"refs: {answerRefs.GetRawText()}");
            Console.WriteLine();
            Console.WriteLine(answer.Body);
        }
        return ExitOk;
    }

    if (!asJson)
    {
        Console.WriteLine($"Sin respuesta tras {wait}s. La petición sigue viva: {result.RequestId}");
        Console.WriteLine($"Retómala con:  arc await {result.RequestId} --wait 300");
    }
    return ExitTimeout;
}

async Task<int> AwaitAsync()
{
    if (flags.Positional.FirstOrDefault() is not { Length: > 0 } requestId)
        return Fail("Uso: arc await <request_id> [--wait N]", ExitUsage);

    var wait = flags.Number("wait") ?? 120;
    var response = await http.GetAsync($"/v1/requests/{requestId}/response?wait={wait}");
    var text = await response.Content.ReadAsStringAsync();

    if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Accepted)) return FailHttp(response, text);
    if (asJson) Console.WriteLine(text);

    var result = Deserialize<AskResult>(text);
    if (result?.Outcome == "answered" && result.Response is { } answer)
    {
        if (!asJson)
        {
            Console.WriteLine($"Respondido por {answer.From}  ({result.RequestId} · hilo {result.ThreadId})");
            Console.WriteLine();
            Console.WriteLine(answer.Body);
        }
        return ExitOk;
    }

    if (!asJson) Console.WriteLine($"{requestId} sigue sin respuesta.");
    return ExitTimeout;
}

async Task<int> InboxAsync()
{
    var wait = flags.Number("wait") ?? 0;
    var query = $"/v1/inbox/{agent}?wait={wait}" + (flags.Has("unanswered") ? "&unanswered=true" : "");

    var response = await http.GetAsync(query);
    var text = await response.Content.ReadAsStringAsync();

    if (response.StatusCode == HttpStatusCode.NoContent)
    {
        if (asJson) Console.WriteLine("""{"messages":[]}""");
        else Console.WriteLine($"Sin mensajes para {agent}.");
        return ExitEmpty;
    }
    if (!response.IsSuccessStatusCode) return FailHttp(response, text);

    if (asJson) { Console.WriteLine(text); return ExitOk; }

    var inbox = Deserialize<InboxResult>(text);
    if (inbox is null || inbox.Messages.Count == 0) return ExitEmpty;

    Console.WriteLine($"{inbox.Messages.Count} mensaje(s) para {agent}");
    var index = 0;
    foreach (var message in inbox.Messages)
    {
        Console.WriteLine();
        Console.WriteLine($"[{++index}] {message.Kind.ToString().ToLowerInvariant()} {message.Id}  de {message.From}  hilo {message.ThreadId}  {Ago(message.CreatedAt)}");
        if (message.Subject is { Length: > 0 } subject) Console.WriteLine($"    asunto: {subject}");
        if (message.Refs is { } refs) Console.WriteLine($"    refs: {refs.GetRawText()}");
        Console.WriteLine();
        foreach (var line in message.Body.ReplaceLineEndings("\n").Split('\n')) Console.WriteLine("    " + line);
        if (message.Kind == MessageKind.Request)
        {
            Console.WriteLine();
            Console.WriteLine($"    responder:  arc respond {message.Id} --body-file <fichero>");
        }
    }
    return ExitOk;
}

async Task<int> RespondAsync()
{
    if (flags.Positional.FirstOrDefault() is not { Length: > 0 } requestId)
        return Fail("Uso: arc respond <request_id> --body-file <fichero>", ExitUsage);
    if (ReadBody() is not { } body) return ExitUsage;

    var payload = new JsonObject { ["body"] = body };
    if (ReadRefs() is { } refs) payload["refs"] = refs;

    var response = await http.PostAsync($"/v1/requests/{requestId}/response", Json(payload));
    var text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) return FailHttp(response, text);

    if (asJson) Console.WriteLine(text);
    else Console.WriteLine($"Respuesta entregada a la petición {requestId}.");
    return ExitOk;
}

async Task<int> NoteAsync()
{
    var to = flags.Value("to");
    if (string.IsNullOrWhiteSpace(to)) return Fail("Falta --to <agente>.", ExitUsage);
    if (ReadBody() is not { } body) return ExitUsage;

    var payload = new JsonObject { ["to"] = to, ["body"] = body };
    if (flags.Value("subject") is { Length: > 0 } subject) payload["subject"] = subject;
    if (flags.Value("thread") is { Length: > 0 } thread) payload["thread_id"] = thread;
    if (ReadRefs() is { } refs) payload["refs"] = refs;

    var response = await http.PostAsync("/v1/notes", Json(payload));
    var text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) return FailHttp(response, text);

    if (asJson) Console.WriteLine(text);
    else Console.WriteLine($"Aviso enviado a {to}.");
    return ExitOk;
}

async Task<int> ThreadAsync()
{
    if (flags.Positional.FirstOrDefault() is not { Length: > 0 } threadId)
        return Fail("Uso: arc thread <thread_id>", ExitUsage);

    var response = await http.GetAsync($"/v1/threads/{threadId}");
    var text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) return FailHttp(response, text);

    if (asJson) { Console.WriteLine(text); return ExitOk; }

    var messages = Deserialize<List<Message>>(text) ?? [];
    Console.WriteLine($"hilo {threadId} · {messages.Count} mensaje(s)");
    foreach (var message in messages)
    {
        Console.WriteLine();
        Console.WriteLine($"{message.CreatedAt.ToLocalTime():HH:mm:ss}  {message.From} -> {message.To}  ({message.Kind.ToString().ToLowerInvariant()})");
        foreach (var line in message.Body.ReplaceLineEndings("\n").Split('\n')) Console.WriteLine("    " + line);
    }
    return ExitOk;
}

async Task<int> GetAsync(string path)
{
    var response = await http.GetAsync(path);
    var text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) return FailHttp(response, text);
    Console.WriteLine(text);
    return ExitOk;
}

// ---------- Auxiliares ----------

// El cuerpo entra por fichero o por stdin, nunca por argv salvo petición expresa:
// en Windows los argumentos pasan por la codepage ANSI y destrozan los acentos.
string? ReadBody()
{
    var file = flags.Value("body-file");
    if (file is not null)
    {
        if (file == "-") return Console.In.ReadToEnd();
        if (!File.Exists(file)) { Console.Error.WriteLine($"No existe el fichero: {file}"); return null; }
        return File.ReadAllText(file, Encoding.UTF8);
    }

    if (flags.Value("body") is { } inline) return inline;
    if (flags.Has("stdin")) return Console.In.ReadToEnd();

    Console.Error.WriteLine("Falta el cuerpo. Usa --body-file <fichero>, --body-file - (stdin) o --body \"texto\".");
    return null;
}

JsonNode? ReadRefs()
{
    var raw = flags.Value("refs-file") is { } file && File.Exists(file)
        ? File.ReadAllText(file, Encoding.UTF8)
        : flags.Value("refs");

    if (string.IsNullOrWhiteSpace(raw)) return null;
    try { return JsonNode.Parse(raw); }
    catch (JsonException exception)
    {
        Console.Error.WriteLine($"--refs no es JSON válido: {exception.Message}");
        return null;
    }
}

static StringContent Json(JsonNode payload) =>
    new(payload.ToJsonString(), new UTF8Encoding(false), "application/json");

static T? Deserialize<T>(string text)
{
    try { return JsonSerializer.Deserialize<T>(text, ArcJson.Options); }
    catch (JsonException) { return default; }
}

static string Ago(DateTimeOffset moment)
{
    var elapsed = DateTimeOffset.UtcNow - moment;
    if (elapsed.TotalSeconds < 60) return $"hace {(int)elapsed.TotalSeconds}s";
    if (elapsed.TotalMinutes < 60) return $"hace {(int)elapsed.TotalMinutes}min";
    if (elapsed.TotalHours < 24) return $"hace {(int)elapsed.TotalHours}h";
    return moment.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
}

static int Fail(string message, int code)
{
    Console.Error.WriteLine(message);
    return code;
}

static int FailHttp(HttpResponseMessage response, string text)
{
    var detail = Deserialize<ErrorBody>(text);
    Console.Error.WriteLine(detail is null
        ? $"El hub respondió {(int)response.StatusCode}: {text}"
        : $"El hub respondió {(int)response.StatusCode} ({detail.Error}): {detail.Detail}");
    return 1;
}

// ---------- Argumentos ----------

/// <summary>Parser mínimo: --clave valor, --bandera, y posicionales.</summary>
internal sealed class Flags
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _switches = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Positional { get; } = [];

    public static Flags Parse(IEnumerable<string> args)
    {
        var flags = new Flags();
        var queue = new Queue<string>(args);
        while (queue.Count > 0)
        {
            var token = queue.Dequeue();
            if (!token.StartsWith("--", StringComparison.Ordinal)) { flags.Positional.Add(token); continue; }

            var name = token[2..];
            if (name.Contains('='))
            {
                var parts = name.Split('=', 2);
                flags._values[parts[0]] = parts[1];
            }
            // "-" es un valor legítimo (stdin), no el inicio de otra bandera.
            else if (queue.Count > 0 && (queue.Peek() == "-" || !queue.Peek().StartsWith("--", StringComparison.Ordinal)))
            {
                flags._values[name] = queue.Dequeue();
            }
            else
            {
                flags._switches.Add(name);
            }
        }
        return flags;
    }

    public string? Value(string name) => _values.GetValueOrDefault(name);
    public bool Has(string name) => _switches.Contains(name) || _values.ContainsKey(name);
    public int? Number(string name) => int.TryParse(Value(name), out var value) ? value : null;
}

internal static class Help
{
    public const string Text = """
        arc — canal de peticiones entre agentes

        Uso:
          arc ask      --to <agente> --body-file <f> [--wait N] [--subject S] [--refs JSON] [--thread ID]
          arc await    <request_id> [--wait N]
          arc inbox    [--wait N] [--unanswered]
          arc respond  <request_id> --body-file <f> [--refs JSON]
          arc note     --to <agente> --body-file <f>
          arc thread   <thread_id>
          arc agents
          arc health

        El cuerpo se pasa por fichero (--body-file f), por stdin (--body-file -)
        o en línea (--body "texto"). En Windows, --body-file evita que los acentos
        se corrompan al atravesar la codepage de la consola.

        Configuración por entorno:
          ARC_URL      hub al que conectarse        (por defecto http://127.0.0.1:8765)
          ARC_AGENT    identidad de este agente     (obligatoria)
          ARC_TOKEN    secreto compartido del hub
          ARC_PROVIDER etiqueta informativa: claude-code, codex...

        Opciones comunes:
          --json       salida JSON cruda, sin formato de lectura
          --url --agent --token   equivalen a las variables de entorno

        Códigos de salida:
          0 éxito · 1 error · 2 uso incorrecto · 3 espera expirada · 4 sin mensajes

        Ejemplo de ciclo bloqueante:
          arc ask --to codex-pc2 --subject "Contrato" --body-file pregunta.md --wait 180
          arc inbox --wait 300
          arc respond req_1234 --body-file respuesta.md
        """;
}
