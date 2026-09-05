using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Arc.Core;

/// <summary>Fallo con significado para el llamante: se traduce a HTTP o a texto de herramienta MCP.</summary>
public sealed class ChannelException(string code, string detail, int status) : Exception(detail)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

/// <summary>
/// Las reglas del canal, en un solo sitio. REST y MCP son dos fachadas sobre
/// esto: si la lógica viviera en los endpoints, ambas acabarían divergiendo.
/// </summary>
public sealed class ChannelService(MessageStore store, WaiterRegistry registry, int maxWaitSeconds = 300,
    EventStream? events = null, TimeProvider? time = null)
{
    // El reloj entra por la puerta para que un test pueda fijarlo; en producción es el del sistema.
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>Nombres de agente acotados: son claves del registro de esperas.</summary>
    public static readonly Regex AgentNamePattern = new("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.Compiled);

    public int MaxWaitSeconds { get; } = maxWaitSeconds;

    public MessageStore Store => store;
    public WaiterRegistry Registry => registry;

    /// <summary>Difusión hacia observadores pasivos. Nunca altera el curso de una operación.</summary>
    public EventStream? Events => events;

    public async Task<AskResult> AskAsync(
        string from, string? to, string? body, string? subject, JsonElement? refs,
        string? threadId, int? wait, CancellationToken ct = default)
    {
        ValidateAgent(to, ArcErrors.BadRecipient);
        if (to == from)
        {
            throw new ChannelException(ArcErrors.SelfAddressed, "Un agente no puede enviarse peticiones a sí mismo.", 422);
        }

        ValidateBody(body);

        // Antes de insertar: un 'wait' fuera de rango no debe dejar la petición
        // creada en el canal y contestar 422 a quien la creó.
        int seconds = ValidateWait(wait);

        string requestId = "req_" + Guid.NewGuid().ToString("n")[..16];
        string thread = Blank(threadId) ?? "thr_" + Guid.NewGuid().ToString("n")[..16];

        // Registrar la espera ANTES de insertar: si el destinatario contesta de
        // inmediato, la señal encuentra al waiter ya montado y no se pierde.
        using Waiter waiter = registry.Register(WaiterRegistry.ResponseKey(requestId));

        Message message = new Message
        {
            Id = requestId,
            ThreadId = thread,
            From = from,
            To = to!,
            Kind = MessageKind.Request,
            Subject = Blank(subject),
            Body = body!,
            Refs = refs,
            Status = MessageStatus.Pending,
            CreatedAt = _time.GetUtcNow()
        };

        await store.AddAsync(message, ct);
        await store.TouchAgentAsync(from, null, null, sentMessage: true, ct: ct);
        registry.Signal(WaiterRegistry.InboxKey(to!), message);
        events?.PublishMessage(message);

        if (seconds == 0)
        {
            return new AskResult { Outcome = "queued", RequestId = requestId, ThreadId = thread };
        }

        Message? response = await waiter.WaitAsync(TimeSpan.FromSeconds(seconds), ct)
                       ?? await store.GetResponseForAsync(requestId, ct);

        return response is null
            // La petición sigue viva: el destinatario puede contestarla más tarde.
            ? new AskResult { Outcome = "timeout", RequestId = requestId, ThreadId = thread }
            : new AskResult { Outcome = "answered", RequestId = requestId, ThreadId = thread, Response = response };
    }

    public async Task<AskResult> AwaitResponseAsync(string caller, string requestId, int? wait, CancellationToken ct = default)
    {
        int seconds = ValidateWait(wait);

        Message? request = await store.GetAsync(requestId, ct);
        if (request is null || request.Kind != MessageKind.Request)
        {
            throw new ChannelException(ArcErrors.NotFound, "No existe esa petición.", 404);
        }

        if (request.From != caller)
        {
            throw new ChannelException(ArcErrors.Forbidden, "Sólo el emisor puede esperar esta respuesta.", 403);
        }

        using Waiter waiter = registry.Register(WaiterRegistry.ResponseKey(requestId));

        Message? response = await store.GetResponseForAsync(requestId, ct);
        if (response is null && seconds > 0)
        {
            response = await waiter.WaitAsync(TimeSpan.FromSeconds(seconds), ct)
                       ?? await store.GetResponseForAsync(requestId, ct);
        }

        return response is null
            ? new AskResult { Outcome = "timeout", RequestId = requestId, ThreadId = request.ThreadId }
            : new AskResult { Outcome = "answered", RequestId = requestId, ThreadId = request.ThreadId, Response = response };
    }

    public async Task<Message> RespondAsync(
        string from, string requestId, string? body, JsonElement? refs, CancellationToken ct = default)
    {
        ValidateBody(body);

        Message? request = await store.GetAsync(requestId, ct);
        if (request is null || request.Kind != MessageKind.Request)
        {
            throw new ChannelException(ArcErrors.NotFound, "No existe esa petición.", 404);
        }

        if (request.To != from)
        {
            throw new ChannelException(ArcErrors.Forbidden, $"Esta petición va dirigida a '{request.To}'.", 403);
        }

        // Comprobación temprana para no construir una respuesta que se va a tirar.
        // No es la que decide: la que decide está en el WHERE del store.
        if (request.Status == MessageStatus.Answered)
        {
            throw new ChannelException(ArcErrors.AlreadyAnswered, "Esa petición ya tiene respuesta.", 409);
        }

        Message response = new Message
        {
            Id = "res_" + Guid.NewGuid().ToString("n")[..16],
            ThreadId = request.ThreadId,
            From = from,
            To = request.From,
            Kind = MessageKind.Response,
            Subject = request.Subject,
            Body = body!,
            Refs = refs,
            Status = MessageStatus.Pending,
            CorrelationId = requestId,
            CreatedAt = _time.GetUtcNow()
        };

        if (!await store.AddResponseAsync(response, ct))
        {
            throw new ChannelException(ArcErrors.AlreadyAnswered, "Esa petición ya tiene respuesta.", 409);
        }

        await store.TouchAgentAsync(from, null, null, sentMessage: true, ct: ct);

        registry.Signal(WaiterRegistry.ResponseKey(requestId), response);
        // Si el emisor ya dejó de esperar, la recogerá por su buzón.
        registry.Signal(WaiterRegistry.InboxKey(request.From), response);
        events?.PublishMessage(response);

        return response;
    }

    public async Task<Message> NoteAsync(
        string from, string? to, string? body, string? subject, JsonElement? refs,
        string? threadId, CancellationToken ct = default)
    {
        ValidateAgent(to, ArcErrors.BadRecipient);
        ValidateBody(body);

        Message note = new Message
        {
            Id = "not_" + Guid.NewGuid().ToString("n")[..16],
            ThreadId = Blank(threadId) ?? "thr_" + Guid.NewGuid().ToString("n")[..16],
            From = from,
            To = to!,
            Kind = MessageKind.Note,
            Subject = Blank(subject),
            Body = body!,
            Refs = refs,
            Status = MessageStatus.Pending,
            CreatedAt = _time.GetUtcNow()
        };

        await store.AddAsync(note, ct);
        await store.TouchAgentAsync(from, null, null, sentMessage: true, ct: ct);
        registry.Signal(WaiterRegistry.InboxKey(to!), note);
        events?.PublishMessage(note);

        return note;
    }

    /// <summary>Buzón propio. Marca como entregado lo que devuelve.</summary>
    public async Task<IReadOnlyList<Message>> InboxAsync(
        string caller, string agent, bool includeUnanswered, int? wait, CancellationToken ct = default)
    {
        int seconds = ValidateWait(wait);

        if (agent != caller)
        {
            throw new ChannelException(ArcErrors.Forbidden, "Un agente sólo puede leer su propio buzón.", 403);
        }

        // Waiter primero, consulta después: así no se cuela un mensaje entre ambas.
        using Waiter waiter = registry.Register(WaiterRegistry.InboxKey(agent));

        IReadOnlyList<Message> messages = await store.GetInboxAsync(agent, includeUnanswered, ct);
        if (messages.Count == 0 && seconds > 0)
        {
            if (await waiter.WaitAsync(TimeSpan.FromSeconds(seconds), ct) is not null)
            {
                messages = await store.GetInboxAsync(agent, includeUnanswered, ct);
            }
        }

        if (messages.Count > 0)
        {
            List<string> justDelivered = messages.Where(m => m.Status == MessageStatus.Pending).Select(m => m.Id).ToList();
            await store.MarkDeliveredAsync(justDelivered, ct);
            events?.PublishDelivered(justDelivered);
        }

        return messages;
    }

    /// <summary>
    /// Un mensaje suelto, para sus dos extremos y para nadie más. Un mensaje ajeno y uno
    /// inexistente contestan lo mismo: un 403 confirmaría que ese identificador existe, que
    /// es justo lo que el llamante no tenía derecho a saber — H011, del que
    /// <c>InboxAsync</c> se aparta porque los nombres de agente ya son públicos y un
    /// identificador de mensaje no lo es.
    /// </summary>
    public async Task<Message> MessageAsync(string caller, string id, CancellationToken ct = default)
    {
        Message? message = await store.GetAsync(id, ct);
        if (message is null || (message.From != caller && message.To != caller))
        {
            throw new ChannelException(ArcErrors.NotFound, "No existe ese mensaje.", 404);
        }

        return message;
    }

    /// <summary>
    /// El hilo recortado a las filas del llamante, no el hilo entero de quien aparezca en él:
    /// cualquiera que conozca un identificador de hilo puede entrar en él mandando un aviso,
    /// así que participar no puede ser lo que dé derecho a leer lo anterior.
    /// </summary>
    public async Task<IReadOnlyList<Message>> ThreadAsync(string caller, string threadId, CancellationToken ct = default)
    {
        IReadOnlyList<Message> messages = await store.GetThreadAsync(threadId, ct);
        List<Message> mine = messages.Where(m => m.From == caller || m.To == caller).ToList();

        // Mismo texto que un hilo que no existe: distinguirlos por el detalle filtraría
        // por la prosa lo que el código de estado existe para no decir.
        if (mine.Count == 0)
        {
            throw new ChannelException(ArcErrors.NotFound, "No existe ese hilo.", 404);
        }

        return mine;
    }

    /// <summary>
    /// Segundos de espera pedidos, comprobados contra el máximo. Rechaza en vez de
    /// recortar: una espera acortada en silencio vuelve con un <c>outcome</c>
    /// indistinguible de un plazo agotado de verdad, y el llamante no se entera.
    /// </summary>
    public int ValidateWait(int? requested)
    {
        int seconds = requested ?? 0;
        if (seconds < 0 || seconds > MaxWaitSeconds)
        {
            throw new ChannelException(ArcErrors.InvalidWait,
                $"'wait' va entre 0 y {MaxWaitSeconds} segundos.", 422);
        }

        return seconds;
    }

    public static void ValidateAgent(string? name, string code)
    {
        if (string.IsNullOrWhiteSpace(name) || !AgentNamePattern.IsMatch(name))
        {
            throw new ChannelException(code,
                "Nombre de agente ausente o inválido. Formato: minúsculas, dígitos, punto, guion o guion bajo (máx. 64).", 422);
        }
    }

    private static void ValidateBody(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            throw new ChannelException(ArcErrors.EmptyBody, "El cuerpo del mensaje es obligatorio.", 422);
        }

        if (Encoding.UTF8.GetByteCount(body) > MessageStore.MaxBodyBytes)
        {
            throw new ChannelException(ArcErrors.BodyTooLarge,
                $"Máximo {MessageStore.MaxBodyBytes / 1024} KB. Pasa una referencia al repositorio en 'refs' en vez del contenido.", 422);
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
