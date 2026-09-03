using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arc.Core;

public enum MessageKind { Request, Response, Note }

public enum MessageStatus { Pending, Delivered, Answered, Expired }

/// <summary>Un mensaje del canal. Inmutable: los cambios de estado se hacen en el almacén.</summary>
public sealed record Message
{
    public required string Id { get; init; }
    public required string ThreadId { get; init; }
    public required string From { get; init; }
    public required string To { get; init; }
    public required MessageKind Kind { get; init; }
    public string? Subject { get; init; }
    public required string Body { get; init; }

    /// <summary>Referencias al repositorio (rama, commit, rutas). Nunca artefactos.</summary>
    public JsonElement? Refs { get; init; }

    public MessageStatus Status { get; init; } = MessageStatus.Pending;

    /// <summary>En una respuesta, el Id del request que contesta.</summary>
    public string? CorrelationId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? AnsweredAt { get; init; }
}

public sealed record AgentInfo
{
    public required string Id { get; init; }
    public string? Provider { get; init; }
    public string? Host { get; init; }
    public DateTimeOffset LastSeen { get; init; }
    public int MessagesSent { get; init; }
}

/// <summary>
/// Una conversación vista desde fuera: lo justo para listarla sin traerse los cuerpos.
/// Es lo que el panel necesita para dejar elegir un hilo entre todos los del canal.
/// </summary>
public sealed record ThreadSummary
{
    public required string ThreadId { get; init; }

    /// <summary>El asunto con el que arrancó el hilo; puede no haberlo.</summary>
    public string? Subject { get; init; }

    /// <summary>Quienes aparecen en el hilo, como emisor o como destinatario.</summary>
    public required IReadOnlyList<string> Participants { get; init; }

    public required int Messages { get; init; }

    /// <summary>Preguntas del hilo que siguen sin respuesta.</summary>
    public required int OpenRequests { get; init; }

    /// <summary>
    /// Terminada: no queda ninguna pregunta esperando. Un hilo de puros avisos no deja
    /// nada abierto, así que nace terminado — nadie va a contestarlo.
    /// </summary>
    public bool Closed => OpenRequests == 0;

    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset LastAt { get; init; }
}

// ---------- Contratos HTTP ----------

public sealed record CreateRequestBody
{
    public string? To { get; init; }
    public string? Subject { get; init; }
    public string? Body { get; init; }
    public JsonElement? Refs { get; init; }
    public string? ThreadId { get; init; }
}

public sealed record CreateResponseBody
{
    public string? Body { get; init; }
    public JsonElement? Refs { get; init; }
}

public sealed record AskResult
{
    /// <summary>answered | timeout</summary>
    public required string Outcome { get; init; }
    public required string RequestId { get; init; }
    public required string ThreadId { get; init; }
    public Message? Response { get; init; }
}

public sealed record InboxResult
{
    public required IReadOnlyList<Message> Messages { get; init; }
    public required string Agent { get; init; }
}

public sealed record ErrorBody(string Error, string? Detail = null);

public static class ArcJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    /// <summary>
    /// Igual, pero en una sola línea. Obligatorio en SSE: un salto de línea dentro de
    /// <c>data:</c> cortaría el evento por la mitad.
    /// </summary>
    public static readonly JsonSerializerOptions Compact = new(Options) { WriteIndented = false };
}
