using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Arc.Core;

/// <summary>Algo que ha pasado en el canal, tal y como lo ve un observador.</summary>
public sealed record ChannelEvent
{
    /// <summary>message | delivered</summary>
    public required string Event { get; init; }

    public required DateTimeOffset At { get; init; }

    /// <summary>El mensaje recién creado, en los eventos <c>message</c>.</summary>
    public Message? Message { get; init; }

    /// <summary>Mensajes que acaban de pasar a entregados, en los eventos <c>delivered</c>.</summary>
    public IReadOnlyList<string>? Ids { get; init; }
}

/// <summary>
/// Difusión de lo que ocurre en el canal hacia observadores pasivos (el panel web).
///
/// Es deliberadamente distinto del <see cref="WaiterRegistry"/>: allí cada espera es
/// de un solo uso y despierta a un agente que está bloqueado; aquí hay N suscriptores
/// de larga duración que sólo miran. Un observador lento nunca debe frenar al canal,
/// así que cada cola es acotada y descarta lo más viejo en vez de bloquear al emisor.
/// </summary>
public sealed class EventStream(TimeProvider? time = null)
{
    private const int QueueCapacity = 256;

    private readonly ConcurrentDictionary<Guid, Channel<ChannelEvent>> _subscribers = new();
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public int SubscriberCount => _subscribers.Count;

    public Subscription Subscribe()
    {
        Channel<ChannelEvent> channel = Channel.CreateBounded<ChannelEvent>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
        Guid id = Guid.NewGuid();
        _subscribers[id] = channel;
        return new Subscription(this, id, channel.Reader);
    }

    public void Publish(ChannelEvent channelEvent)
    {
        foreach ((Guid _, Channel<ChannelEvent> channel) in _subscribers)
        {
            channel.Writer.TryWrite(channelEvent);
        }
    }

    public void PublishMessage(Message message) =>
        Publish(new ChannelEvent { Event = "message", At = _time.GetUtcNow(), Message = message });

    public void PublishDelivered(IReadOnlyList<string> ids)
    {
        if (ids.Count > 0)
        {
            Publish(new ChannelEvent { Event = "delivered", At = _time.GetUtcNow(), Ids = ids });
        }
    }

    internal void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out Channel<ChannelEvent>? channel))
        {
            channel.Writer.TryComplete();
        }
    }
}

public sealed class Subscription(EventStream stream, Guid id, ChannelReader<ChannelEvent> reader) : IDisposable
{
    public ChannelReader<ChannelEvent> Reader { get; } = reader;

    public void Dispose() => stream.Unsubscribe(id);
}
