using System.Collections.Concurrent;

namespace Arc.Core;

/// <summary>
/// Registro de esperas en memoria. Es lo que convierte el canal en petición/respuesta
/// bloqueante sin sondear la base de datos: quien escribe un mensaje despierta
/// directamente al que estaba esperando.
///
/// Claves usadas por el hub:
///   inbox:{agent}     alguien tiene correo nuevo
///   resp:{requestId}  alguien contestó a este request
/// </summary>
public sealed class WaiterRegistry
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, TaskCompletionSource<Message?>>> _keys = new();

    /// <summary>
    /// Registrar y desregistrar son atómicos entre sí. Cada operación por separado lo era —
    /// los dos diccionarios son concurrentes— y la secuencia no: un <c>Register</c> que
    /// obtenía el diccionario de una clave justo antes de que <c>Unregister</c> lo desalojara
    /// por vacío insertaba su puesto en un diccionario que <c>Signal</c> ya no alcanzaba, y esa
    /// espera agotaba su plazo entero sin que nada la despertara.
    ///
    /// Reintentar en vez de bloquear deja una ventana residual: el desalojo puede ocurrir
    /// después de la comprobación. El canal mueve unas decenas de mensajes al día, así que la
    /// contención de este cerrojo no es medible y sí lo era el fallo.
    /// </summary>
    private readonly Lock _gate = new Lock();

    public static string InboxKey(string agent) => "inbox:" + agent;
    public static string ResponseKey(string requestId) => "resp:" + requestId;

    /// <summary>
    /// Reserva un puesto de espera. Registrar SIEMPRE antes de consultar la base de datos,
    /// para que un mensaje que llegue entre ambas operaciones no se pierda.
    /// </summary>
    public Waiter Register(string key)
    {
        Guid id = Guid.NewGuid();
        // RunContinuationsAsynchronously: la continuación no debe ejecutarse en el hilo
        // que está sirviendo la petición HTTP de escritura.
        TaskCompletionSource<Message?> tcs = new TaskCompletionSource<Message?>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_gate)
        {
            ConcurrentDictionary<Guid, TaskCompletionSource<Message?>> slots = _keys.GetOrAdd(key, static _ => new ConcurrentDictionary<Guid, TaskCompletionSource<Message?>>());
            slots[id] = tcs;
        }

        return new Waiter(this, key, id, tcs);
    }

    /// <summary>Despierta a todos los que esperaban esta clave.</summary>
    public int Signal(string key, Message message)
    {
        ConcurrentDictionary<Guid, TaskCompletionSource<Message?>>? slots;
        lock (_gate)
        {
            if (!_keys.TryGetValue(key, out slots))
            {
                return 0;
            }
        }

        // Despertar fuera del cerrojo: quien escribe no debe esperar a nadie para hacerlo.
        int woken = 0;
        foreach ((Guid _, TaskCompletionSource<Message?> tcs) in slots)
        {
            if (tcs.TrySetResult(message))
            {
                woken++;
            }
        }
        return woken;
    }

    internal void Unregister(string key, Guid id)
    {
        lock (_gate)
        {
            if (!_keys.TryGetValue(key, out ConcurrentDictionary<Guid, TaskCompletionSource<Message?>>? slots))
            {
                return;
            }

            slots.TryRemove(id, out _);
            if (slots.IsEmpty)
            {
                _keys.TryRemove(KeyValuePair.Create(key, slots));
            }
        }
    }

    /// <summary>Esperas activas por clave. Para /healthz: hace visible un interbloqueo mutuo.</summary>
    public IReadOnlyDictionary<string, int> Snapshot()
    {
        Dictionary<string, int> result = new Dictionary<string, int>();
        foreach ((string key, ConcurrentDictionary<Guid, TaskCompletionSource<Message?>> slots) in _keys)
        {
            int count = slots.Count;
            if (count > 0)
            {
                result[key] = count;
            }
        }
        return result;
    }
}

public sealed class Waiter : IDisposable
{
    private readonly WaiterRegistry _registry;
    private readonly string _key;
    private readonly Guid _id;
    private readonly TaskCompletionSource<Message?> _tcs;
    private int _disposed;

    internal Waiter(WaiterRegistry registry, string key, Guid id, TaskCompletionSource<Message?> tcs)
    {
        _registry = registry;
        _key = key;
        _id = id;
        _tcs = tcs;
    }

    /// <summary>Devuelve el mensaje que despertó la espera, o null si expiró o se canceló.</summary>
    public async Task<Message?> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (_tcs.Task.IsCompleted)
        {
            return await _tcs.Task.ConfigureAwait(false);
        }

        if (timeout <= TimeSpan.Zero)
        {
            return null;
        }

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        // Cancelar (por timeout o porque el cliente cortó) resuelve la espera como "sin mensaje".
        await using CancellationTokenRegistration registration = cts.Token.Register(
            static state => ((TaskCompletionSource<Message?>)state!).TrySetResult(null), _tcs);

        return await _tcs.Task.ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _tcs.TrySetResult(null);
        _registry.Unregister(_key, _id);
    }
}
