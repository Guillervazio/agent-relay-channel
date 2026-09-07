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
        foreach (string file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Leer el buzón es reclamarlo: no hay lectura pura del correo desde el incremento 12, porque
    /// era justo el hueco entre leer y marcar lo que dejaba que dos sondeos se llevasen lo mismo.
    /// Así que un arrange que quiere una fila entregada la entrega como la entrega el hub.
    /// </summary>
    private async Task<IReadOnlyList<Message>> Reclamar(
        string agent = "codex-pc2", bool includeUnanswered = false, DateTimeOffset? replaySince = null) =>
        (await _store.ClaimInboxAsync(agent, includeUnanswered, replaySince)).Messages;

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

    private static Message Note(string id = "msg_1", string to = "codex-pc2", string body = "rama subida",
        DateTimeOffset? createdAt = null) => new()
        {
            Id = id,
            ThreadId = "thr_2",
            From = "claude-pc1",
            To = to,
            Kind = MessageKind.Note,
            Subject = "Despliegue",
            Body = body,
            Status = MessageStatus.Pending,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow
        };

    [Fact]
    public async Task Un_mensaje_vuelve_tal_y_como_entro()
    {
        await _store.AddAsync(Request());
        Message? stored = await _store.GetAsync("req_1");

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

        IReadOnlyList<Message> inbox = await Reclamar("codex-pc2");
        Assert.Equal("req_1", Assert.Single(inbox).Id);
    }

    [Fact]
    public async Task Lo_entregado_no_vuelve_a_aparecer_en_el_buzon()
    {
        await _store.AddAsync(Request());
        await Reclamar();

        Assert.Empty(await Reclamar("codex-pc2"));

        // Salvo que se pidan las peticiones aún sin responder: es la vía de
        // recuperación para un agente que se cayó antes de contestar.
        IReadOnlyList<Message> pending = await Reclamar("codex-pc2", includeUnanswered: true);
        Assert.Equal("req_1", Assert.Single(pending).Id);
    }

    /// <summary>
    /// El corazón del incremento 09. Un aviso entregado no lo devuelve ninguna de las dos
    /// consultas anteriores — <c>includeUnanswered</c> lo excluye por construcción, porque su
    /// nombre habla de responder y un aviso no se responde — y la afirmación que importa no es
    /// que vuelva, sino que vuelva entero: una recuperación que devolviera la fila sin su cuerpo
    /// pasaría cualquier test que contase mensajes.
    /// </summary>
    [Fact]
    public async Task Un_aviso_entregado_vuelve_con_su_cuerpo_dentro_de_la_ventana()
    {
        await _store.AddAsync(Note(body: "rama feat/pagos subida, con acentos: ñáéíóú"));
        await Reclamar();

        Assert.Empty(await Reclamar("codex-pc2"));
        Assert.Empty(await Reclamar("codex-pc2", includeUnanswered: true));

        Message recuperado = Assert.Single(
            await Reclamar("codex-pc2", replaySince: DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.Equal("msg_1", recuperado.Id);
        Assert.Equal("rama feat/pagos subida, con acentos: ñáéíóú", recuperado.Body);
        Assert.Equal("Despliegue", recuperado.Subject);
        Assert.Equal(MessageKind.Note, recuperado.Kind);
    }

    [Fact]
    public async Task La_ventana_no_alcanza_lo_anterior_a_ella()
    {
        await _store.AddAsync(Note("msg_viejo", createdAt: DateTimeOffset.UtcNow.AddHours(-2)));
        await _store.AddAsync(Note("msg_nuevo"));
        await Reclamar();

        IReadOnlyList<Message> ventana =
            await Reclamar("codex-pc2", replaySince: DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.Equal("msg_nuevo", Assert.Single(ventana).Id);
    }

    /// <summary>
    /// Un mensaje que cumple dos criterios a la vez sale una sola vez: el OR los suma dentro de
    /// un mismo WHERE, no une tres consultas.
    /// </summary>
    [Fact]
    public async Task Un_mensaje_que_cumple_dos_criterios_no_sale_dos_veces()
    {
        await _store.AddAsync(Request());
        await Reclamar();

        IReadOnlyList<Message> buzon = await Reclamar(
            "codex-pc2", includeUnanswered: true, replaySince: DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.Equal("req_1", Assert.Single(buzon).Id);
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

        Message? request = await _store.GetAsync("req_1");
        Assert.Equal(MessageStatus.Answered, request!.Status);
        Assert.NotNull(request.AnsweredAt);

        Message? response = await _store.GetResponseForAsync("req_1");
        Assert.Equal("res_1", response!.Id);

        // Y una petición respondida ya no reclama atención.
        Assert.Empty(await Reclamar("codex-pc2", includeUnanswered: true));
    }

    [Fact]
    public async Task Dos_respuestas_simultaneas_solo_dejan_una()
    {
        await _store.AddAsync(Request());

        Task<bool> primera = _store.AddResponseAsync(Respuesta("res_a"));
        Task<bool> segunda = _store.AddResponseAsync(Respuesta("res_b"));
        bool[] resultados = await Task.WhenAll(primera, segunda);

        // Exactamente una gana: el estado va en el WHERE, no en una lectura previa.
        Assert.Single(resultados, ganada => ganada);

        IReadOnlyList<Message> hilo = await _store.GetThreadAsync("thr_1");
        Assert.Single(hilo, m => m.Kind == MessageKind.Response);
    }

    /// <summary>
    /// El defecto del incremento 12. Entre leer el buzón y marcarlo cabía otro sondeo del mismo
    /// agente: los dos leían las filas pendientes y las dos respuestas se llevaban los mensajes,
    /// aunque sólo un UPDATE los marcase. Lo que lo detecta es contar entregas en total, no mirar
    /// la fila — la base de datos siempre estuvo coherente, y por eso el defecto duró tanto.
    /// </summary>
    /// <remarks>
    /// Lo que este test **no** prueba: que la carrera ocurriera. Ocho sondeos sobre cinco mensajes
    /// contienden de sobra en la práctica, pero nada garantiza el entrelazado, así que un verde
    /// aquí es la propiedad ejercitada y no demostrada. Lo que sí es determinista es que la
    /// operación sea una sola: fuera del almacén ya no existe el hueco entre leer y marcar, que es
    /// la forma en que este defecto deja de ser alcanzable en lugar de ser vigilado.
    /// </remarks>
    [Fact]
    public async Task Sondeos_simultaneos_entregan_cada_mensaje_una_sola_vez()
    {
        for (int i = 0; i < 5; i++)
        {
            await _store.AddAsync(Note($"msg_{i}"));
        }

        (IReadOnlyList<Message> Messages, IReadOnlyList<string> Claimed)[] sondeos = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => _store.ClaimInboxAsync("codex-pc2")));

        List<string> entregados = sondeos.SelectMany(s => s.Messages.Select(m => m.Id)).ToList();
        Assert.Equal(5, entregados.Count);
        Assert.Equal(5, entregados.Distinct().Count());
        Assert.Equal(entregados.Count, sondeos.Sum(s => s.Claimed.Count));
    }

    /// <summary>
    /// Lo que se lleva un sondeo sale con el estado que la fila ya tiene escrito. Devolvía
    /// <c>pending</c> para una fila que la misma operación acababa de dejar en <c>delivered</c>,
    /// y `PROTOCOL.md` dice desde siempre que leer el buzón es lo que entrega.
    /// </summary>
    [Fact]
    public async Task Lo_que_sale_del_buzon_sale_ya_entregado()
    {
        await _store.AddAsync(Request());

        Message entregado = Assert.Single(await Reclamar());

        Assert.Equal(MessageStatus.Delivered, entregado.Status);
        Assert.Equal(MessageStatus.Delivered, (await _store.GetAsync("req_1"))!.Status);
    }

    /// <summary>
    /// La ventana de P020 sigue sin escribir nada, ahora que leerla ocurre dentro de una
    /// transacción de escritura: lo que vuelve por ella ya estaba entregado y no se reclama otra
    /// vez, que es lo que hace repetible releer.
    /// </summary>
    [Fact]
    public async Task Releer_la_ventana_no_reclama_nada()
    {
        await _store.AddAsync(Note());
        await Reclamar();

        (IReadOnlyList<Message> Messages, IReadOnlyList<string> Claimed) segunda =
            await _store.ClaimInboxAsync("codex-pc2", replaySince: DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.Equal(MessageStatus.Delivered, Assert.Single(segunda.Messages).Status);
        Assert.Empty(segunda.Claimed);
    }

    [Fact]
    public async Task Responder_dos_veces_seguidas_no_sobrescribe_la_primera()
    {
        await _store.AddAsync(Request());

        Assert.True(await _store.AddResponseAsync(Respuesta("res_a")));
        Assert.False(await _store.AddResponseAsync(Respuesta("res_b")));

        Message? response = await _store.GetResponseForAsync("req_1");
        Assert.Equal("res_a", response!.Id);
    }

    private static Message Respuesta(string id) => new Message
    {
        Id = id,
        ThreadId = "thr_1",
        From = "codex-pc2",
        To = "claude-pc1",
        Kind = MessageKind.Response,
        Body = "Céntimos.",
        CorrelationId = "req_1",
        Status = MessageStatus.Pending,
        CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Una_respuesta_necesita_a_quien_responde()
    {
        Message huerfana = new Message
        {
            Id = "res_1",
            ThreadId = "thr_1",
            From = "codex-pc2",
            To = "claude-pc1",
            Kind = MessageKind.Response,
            Body = "x",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await Assert.ThrowsAsync<ArgumentException>(() => _store.AddResponseAsync(huerfana));
    }

    [Fact]
    public async Task El_hilo_conserva_el_orden_de_la_conversacion()
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
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(1)
        });

        IReadOnlyList<Message> thread = await _store.GetThreadAsync("thr_1");
        Assert.Equal([MessageKind.Request, MessageKind.Response], thread.Select(m => m.Kind));
    }

    [Fact]
    public async Task Una_conversacion_termina_cuando_no_queda_nada_esperando()
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
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await _store.AddAsync(Request() with { CreatedAt = now.AddMinutes(-10) });
        await _store.AddAsync(Request("req_2", to: "otro-agente") with
        {
            ThreadId = "thr_2",
            Subject = "Otra cosa",
            CreatedAt = now
        });

        IReadOnlyList<ThreadSummary> threads = await _store.ListThreadsAsync();

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

        AgentInfo agent = Assert.Single(await _store.ListAgentsAsync());
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
        string big = new string('x', MessageStore.MaxBodyBytes);
        await _store.AddAsync(Request("req_big", body: big));
        Assert.Equal(big.Length, (await _store.GetAsync("req_big"))!.Body.Length);
    }
}
