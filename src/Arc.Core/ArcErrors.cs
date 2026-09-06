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

    /// <summary>
    /// <c>refs</c> no se pudo leer como JSON. <c>refs</c> es cualquier valor JSON, no sólo un
    /// objeto, así que esto no habla de su forma sino de que no pudo parsearse.
    /// </summary>
    /// <remarks>
    /// Sólo MCP puede emitirlo, y es la forma del cable y no un olvido: allí <c>refs</c> llega
    /// como cadena aparte y falla sola. En REST viaja dentro del cuerpo, de modo que unas refs
    /// rotas son un cuerpo roto y contestan <see cref="InvalidJson"/>; el CLI las lee antes de
    /// enviar nada y sale con 2 sin llegar al hub.
    /// </remarks>
    public const string InvalidRefs = "invalid_refs";

    /// <summary><c>wait</c> fuera del rango que admite el hub.</summary>
    public const string InvalidWait = "invalid_wait";

    /// <summary>
    /// <c>replay</c> fuera del rango que admite el hub. Es un código propio y no
    /// <see cref="InvalidWait"/> porque acota otra cosa: <c>wait</c> mide cuánto se retiene una
    /// conexión hacia adelante, <c>replay</c> cuánto se mira hacia atrás, y un cliente que pide
    /// los dos necesita saber cuál de ellos le rechazaron.
    /// </summary>
    public const string InvalidReplay = "invalid_replay";

    /// <summary>
    /// Un agente pide esperar su propia respuesta. Escribirse a sí mismo sí vale — un aviso
    /// siempre pudo, y una petición con <c>wait</c> a 0 queda en el buzón del que la manda —;
    /// lo que no vale es bloquearse esperando a quien está bloqueado.
    /// </summary>
    public const string SelfAddressed = "self_addressed";

    /// <summary>Buzón ajeno, o responder algo que no va dirigido a ti.</summary>
    public const string Forbidden = "forbidden";

    /// <summary>No existe esa petición, ese mensaje o ese hilo.</summary>
    public const string NotFound = "not_found";

    /// <summary>Esa petición ya tiene respuesta.</summary>
    public const string AlreadyAnswered = "already_answered";
}
