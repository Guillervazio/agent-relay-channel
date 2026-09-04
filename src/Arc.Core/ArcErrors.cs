namespace Arc.Core;

/// <summary>
/// Los códigos de error del canal, definidos una sola vez. Son contrato publicado:
/// la tabla de <c>docs/PROTOCOL.md</c> es la copia que ve un cliente, y un código
/// no cambia de significado una vez publicado — uno nuevo toma un nombre nuevo.
/// </summary>
/// <remarks>
/// Un test que compruebe uno de estos escribe el literal, no la constante: una
/// aserción contra la constante pasa justamente cuando alguien la cambia, que es
/// la rotura que existe para detectar.
/// </remarks>
public static class ArcErrors
{
    /// <summary><c>X-ARC-Token</c> ausente o incorrecto.</summary>
    public const string Unauthorized = "unauthorized";

    /// <summary><c>X-ARC-Agent</c> ausente o mal formado.</summary>
    public const string BadAgent = "bad_agent";

    /// <summary>El destinatario (<c>to</c>) está ausente o mal formado.</summary>
    public const string BadRecipient = "bad_recipient";

    /// <summary>Falta el cuerpo del mensaje.</summary>
    public const string EmptyBody = "empty_body";

    /// <summary>El cuerpo supera <see cref="MessageStore.MaxBodyBytes"/>.</summary>
    public const string BodyTooLarge = "body_too_large";

    /// <summary>El cuerpo de la petición no es JSON válido, o no llegó como UTF-8.</summary>
    public const string InvalidJson = "invalid_json";

    /// <summary><c>refs</c> no es un objeto JSON válido.</summary>
    public const string InvalidRefs = "invalid_refs";

    /// <summary>Un agente se escribe a sí mismo.</summary>
    public const string SelfAddressed = "self_addressed";

    /// <summary>Buzón ajeno, o responder algo que no va dirigido a ti.</summary>
    public const string Forbidden = "forbidden";

    /// <summary>No existe esa petición, ese mensaje o ese hilo.</summary>
    public const string NotFound = "not_found";

    /// <summary>Esa petición ya tiene respuesta.</summary>
    public const string AlreadyAnswered = "already_answered";
}
