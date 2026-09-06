using Arc.Core;
using Microsoft.Extensions.Time.Testing;

namespace Arc.Tests;

/// <summary>
/// El reloj. Antes de que entrara por la puerta, ningún test podía afirmar
/// qué instante quedaba escrito: sólo que estaba cerca del de ahora, que es
/// una tolerancia disfrazada de aserción.
/// </summary>
public sealed class ClockTests : IAsyncLifetime
{
    private const string A = "claude-pc1";
    private const string B = "codex-pc2";

    /// <summary>Un instante que nadie confundiría con el de la máquina que corre el test.</summary>
    private static readonly DateTimeOffset Instant = new(2019, 7, 20, 20, 17, 40, TimeSpan.Zero);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"arc-clock-{Guid.NewGuid():n}.db");
    private readonly FakeTimeProvider _time = new(Instant);
    private MessageStore _store = null!;
    private EventStream _events = null!;
    private ChannelService _channel = null!;

    public async Task InitializeAsync()
    {
        _store = new MessageStore(_path, _time);
        await _store.InitializeAsync();
        _events = new EventStream(_time);
        _channel = new ChannelService(_store, new WaiterRegistry(), maxWaitSeconds: 30, _events, _time);
    }

    public Task DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (string file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Una_peticion_se_fecha_con_el_reloj_inyectado()
    {
        AskResult result = await _channel.AskAsync(A, B, "¿céntimos o euros?", null, null, null, 0);

        Message? stored = await _store.GetAsync(result.RequestId!);
        Assert.Equal(Instant, stored!.CreatedAt);
    }

    [Fact]
    public async Task Una_nota_se_fecha_con_el_reloj_inyectado()
    {
        Message note = await _channel.NoteAsync(A, B, "rama subida", null, null, null);

        Assert.Equal(Instant, note.CreatedAt);
    }

    [Fact]
    public async Task Una_respuesta_lleva_la_hora_a_la_que_se_contesto_y_no_la_de_la_pregunta()
    {
        AskResult asked = await _channel.AskAsync(A, B, "¿céntimos o euros?", null, null, null, 0);
        _time.Advance(TimeSpan.FromMinutes(4));

        Message response = await _channel.RespondAsync(B, asked.RequestId!, "céntimos", null);

        Assert.Equal(Instant.AddMinutes(4), response.CreatedAt);
    }

    /// <summary>
    /// El borde de la ventana, medido sin tolerancias: la resta la hace el hub contra el reloj
    /// que se le inyectó, de modo que el llamante dice hace cuánto y nunca desde cuándo — y un
    /// desfase entre su reloj y el del hub no puede estrechar ni ensanchar lo que recibe.
    /// </summary>
    [Fact]
    public async Task La_ventana_de_relectura_se_mide_contra_el_reloj_del_hub()
    {
        await _channel.NoteAsync(A, B, "rama subida", null, null, null);
        await _channel.InboxAsync(B, B, false, 0);

        _time.Advance(TimeSpan.FromMinutes(10));

        Assert.Empty(await _channel.InboxAsync(B, B, false, 0, replay: 60));
        Assert.Single(await _channel.InboxAsync(B, B, false, 0, replay: 3600));
    }

    [Fact]
    public async Task El_last_seen_de_un_agente_avanza_con_el_reloj()
    {
        await _channel.NoteAsync(A, B, "primera", null, null, null);
        _time.Advance(TimeSpan.FromHours(2));
        await _channel.NoteAsync(A, B, "segunda", null, null, null);

        AgentInfo agent = Assert.Single(await _store.ListAgentsAsync(), a => a.Id == A);
        Assert.Equal(Instant.AddHours(2), agent.LastSeen);
    }

    [Fact]
    public async Task Un_evento_para_el_observador_lleva_la_hora_del_reloj_inyectado()
    {
        using Subscription subscription = _events.Subscribe();
        _time.Advance(TimeSpan.FromSeconds(30));

        await _channel.NoteAsync(A, B, "rama subida", null, null, null);

        ChannelEvent published = await subscription.Reader.ReadAsync();
        Assert.Equal("message", published.Event);
        Assert.Equal(Instant.AddSeconds(30), published.At);
    }
}
