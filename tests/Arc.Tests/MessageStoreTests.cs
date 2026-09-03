using System.Text.Json;
using Arc.Core;

namespace Arc.Tests;

/// <summary>Persistencia: lo que sobrevive a que un agente se caiga a mitad de un hilo.</summary>
public sealed class MessageStoreTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"arc-test-{Guid.NewGuid():n}.db");
    private MessageStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = new MessageStore(_path);
        await _store.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        return Task.CompletedTask;
    }

    private static Message Request(string id = "req_1", string to = "codex-pc2", string body = "¿céntimos o euros?") => new()
    {
        Id = id,
        ThreadId = "thr_1",
        From = "claude-pc1",
        To = to,
        Kind = MessageKind.Request,
        Subject = "Contrato de pagos",
        Body = body,
        Refs = JsonDocument.Parse("""{"branch":"feat/pagos"}""").RootElement.Clone(),
        Status = MessageStatus.Pending,
        CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Un_mensaje_vuelve_tal_y_como_entro()
    {
        await _store.AddAsync(Request());
        var stored = await _store.GetAsync("req_1");

        Assert.NotNull(stored);
        Assert.Equal("¿céntimos o euros?", stored.Body);          // acentos intactos
        Assert.Equal("Contrato de pagos", stored.Subject);
        Assert.Equal(MessageKind.Request, stored.Kind);
        Assert.Equal("feat/pagos", stored.Refs!.Value.GetProperty("branch").GetString());
    }

    [Fact]
    public async Task El_buzon_solo_muestra_lo_dirigido_a_cada_agente()
    {
        await _store.AddAsync(Request("req_1", to: "codex-pc2"));
        await _store.AddAsync(Request("req_2", to: "otro-agente"));

        var inbox = await _store.GetInboxAsync("codex-pc2");
        Assert.Equal("req_1", Assert.Single(inbox).Id);
    }

    [Fact]
    public async Task Lo_entregado_no_vuelve_a_aparecer_en_el_buzon()
    {
        await _store.AddAsync(Request());
        await _store.MarkDeliveredAsync(["req_1"]);

        Assert.Empty(await _store.GetInboxAsync("codex-pc2"));

        // Salvo que se pidan las peticiones aún sin responder: es la vía de
        // recuperación para un agente que se cayó antes de contestar.
        var pending = await _store.GetInboxAsync("codex-pc2", includeUnanswered: true);
        Assert.Equal("req_1", Assert.Single(pending).Id);
    }

    [Fact]
    public async Task Responder_cierra_la_peticion_en_la_misma_operacion()
    {
        await _store.AddAsync(Request());
        await _store.AddResponseAsync(new Message
        {
            Id = "res_1",
            ThreadId = "thr_1",
            From = "codex-pc2",
            To = "claude-pc1",
            Kind = MessageKind.Response,
            Body = "Céntimos.",
            CorrelationId = "req_1",
            Status = MessageStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var request = await _store.GetAsync("req_1");
        Assert.Equal(MessageStatus.Answered, request!.Status);
        Assert.NotNull(request.AnsweredAt);

        var response = await _store.GetResponseForAsync("req_1");
        Assert.Equal("res_1", response!.Id);

        // Y una petición respondida ya no reclama atención.
        Assert.Empty(await _store.GetInboxAsync("codex-pc2", includeUnanswered: true));
    }

    [Fact]
    public async Task Una_respuesta_necesita_a_quien_responde()
    {
        Message huerfana = new Message
        {
            Id = "res_1", ThreadId = "thr_1", From = "codex-pc2", To = "claude-pc1",
            Kind = MessageKind.Response, Body = "x", CreatedAt = DateTimeOffset.UtcNow
        };
        await Assert.ThrowsAsync<ArgumentException>(() => _store.AddResponseAsync(huerfana));
    }

    [Fact]
    public async Task El_hilo_conserva_el_orden_de_la_conversacion()
    {
        await _store.AddAsync(Request());
        await _store.AddResponseAsync(new Message
        {
            Id = "res_1", ThreadId = "thr_1", From = "codex-pc2", To = "claude-pc1",
            Kind = MessageKind.Response, Body = "Céntimos.", CorrelationId = "req_1",
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(1)
        });

        var thread = await _store.GetThreadAsync("thr_1");
        Assert.Equal([MessageKind.Request, MessageKind.Response], thread.Select(m => m.Kind));
    }

    [Fact]
    public async Task Una_conversacion_termina_cuando_no_queda_nada_esperando()
    {
        await _store.AddAsync(Request());
        await _store.AddResponseAsync(new Message
        {
            Id = "res_1", ThreadId = "thr_1", From = "codex-pc2", To = "claude-pc1",
            Kind = MessageKind.Response, Body = "Céntimos.", CorrelationId = "req_1",
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(1)
        });
        // El segundo hilo se queda con la pregunta en el aire.
        await _store.AddAsync(Request("req_2") with { ThreadId = "thr_2" });

        Dictionary<string, ThreadSummary> threads = (await _store.ListThreadsAsync()).ToDictionary(thread => thread.ThreadId);

        Assert.True(threads["thr_1"].Closed);
        Assert.Equal(0, threads["thr_1"].OpenRequests);
        Assert.False(threads["thr_2"].Closed);
        Assert.Equal(1, threads["thr_2"].OpenRequests);
    }

    [Fact]
    public async Task Un_hilo_de_solo_avisos_nace_terminado()
    {
        await _store.AddAsync(Request("nte_1") with { Kind = MessageKind.Note });

        // Nadie va a contestar un aviso: no deja nada abierto que esperar.
        Assert.True(Assert.Single(await _store.ListThreadsAsync()).Closed);
    }

    [Fact]
    public async Task El_indice_resume_cada_conversacion_y_pone_delante_la_ultima()
    {
        var now = DateTimeOffset.UtcNow;
        await _store.AddAsync(Request() with { CreatedAt = now.AddMinutes(-10) });
        await _store.AddAsync(Request("req_2", to: "otro-agente") with
        {
            ThreadId = "thr_2", Subject = "Otra cosa", CreatedAt = now
        });

        var threads = await _store.ListThreadsAsync();

        Assert.Equal(["thr_2", "thr_1"], threads.Select(thread => thread.ThreadId));
        Assert.Equal("Contrato de pagos", threads[1].Subject);
        Assert.Equal(1, threads[1].Messages);
        Assert.Equal(["claude-pc1", "codex-pc2"], threads[1].Participants);
    }

    [Fact]
    public async Task Los_agentes_se_registran_y_acumulan_envios()
    {
        await _store.TouchAgentAsync("claude-pc1", "claude-code", "192.168.1.10", sentMessage: true);
        await _store.TouchAgentAsync("claude-pc1", null, null, sentMessage: true);

        var agent = Assert.Single(await _store.ListAgentsAsync());
        Assert.Equal("claude-code", agent.Provider);   // no se borra al no reenviarlo
        Assert.Equal("192.168.1.10", agent.Host);
        Assert.Equal(2, agent.MessagesSent);
    }

    [Fact]
    public async Task Un_mensaje_sin_refs_se_guarda_igual()
    {
        await _store.AddAsync(Request() with { Id = "req_2", Refs = null });
        Assert.Null((await _store.GetAsync("req_2"))!.Refs);
    }

    [Fact]
    public async Task Un_cuerpo_al_limite_de_tamano_cabe_entero()
    {
        var big = new string('x', MessageStore.MaxBodyBytes);
        await _store.AddAsync(Request("req_big", body: big));
        Assert.Equal(big.Length, (await _store.GetAsync("req_big"))!.Body.Length);
    }
}
