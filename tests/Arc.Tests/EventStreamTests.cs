using Arc.Core;

namespace Arc.Tests;

/// <summary>
/// La difusión hacia el panel. No tenía ningún test, y `ChannelServiceTests`
/// construye el canal <em>sin</em> flujo de eventos, así que sus ramas de
/// publicación no se ejecutaban en ninguna parte — incluidas las tres cosas que
/// `PROTOCOL.md` promete: el evento `delivered`, y descartar lo viejo en vez de
/// frenar a quien escribe.
/// </summary>
public sealed class EventStreamTests
{
    private static Message AnyMessage(string id = "not_1") => new()
    {
        Id = id,
        ThreadId = "thr_1",
        From = "claude-pc1",
        To = "codex-pc2",
        Kind = MessageKind.Note,
        Body = "rama subida",
        CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public void Un_mensaje_llega_a_todos_los_que_miran()
    {
        EventStream stream = new EventStream();
        using Subscription first = stream.Subscribe();
        using Subscription second = stream.Subscribe();

        stream.PublishMessage(AnyMessage());

        Assert.True(first.Reader.TryRead(out ChannelEvent? toFirst));
        Assert.True(second.Reader.TryRead(out ChannelEvent? toSecond));
        Assert.Equal("message", toFirst!.Event);
        Assert.Equal("not_1", toSecond!.Message!.Id);
    }

    [Fact]
    public void La_entrega_se_publica_con_los_identificadores_que_cambiaron()
    {
        EventStream stream = new EventStream();
        using Subscription subscription = stream.Subscribe();

        stream.PublishDelivered(["not_1", "not_2"]);

        Assert.True(subscription.Reader.TryRead(out ChannelEvent? published));
        Assert.Equal("delivered", published!.Event);
        Assert.Equal(["not_1", "not_2"], published.Ids);
    }

    [Fact]
    public void Una_entrega_vacia_no_se_publica_porque_no_es_noticia()
    {
        EventStream stream = new EventStream();
        using Subscription subscription = stream.Subscribe();

        stream.PublishDelivered([]);

        Assert.False(subscription.Reader.TryRead(out _));
    }

    [Fact]
    public void Un_observador_lento_pierde_lo_viejo_y_no_frena_a_quien_escribe()
    {
        EventStream stream = new EventStream();
        using Subscription subscription = stream.Subscribe();

        // La cola es de 256. Nadie lee mientras se publica: si bloqueara en vez de
        // descartar, esto no terminaría — que es exactamente el fallo que la promesa
        // `DropOldest` existe para evitar.
        for (int i = 0; i < 300; i++)
        {
            stream.PublishMessage(AnyMessage($"not_{i}"));
        }

        List<string> received = [];
        while (subscription.Reader.TryRead(out ChannelEvent? published))
        {
            received.Add(published.Message!.Id);
        }

        Assert.Equal(256, received.Count);
        Assert.Equal("not_299", received[^1]);
        Assert.DoesNotContain("not_0", received);
    }

    [Fact]
    public void Al_soltar_la_suscripcion_deja_de_contar_como_observador()
    {
        EventStream stream = new EventStream();

        Subscription subscription = stream.Subscribe();
        Assert.Equal(1, stream.SubscriberCount);

        subscription.Dispose();
        Assert.Equal(0, stream.SubscriberCount);
    }

    [Fact]
    public void Publicar_sin_nadie_mirando_no_es_un_error()
    {
        EventStream stream = new EventStream();

        stream.PublishMessage(AnyMessage());

        Assert.Equal(0, stream.SubscriberCount);
    }
}
