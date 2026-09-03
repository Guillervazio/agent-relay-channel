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

    public static string InboxKey(string agent) => "inbox:" + agent;
    public static string ResponseKey(string requestId) => "resp:" + requestId;

    /// <summary>
    /// Reserva un puesto de espera. Registrar SIEMPRE antes de consultar la base de datos,
    /// para que un mensaje que llegue entre ambas operaciones no se pierda.
    /// </summary>
    public Waiter Register(string key)
    {
        var slots = _keys.GetOrAdd(key, static _ => new ConcurrentDictionary<Guid, TaskCompletionSource<Message?>>());
        Guid id = Guid.NewGuid();
        // RunContinuationsAsynchronously: la continuación no debe ejecutarse en el hilo
        // que está sirviendo la petición HTTP de escritura.
        TaskCompletionSource<Message?> tcs = new TaskCompletionSource<Message?>(TaskCreationOptions.RunContinuationsAsynchronously);
        slots[id] = tcs;
        return new Waiter(this, key, id, tcs);
    }

    /// <summary>Despierta a todos los que esperaban esta clave.</summary>
    public int Signal(string key, Message message)
    {
        if (!_keys.TryGetValue(key, out var slots))
        {
            return 0;
        }

        var woken = 0;
        foreach (var (_, tcs) in slots)
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
        if (!_keys.TryGetValue(key, out var slots))
        {
            return;
        }

        slots.TryRemove(id, out _);
        if (slots.IsEmpty)
        {
            _keys.TryRemove(KeyValuePair.Create(key, slots));
        }
    }

    /// <summary>Esperas activas por clave. Para /healthz: hace visible un interbloqueo mutuo.</summary>
    public IReadOnlyDictionary<string, int> Snapshot()
    {
        Dictionary<string, int> result = new Dictionary<string, int>();
        foreach (var (key, slots) in _keys)
        {
            var count = slots.Count;
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
        await using var registration = cts.Token.Register(
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
