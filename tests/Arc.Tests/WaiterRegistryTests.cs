using System.Diagnostics;
using Arc.Core;

namespace Arc.Tests;

/// <summary>
/// El registro de esperas es lo que hace bloqueante al canal. Sus fallos no son
/// visibles en una prueba manual feliz: son carreras y esperas que no despiertan.
/// </summary>
public class WaiterRegistryTests
{
    private static Message AnyMessage(string id = "req_1") => new()
    {
        Id = id,
        ThreadId = "thr_1",
        From = "claude-pc1",
        To = "codex-pc2",
        Kind = MessageKind.Request,
        Body = "¿pregunta con acentos?",
        CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Una_senal_despierta_al_que_espera()
    {
        var registry = new WaiterRegistry();
        using var waiter = registry.Register("inbox:codex-pc2");

        var waiting = waiter.WaitAsync(TimeSpan.FromSeconds(10));
        registry.Signal("inbox:codex-pc2", AnyMessage());

        var received = await waiting;
        Assert.NotNull(received);
        Assert.Equal("req_1", received.Id);
    }

    [Fact]
    public async Task La_espera_expira_devolviendo_null()
    {
        var registry = new WaiterRegistry();
        using var waiter = registry.Register("inbox:nadie");

        var stopwatch = Stopwatch.StartNew();
        var received = await waiter.WaitAsync(TimeSpan.FromMilliseconds(300));
        stopwatch.Stop();

        Assert.Null(received);
        Assert.InRange(stopwatch.ElapsedMilliseconds, 250, 5000);
    }

    [Fact]
    public async Task Una_senal_previa_a_la_espera_no_se_pierde()
    {
        // La carrera real del hub: el destinatario contesta entre el registro
        // del waiter y la llamada a WaitAsync. El mensaje debe seguir ahí.
        var registry = new WaiterRegistry();
        using var waiter = registry.Register("resp:req_1");

        registry.Signal("resp:req_1", AnyMessage("res_1"));
        var received = await waiter.WaitAsync(TimeSpan.FromMilliseconds(100));

        Assert.NotNull(received);
        Assert.Equal("res_1", received.Id);
    }

    [Fact]
    public async Task Varios_agentes_esperando_la_misma_clave_despiertan_todos()
    {
        var registry = new WaiterRegistry();
        using var first = registry.Register("inbox:codex-pc2");
        using var second = registry.Register("inbox:codex-pc2");

        var both = Task.WhenAll(
            first.WaitAsync(TimeSpan.FromSeconds(10)),
            second.WaitAsync(TimeSpan.FromSeconds(10)));

        var woken = registry.Signal("inbox:codex-pc2", AnyMessage());

        Assert.Equal(2, woken);
        Assert.All(await both, Assert.NotNull);
    }

    [Fact]
    public async Task Una_senal_a_otra_clave_no_despierta()
    {
        var registry = new WaiterRegistry();
        using var waiter = registry.Register("inbox:codex-pc2");

        var waiting = waiter.WaitAsync(TimeSpan.FromMilliseconds(300));
        registry.Signal("inbox:otro-agente", AnyMessage());

        Assert.Null(await waiting);
    }

    [Fact]
    public async Task Cancelar_desde_fuera_termina_la_espera()
    {
        // Es el caso de un agente que corta la conexión a mitad del long-poll.
        var registry = new WaiterRegistry();
        using var waiter = registry.Register("inbox:codex-pc2");
        using var cancellation = new CancellationTokenSource();

        var waiting = waiter.WaitAsync(TimeSpan.FromSeconds(30), cancellation.Token);
        await cancellation.CancelAsync();

        Assert.Null(await waiting);
    }

    [Fact]
    public void Al_liberar_el_waiter_la_clave_desaparece_del_diagnostico()
    {
        var registry = new WaiterRegistry();
        var waiter = registry.Register("inbox:codex-pc2");
        Assert.Equal(1, registry.Snapshot()["inbox:codex-pc2"]);

        waiter.Dispose();
        Assert.Empty(registry.Snapshot());
    }

    [Fact]
    public void Las_claves_distinguen_buzon_de_respuesta()
    {
        Assert.Equal("inbox:codex-pc2", WaiterRegistry.InboxKey("codex-pc2"));
        Assert.Equal("resp:req_1", WaiterRegistry.ResponseKey("req_1"));
        Assert.NotEqual(WaiterRegistry.InboxKey("x"), WaiterRegistry.ResponseKey("x"));
    }

    [Fact]
    public async Task Senalar_una_clave_sin_esperas_no_falla()
    {
        var registry = new WaiterRegistry();
        Assert.Equal(0, registry.Signal("inbox:fantasma", AnyMessage()));

        // Y quien llegue después no recibe esa señal perdida.
        using var waiter = registry.Register("inbox:fantasma");
        Assert.Null(await waiter.WaitAsync(TimeSpan.FromMilliseconds(150)));
    }
}
