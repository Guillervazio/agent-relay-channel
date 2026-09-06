using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arc.Core;

namespace Arc.Cli;

/// <summary>
/// El cliente entero, con su entrada y su salida por parámetro en vez de por
/// <c>Console</c>. Es lo que permite afirmar en un test los cinco códigos de salida
/// —que son contrato publicado— sin red y sin un hub que responda.
/// </summary>
public sealed class CliRunner
{
    private readonly TextWriter _out;
    private readonly TextWriter _err;
    private readonly TextReader _in;
    private readonly TimeProvider _time;

    private Flags _flags = null!;
    private HttpClient _http = null!;
    private bool _asJson;
    private string _agent = string.Empty;
    private string _url = string.Empty;

    public CliRunner(TextWriter output, TextWriter error, TextReader input, TimeProvider? time = null)
    {
        _out = output;
        _err = error;
        _in = input;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// <paramref name="handler"/> es la costura: en producción no se pasa y el cliente
    /// habla por la red; en un test es un doble que contesta lo que el caso necesita.
    /// </summary>
    public async Task<int> RunAsync(string[] args, HttpMessageHandler? handler = null)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            _out.WriteLine(Help.Text);
            return args.Length == 0 ? ExitCodes.Usage : ExitCodes.Ok;
        }

        string command = args[0];
        _flags = Flags.Parse(args.Skip(1));

        _url = (_flags.Value("url") ?? Environment.GetEnvironmentVariable("ARC_URL") ?? "http://127.0.0.1:8765").TrimEnd('/');
        string url = _url;
        string? agent = _flags.Value("agent") ?? Environment.GetEnvironmentVariable("ARC_AGENT");
        string? token = _flags.Value("token") ?? Environment.GetEnvironmentVariable("ARC_TOKEN");
        _asJson = _flags.Has("json");

        if (string.IsNullOrWhiteSpace(agent))
        {
            _err.WriteLine("Falta la identidad del agente. Define ARC_AGENT o pasa --agent <nombre>.");
            return ExitCodes.Usage;
        }

        _agent = agent;

        using HttpClient http = (handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false));
        _http = http;
        Configure(http);

        void Configure(HttpClient client)
        {
            client.BaseAddress = new Uri(url);
            // Por encima del tope del hub: quien decide cuánto se espera es el servidor.
            client.Timeout = TimeSpan.FromSeconds(400);
            client.DefaultRequestHeaders.Add("X-ARC-Agent", agent);
            if (!string.IsNullOrWhiteSpace(token))
            {
                client.DefaultRequestHeaders.Add("X-ARC-Token", token);
            }

            if (Environment.GetEnvironmentVariable("ARC_PROVIDER") is { Length: > 0 } provider)
            {
                client.DefaultRequestHeaders.Add("X-ARC-Provider", provider);
            }
        }

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
                _ => Fail($"Comando desconocido: '{command}'.\n\n{Help.Text}", ExitCodes.Usage)
            };
        }
        catch (HttpRequestException exception)
        {
            return Fail($"No se pudo contactar con el hub en {url}: {exception.Message}", ExitCodes.Error);
        }
        catch (TaskCanceledException)
        {
            return Fail($"El hub en {url} no respondió a tiempo.", ExitCodes.Error);
        }
    }

    // ---------- Comandos ----------

    private async Task<int> AskAsync()
    {
        string? to = _flags.Value("to");
        if (string.IsNullOrWhiteSpace(to))
        {
            return Fail("Falta --to <agente>.", ExitCodes.Usage);
        }

        if (ReadBody() is not { } body)
        {
            return ExitCodes.Usage;
        }

        int wait = _flags.Number("wait") ?? 120;

        JsonObject payload = new JsonObject { ["to"] = to, ["body"] = body };
        if (_flags.Value("subject") is { Length: > 0 } subject)
        {
            payload["subject"] = subject;
        }

        if (_flags.Value("thread") is { Length: > 0 } thread)
        {
            payload["thread_id"] = thread;
        }

        if (!TryReadRefs(out JsonNode? refs))
        {
            return ExitCodes.Usage;
        }

        if (refs is not null)
        {
            payload["refs"] = refs;
        }

        DateTimeOffset started = DateTimeOffset.UtcNow;
        HttpResponseMessage response = await _http.PostAsync($"/v1/requests?wait={wait}", Json(payload));
        string text = await response.Content.ReadAsStringAsync();

        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Accepted))
        {
            return FailHttp(response, text);
        }

        if (_asJson)
        {
            _out.WriteLine(text);
        }

        AskResult? result = Deserialize<AskResult>(text);
        if (result is null)
        {
            return Fail("Respuesta del hub ilegible.", ExitCodes.Error);
        }

        if (result.Outcome == "answered" && result.Response is { } answer)
        {
            if (!_asJson)
            {
                int seconds = (int)(DateTimeOffset.UtcNow - started).TotalSeconds;
                _out.WriteLine($"Respondido por {answer.From} en {seconds}s  ({result.RequestId} · hilo {result.ThreadId})");
                if (answer.Refs is { } answerRefs)
                {
                    _out.WriteLine($"refs: {answerRefs.GetRawText()}");
                }

                _out.WriteLine();
                _out.WriteLine(answer.Body);
            }
            return ExitCodes.Ok;
        }

        if (!_asJson)
        {
            _out.WriteLine($"Sin respuesta tras {wait}s. La petición sigue viva: {result.RequestId}");
            _out.WriteLine($"Retómala con:  arc await {result.RequestId} --wait 300");
        }
        return ExitCodes.Timeout;
    }

    private async Task<int> AwaitAsync()
    {
        if (_flags.Positional.FirstOrDefault() is not { Length: > 0 } requestId)
        {
            return Fail("Uso: arc await <request_id> [--wait N]", ExitCodes.Usage);
        }

        int wait = _flags.Number("wait") ?? 120;
        HttpResponseMessage response = await _http.GetAsync($"/v1/requests/{requestId}/response?wait={wait}");
        string text = await response.Content.ReadAsStringAsync();

        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Accepted))
        {
            return FailHttp(response, text);
        }

        if (_asJson)
        {
            _out.WriteLine(text);
        }

        AskResult? result = Deserialize<AskResult>(text);
        if (result?.Outcome == "answered" && result.Response is { } answer)
        {
            if (!_asJson)
            {
                _out.WriteLine($"Respondido por {answer.From}  ({result.RequestId} · hilo {result.ThreadId})");
                _out.WriteLine();
                _out.WriteLine(answer.Body);
            }
            return ExitCodes.Ok;
        }

        if (!_asJson)
        {
            _out.WriteLine($"{requestId} sigue sin respuesta.");
        }

        return ExitCodes.Timeout;
    }

    private async Task<int> InboxAsync()
    {
        int wait = _flags.Number("wait") ?? 0;
        string query = $"/v1/inbox/{_agent}?wait={wait}"
            + (_flags.Has("unanswered") ? "&unanswered=true" : "")
            + (_flags.Has("replay") ? $"&replay={_flags.Value("replay")}" : "");

        HttpResponseMessage response = await _http.GetAsync(query);
        string text = await response.Content.ReadAsStringAsync();

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            if (_asJson)
            {
                _out.WriteLine("""{"messages":[]}""");
            }
            else
            {
                _out.WriteLine($"Sin mensajes para {_agent}.");
            }

            return ExitCodes.Empty;
        }
        if (!response.IsSuccessStatusCode)
        {
            return FailHttp(response, text);
        }

        if (_asJson)
        {
            _out.WriteLine(text);
            return ExitCodes.Ok;
        }

        InboxResult? inbox = Deserialize<InboxResult>(text);
        if (inbox is null || inbox.Messages.Count == 0)
        {
            return ExitCodes.Empty;
        }

        _out.WriteLine($"{inbox.Messages.Count} mensaje(s) para {_agent}");
        int index = 0;
        foreach (Message message in inbox.Messages)
        {
            _out.WriteLine();
            _out.WriteLine($"[{++index}] {message.Kind.ToString().ToLowerInvariant()} {message.Id}  de {message.From}  hilo {message.ThreadId}  {Ago(message.CreatedAt)}");
            if (message.Subject is { Length: > 0 } subject)
            {
                _out.WriteLine($"    asunto: {subject}");
            }

            if (message.Refs is { } refs)
            {
                _out.WriteLine($"    refs: {refs.GetRawText()}");
            }

            _out.WriteLine();
            foreach (string line in message.Body.ReplaceLineEndings("\n").Split('\n'))
            {
                _out.WriteLine("    " + line);
            }

            if (message.Kind == MessageKind.Request)
            {
                _out.WriteLine();
                _out.WriteLine($"    responder:  arc respond {message.Id} --body-file <fichero>");
            }
        }
        return ExitCodes.Ok;
    }

    private async Task<int> RespondAsync()
    {
        if (_flags.Positional.FirstOrDefault() is not { Length: > 0 } requestId)
        {
            return Fail("Uso: arc respond <request_id> --body-file <fichero>", ExitCodes.Usage);
        }

        if (ReadBody() is not { } body)
        {
            return ExitCodes.Usage;
        }

        JsonObject payload = new JsonObject { ["body"] = body };
        if (!TryReadRefs(out JsonNode? refs))
        {
            return ExitCodes.Usage;
        }

        if (refs is not null)
        {
            payload["refs"] = refs;
        }

        HttpResponseMessage response = await _http.PostAsync($"/v1/requests/{requestId}/response", Json(payload));
        string text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            return FailHttp(response, text);
        }

        if (_asJson)
        {
            _out.WriteLine(text);
        }
        else
        {
            _out.WriteLine($"Respuesta entregada a la petición {requestId}.");
        }

        return ExitCodes.Ok;
    }

    private async Task<int> NoteAsync()
    {
        string? to = _flags.Value("to");
        if (string.IsNullOrWhiteSpace(to))
        {
            return Fail("Falta --to <agente>.", ExitCodes.Usage);
        }

        if (ReadBody() is not { } body)
        {
            return ExitCodes.Usage;
        }

        JsonObject payload = new JsonObject { ["to"] = to, ["body"] = body };
        if (_flags.Value("subject") is { Length: > 0 } subject)
        {
            payload["subject"] = subject;
        }

        if (_flags.Value("thread") is { Length: > 0 } thread)
        {
            payload["thread_id"] = thread;
        }

        if (!TryReadRefs(out JsonNode? refs))
        {
            return ExitCodes.Usage;
        }

        if (refs is not null)
        {
            payload["refs"] = refs;
        }

        HttpResponseMessage response = await _http.PostAsync("/v1/notes", Json(payload));
        string text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            return FailHttp(response, text);
        }

        if (_asJson)
        {
            _out.WriteLine(text);
        }
        else
        {
            _out.WriteLine($"Aviso enviado a {to}.");
        }

        return ExitCodes.Ok;
    }

    private async Task<int> ThreadAsync()
    {
        if (_flags.Positional.FirstOrDefault() is not { Length: > 0 } threadId)
        {
            return Fail("Uso: arc thread <thread_id>", ExitCodes.Usage);
        }

        HttpResponseMessage response = await _http.GetAsync($"/v1/threads/{threadId}");
        string text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            return FailHttp(response, text);
        }

        if (_asJson)
        {
            _out.WriteLine(text);
            return ExitCodes.Ok;
        }

        List<Message> messages = Deserialize<List<Message>>(text) ?? [];
        _out.WriteLine($"hilo {threadId} · {messages.Count} mensaje(s)");
        foreach (Message message in messages)
        {
            _out.WriteLine();
            _out.WriteLine($"{message.CreatedAt.ToLocalTime():HH:mm:ss}  {message.From} -> {message.To}  ({message.Kind.ToString().ToLowerInvariant()})");
            foreach (string line in message.Body.ReplaceLineEndings("\n").Split('\n'))
            {
                _out.WriteLine("    " + line);
            }
        }
        return ExitCodes.Ok;
    }

    private async Task<int> GetAsync(string path)
    {
        HttpResponseMessage response = await _http.GetAsync(path);
        string text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            return FailHttp(response, text);
        }

        _out.WriteLine(text);
        return ExitCodes.Ok;
    }

    // ---------- Auxiliares ----------

    // El cuerpo entra por fichero o por stdin, nunca por argv salvo petición expresa:
    // en Windows los argumentos pasan por la codepage ANSI y destrozan los acentos.
    private string? ReadBody()
    {
        string? file = _flags.Value("body-file");
        if (file is not null)
        {
            if (file == "-")
            {
                return _in.ReadToEnd();
            }

            if (!File.Exists(file))
            {
                _err.WriteLine($"No existe el fichero: {file}");
                return null;
            }
            return File.ReadAllText(file, Encoding.UTF8);
        }

        if (_flags.Value("body") is { } inline)
        {
            return inline;
        }

        if (_flags.Has("stdin"))
        {
            return _in.ReadToEnd();
        }

        _err.WriteLine("Falta el cuerpo. Usa --body-file <fichero>, --body-file - (stdin) o --body \"texto\".");
        return null;
    }

    // Devuelve false si el llamante pidió refs y no se pudieron leer. Un único `null` para
    // "no me diste refs" y para "lo que me diste no vale" hacía que el mensaje saliera sin la
    // rama ni el commit, con código 0 y un agente convencido de haberlos enviado.
    private bool TryReadRefs(out JsonNode? refs)
    {
        refs = null;
        string? raw;

        if (_flags.Value("refs-file") is { } file)
        {
            if (!File.Exists(file))
            {
                _err.WriteLine($"No existe el fichero: {file}");
                return false;
            }
            raw = File.ReadAllText(file, Encoding.UTF8);
        }
        else
        {
            raw = _flags.Value("refs");
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        try
        {
            refs = JsonNode.Parse(raw);
            return true;
        }
        catch (JsonException exception)
        {
            _err.WriteLine($"--refs no es JSON válido: {exception.Message}");
            return false;
        }
    }

    private static StringContent Json(JsonNode payload) =>
        new(payload.ToJsonString(), new UTF8Encoding(false), "application/json");

    private static T? Deserialize<T>(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(text, ArcJson.Options);
        }
        catch (JsonException) { return default; }
    }

    private static string Ago(DateTimeOffset moment)
    {
        TimeSpan elapsed = DateTimeOffset.UtcNow - moment;
        if (elapsed.TotalSeconds < 60)
        {
            return $"hace {(int)elapsed.TotalSeconds}s";
        }

        if (elapsed.TotalMinutes < 60)
        {
            return $"hace {(int)elapsed.TotalMinutes}min";
        }

        if (elapsed.TotalHours < 24)
        {
            return $"hace {(int)elapsed.TotalHours}h";
        }

        return moment.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
    }

    private int Fail(string message, int code)
    {
        _err.WriteLine(message);
        return code;
    }

    private int FailHttp(HttpResponseMessage response, string text)
    {
        ErrorBody? detail = Deserialize<ErrorBody>(text);
        _err.WriteLine(detail is null
            ? $"El hub respondió {(int)response.StatusCode}: {text}"
            : $"El hub respondió {(int)response.StatusCode} ({detail.Error}): {detail.Detail}");
        return 1;
    }
}
