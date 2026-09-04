namespace Arc.Cli;

/// <summary>
/// Los códigos de salida de <c>arc</c>. Son contrato publicado: docs/PROTOCOL.md
/// los documenta y docs/AGENTS.md le dice a un agente que ramifique sobre ellos,
/// de modo que un código nunca cambia de significado y un estado nuevo toma un
/// número nuevo.
/// </summary>
internal static class ExitCodes
{
    /// <summary>La operación terminó como se pedía.</summary>
    public const int Ok = 0;

    /// <summary>El hub respondió un error, o no se pudo hablar con él.</summary>
    public const int Error = 1;

    /// <summary>La línea de órdenes está mal formada. No se llegó a llamar al hub.</summary>
    public const int Usage = 2;

    /// <summary>La espera terminó sin respuesta. La petición sigue viva en el buzón.</summary>
    public const int Timeout = 3;

    /// <summary>No había nada que entregar. No es un fallo.</summary>
    public const int Empty = 4;
}
