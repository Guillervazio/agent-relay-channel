using Arc.Core;
using Arc.Hub;
using Microsoft.AspNetCore.Http;

namespace Arc.Tests;

/// <summary>
/// Las siete herramientas MCP. Su salida la lee un modelo, no un programa: lo que
/// se comprueba aquí es que cada una llega al canal, y que la que no sabe quién
/// llama se niega en vez de inventarse una identidad.
/// </summary>
public sealed class ArcToolsTests : IAsyncLifetime
{
    private const string A = "claude-pc1";
    private const string B = "codex-pc2";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"arc-tools-{Guid.NewGuid():n}.db");
    private MessageStore _store = null!;
    private ChannelService _channel = null!;

    public async Task InitializeAsync()
    {
        _store = new MessageStore(_path);
        await _store.InitializeAsync();
        _channel = new ChannelService(_store, new WaiterRegistry(), maxWaitSeconds: 30);
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

    /// <summary>La identidad que el middleware deja puesta, sin pasar por HTTP.</summary>
    private static IHttpContextAccessor As(string? agent)
    {
        DefaultHttpContext context = new DefaultHttpContext();
        if (agent is not null)
        {
            context.Items[ArcTools.AgentKey] = agent;
        }

        return new HttpContextAccessor { HttpContext = context };
    }

    [Fact]
    public async Task Sin_identidad_la_herramienta_se_niega_en_vez_de_suponerla()
    {
        ChannelException error = await Assert.ThrowsAsync<ChannelException>(
            () => ArcTools.InboxAsync(_channel, As(null)));

        Assert.Equal(ArcErrors.BadAgent, error.Code);
        Assert.Equal(422, error.Status);
    }

    [Fact]
    public async Task Una_pregunta_sin_contestar_dice_como_retomarla()
    {
        string answer = await ArcTools.AskAsync(_channel, As(A), B, "¿céntimos o euros?", wait: 0);

        Assert.Contains("arc_await", answer, StringComparison.Ordinal);
        Assert.Contains("req_", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task El_buzon_es_siempre_el_del_que_llama()
    {
        await ArcTools.NoteAsync(_channel, As(A), B, "rama subida");

        Assert.Contains("rama subida", await ArcTools.InboxAsync(_channel, As(B)), StringComparison.Ordinal);
        Assert.DoesNotContain("rama subida", await ArcTools.InboxAsync(_channel, As(A)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task El_ciclo_entero_pasa_por_las_herramientas()
    {
        string asked = await ArcTools.AskAsync(_channel, As(A), B, "¿céntimos o euros?", wait: 0);
        string requestId = asked.Split("req_")[1].Split([' ', '.', ',', '\n'])[0];

        await ArcTools.RespondAsync(_channel, As(B), "req_" + requestId, "céntimos");

        string awaited = await ArcTools.AwaitAsync(_channel, As(A), "req_" + requestId, wait: 0);
        Assert.Contains("céntimos", awaited, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unas_refs_que_no_son_JSON_se_rechazan_en_vez_de_perderse()
    {
        ChannelException error = await Assert.ThrowsAsync<ChannelException>(
            () => ArcTools.NoteAsync(_channel, As(A), B, "rama subida", refs: "{roto"));

        Assert.Equal(ArcErrors.InvalidRefs, error.Code);
    }

    [Fact]
    public async Task El_directorio_de_agentes_nombra_a_quien_ha_escrito()
    {
        await ArcTools.NoteAsync(_channel, As(A), B, "rama subida");

        string agents = await ArcTools.AgentsAsync(_channel);
        Assert.Contains(A, agents, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_hilo_devuelve_la_conversacion_entera()
    {
        Message note = await _channel.NoteAsync(A, B, "rama subida", null, null, null);

        string thread = await ArcTools.ThreadAsync(_channel, note.ThreadId);
        Assert.Contains("rama subida", thread, StringComparison.Ordinal);
    }
}
