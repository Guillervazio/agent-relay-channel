namespace Arc.Hub;

/// <summary>
/// Lo que el hub necesita saber antes de escuchar nada. Existe para que la
/// configuración entre por la puerta: leída del entorno en producción, escrita a
/// mano en un test, sin que ninguno de los dos tenga que enterarse del otro.
/// </summary>
public sealed record HubOptions
{
    /// <summary>Fichero SQLite. Se crea si no existe.</summary>
    public required string DatabasePath { get; init; }

    /// <summary>Secreto compartido. <c>null</c> deja el canal sin autenticar, y entonces sólo debe escucharse en loopback.</summary>
    public string? Token { get; init; }

    /// <summary>Tope de segundos por espera. Del que se deriva el keep-alive de Kestrel.</summary>
    public int MaxWaitSeconds { get; init; } = 300;

    /// <summary>Direcciones de escucha separadas por <c>;</c>.</summary>
    public string Urls { get; init; } = "http://0.0.0.0:8765";

    /// <summary>El reloj de todo el proceso: uno solo, compartido por el store, el canal y el flujo de eventos.</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;
}
