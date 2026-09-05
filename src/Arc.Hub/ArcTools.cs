using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Arc.Core;
using ModelContextProtocol.Server;

namespace Arc.Hub;

/// <summary>
/// El canal visto como herramientas nativas del agente. Misma lógica que REST
/// (<see cref="ChannelService"/>); cambia sólo la forma de entrar y de contar el resultado:
/// aquí la salida la lee un modelo, así que es texto con el siguiente paso a la vista.
/// </summary>
[McpServerToolType]
public sealed class ArcTools
{
    internal const string AgentKey = "arc.agent";

    /// <summary>Identidad del llamante, fijada por el middleware desde la cabecera X-ARC-Agent.</summary>
    internal static string Caller(HttpContext context) => (string)context.Items[AgentKey]!;

    private static string Caller(IHttpContextAccessor accessor) =>
        accessor.HttpContext is { } context && context.Items.TryGetValue(AgentKey, out object? agent) && agent is string name
            ? name
            : throw new ChannelException(ArcErrors.BadAgent,
                "Este servidor MCP no sabe quién eres. Añade la cabecera X-ARC-Agent en la configuración del cliente.", 422);

    [McpServerTool(Name = "arc_ask")]
    [Description("Pregunta algo a otro agente y espera su respuesta. Bloquea hasta que conteste o venza el plazo. " +
                 "Úsala cuando necesites la respuesta para continuar tu trabajo.")]
    public static async Task<string> AskAsync(
        ChannelService channel,
        IHttpContextAccessor accessor,
        [Description("Identificador del agente destinatario, por ejemplo 'codex-pc2'.")] string to,
        [Description("La pregunta o encargo, en texto o markdown.")] string body,
        [Description("Asunto breve que resuma la petición.")] string? subject = null,
        [Description("Segundos a esperar por la respuesta. 120 por defecto, 300 como máximo.")] int wait = 120,
        [Description("Referencias al repositorio como objeto JSON, por ejemplo {\"branch\":\"feat/pagos\",\"commit\":\"a1b2c3d\"}. " +
                     "Envía referencias, nunca el contenido de los ficheros.")] string? refs = null,
        [Description("Hilo existente al que encadenar esta petición. Omítelo para abrir uno nuevo.")] string? threadId = null,
        CancellationToken cancellationToken = default)
    {
        AskResult result = await channel.AskAsync(Caller(accessor), to, body, subject, ParseRefs(refs), threadId, wait, cancellationToken);

        if (result.Outcome == "answered" && result.Response is { } answer)
        {
            StringBuilder text = new StringBuilder()
                .AppendLine($"{answer.From} respondió (petición {result.RequestId}, hilo {result.ThreadId}):")
                .AppendLine();
            if (answer.Refs is { } answerRefs)
            {
                text.AppendLine($"refs: {answerRefs.GetRawText()}").AppendLine();
            }

            return text.Append(answer.Body).ToString();
        }

        return $"""
            {to} no respondió en {wait}s. La petición sigue viva y él la verá en su buzón.
            Retómala más tarde con arc_await sobre {result.RequestId}, o sigue con otra cosa mientras tanto.
            """;
    }

    [McpServerTool(Name = "arc_await")]
    [Description("Retoma la espera de una petición que ya venció antes, sin volver a enviarla.")]
    public static async Task<string> AwaitAsync(
        ChannelService channel,
        IHttpContextAccessor accessor,
        [Description("Identificador de la petición, del tipo 'req_1a2b3c'.")] string requestId,
        [Description("Segundos a esperar. 120 por defecto, 300 como máximo.")] int wait = 120,
        CancellationToken cancellationToken = default)
    {
        AskResult result = await channel.AwaitResponseAsync(Caller(accessor), requestId, wait, cancellationToken);

        return result.Outcome == "answered" && result.Response is { } answer
            ? $"{answer.From} respondió (hilo {result.ThreadId}):\n\n{answer.Body}"
            : $"{requestId} sigue sin respuesta tras {wait}s.";
    }

    [McpServerTool(Name = "arc_inbox")]
    [Description("Lee los mensajes dirigidos a ti. Con 'wait' se queda esperando a que llegue alguno, " +
                 "en vez de devolver el buzón vacío al instante.")]
    public static async Task<string> InboxAsync(
        ChannelService channel,
        IHttpContextAccessor accessor,
        [Description("Segundos a esperar si el buzón está vacío. 0 para mirar y volver enseguida.")] int wait = 0,
        [Description("Incluir también las peticiones que ya leíste pero aún no has respondido.")] bool unanswered = false,
        CancellationToken cancellationToken = default)
    {
        string me = Caller(accessor);
        IReadOnlyList<Message> messages = await channel.InboxAsync(me, me, unanswered, wait, cancellationToken);

        if (messages.Count == 0)
        {
            return $"No hay mensajes para {me}.";
        }

        StringBuilder text = new StringBuilder($"{messages.Count} mensaje(s) para {me}:");
        foreach (Message message in messages)
        {
            text.AppendLine().AppendLine()
                .AppendLine($"--- {message.Kind.ToString().ToLowerInvariant()} {message.Id} · de {message.From} · hilo {message.ThreadId} ---");
            if (message.Subject is { Length: > 0 } subject)
            {
                text.AppendLine($"asunto: {subject}");
            }

            if (message.Refs is { } refs)
            {
                text.AppendLine($"refs: {refs.GetRawText()}");
            }

            text.AppendLine().AppendLine(message.Body);
            if (message.Kind == MessageKind.Request)
            {
                text.Append($"(contesta con arc_respond sobre {message.Id}; puede que te esté esperando ahora mismo)");
            }
        }
        return text.ToString();
    }

    [McpServerTool(Name = "arc_respond")]
    [Description("Contesta una petición que te llegó. Si el emisor sigue esperando, la recibe al instante.")]
    public static async Task<string> RespondAsync(
        ChannelService channel,
        IHttpContextAccessor accessor,
        [Description("Identificador de la petición que contestas, del tipo 'req_1a2b3c'.")] string requestId,
        [Description("Tu respuesta, en texto o markdown.")] string body,
        [Description("Referencias al repositorio como objeto JSON, por ejemplo {\"commit\":\"a1b2c3d\",\"files\":[\"src/x.cs\"]}.")] string? refs = null,
        CancellationToken cancellationToken = default)
    {
        Message response = await channel.RespondAsync(Caller(accessor), requestId, body, ParseRefs(refs), cancellationToken);
        return $"Respuesta entregada a {response.To} (petición {requestId}, hilo {response.ThreadId}).";
    }

    [McpServerTool(Name = "arc_note")]
    [Description("Avisa a otro agente sin esperar respuesta. Para dar por hecho algo, no para preguntar.")]
    public static async Task<string> NoteAsync(
        ChannelService channel,
        IHttpContextAccessor accessor,
        [Description("Identificador del agente destinatario.")] string to,
        [Description("El aviso, en texto o markdown.")] string body,
        [Description("Asunto breve.")] string? subject = null,
        [Description("Referencias al repositorio como objeto JSON.")] string? refs = null,
        [Description("Hilo existente al que encadenar el aviso.")] string? threadId = null,
        CancellationToken cancellationToken = default)
    {
        Message note = await channel.NoteAsync(Caller(accessor), to, body, subject, ParseRefs(refs), threadId, cancellationToken);
        return $"Aviso enviado a {to} (hilo {note.ThreadId}). Lo verá la próxima vez que mire su buzón.";
    }

    [McpServerTool(Name = "arc_thread")]
    [Description("Muestra en orden tus mensajes de una conversación, para recuperar el contexto de un intercambio anterior. " +
                 "Sólo los tuyos: de un hilo en el que no apareces dice que no existe.")]
    public static async Task<string> ThreadAsync(
        ChannelService channel,
        IHttpContextAccessor accessor,
        [Description("Identificador del hilo, del tipo 'thr_1a2b3c'.")] string threadId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Message> messages;
        try
        {
            messages = await channel.ThreadAsync(Caller(accessor), threadId, cancellationToken);
        }
        catch (ChannelException refusal) when (refusal.Code == ArcErrors.NotFound)
        {
            // Una sola frase para los dos casos: el hilo ajeno y el que no existe se
            // contestan igual, y decirle al modelo cuál de los dos fue sería decírselo.
            return $"No existe el hilo {threadId}, o no tienes ningún mensaje en él.";
        }

        StringBuilder text = new StringBuilder($"Hilo {threadId} · {messages.Count} mensaje(s):");
        foreach (Message message in messages)
        {
            text.AppendLine().AppendLine()
                .AppendLine($"[{message.CreatedAt.ToLocalTime():HH:mm:ss}] {message.From} -> {message.To} ({message.Kind.ToString().ToLowerInvariant()})")
                .Append(message.Body);
        }
        return text.ToString();
    }

    [McpServerTool(Name = "arc_agents")]
    [Description("Lista los agentes que ha visto el canal, con su proveedor y cuándo aparecieron por última vez. " +
                 "Úsala si no sabes a quién dirigirte.")]
    public static async Task<string> AgentsAsync(ChannelService channel, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AgentInfo> agents = await channel.Store.ListAgentsAsync(cancellationToken);
        if (agents.Count == 0)
        {
            return "Todavía no se ha conectado ningún agente.";
        }

        StringBuilder text = new StringBuilder("Agentes conocidos:");
        foreach (AgentInfo agent in agents)
        {
            text.AppendLine().Append($"  {agent.Id}");
            if (agent.Provider is { Length: > 0 } provider)
            {
                text.Append($" · {provider}");
            }

            text.Append($" · visto {agent.LastSeen.ToLocalTime():dd/MM HH:mm} · {agent.MessagesSent} mensaje(s) enviados");
        }
        return text.ToString();
    }

    private static JsonElement? ParseRefs(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(raw).RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new ChannelException(ArcErrors.InvalidRefs, $"'refs' debe ser un objeto JSON válido: {exception.Message}", 422);
        }
    }
}
