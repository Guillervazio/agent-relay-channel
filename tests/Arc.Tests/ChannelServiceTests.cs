using Arc.Core;

namespace Arc.Tests;

/// <summary>
/// Las reglas del canal. Casi todo lo que hay aquí son negativas: un endpoint
/// está cubierto cuando lo están sus caminos de fallo, y el camino feliz es el
/// que se escribió mirándolo.
/// </summary>
/// <remarks>
/// Sin sustitutos. El store es el real sobre un fichero temporal — el motor que
/// ARC embarca — y el registro de esperas es el de producción. No hace falta una
/// interfaz para nada de esto, que es H002 satisfecho por ausencia.
/// </remarks>
public sealed class ChannelServiceTests : IAsyncLifetime
{
    private const string A = "claude-pc1";
    private const string B = "codex-pc2";
    private const string C = "gemini-pc3";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"arc-channel-{Guid.NewGuid():n}.db");
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

    [Fact]
    public async Task Un_agente_puede_dejarse_una_peticion_en_su_propio_buzon()
    {
        AskResult result = await _channel.AskAsync(A, A, "¿céntimos o euros?", null, null, null, 0);

        Assert.Equal("queued", result.Outcome);

        IReadOnlyList<Message> inbox = await _channel.InboxAsync(A, A, false, 0);
        Assert.Contains(inbox, m => m.Id == result.RequestId);
    }

    [Fact]
    public async Task Un_agente_no_puede_esperar_su_propia_respuesta()
    {
        ChannelException error = await Assert.ThrowsAsync<ChannelException>(
            () => _channel.AskAsync(A, A, "¿céntimos o euros?", null, null, null, 5));

        Assert.Equal("self_addressed", error.Code);
        Assert.Equal(422, error.Status);
    }

    // La negativa está en las dos puertas: si sólo estuviera en AskAsync, encolar con 0 y
    // esperar después la esquivaría en dos llamadas.
    [Fact]
    public async Task Tampoco_puede_esperarla_en_una_segunda_llamada()
    {
        AskResult queued = await _channel.AskAsync(A, A, "¿céntimos o euros?", null, null, null, 0);

        ChannelException error = await Assert.ThrowsAsync<ChannelException>(
            () => _channel.AwaitResponseAsync(A, queued.RequestId, 5));

        Assert.Equal("self_addressed", error.Code);
        Assert.Equal(422, error.Status);
    }

    // Y no se niega recoger lo que ya existe: la espera es lo que no podía terminar,
    // no la respuesta que el propio agente se dio en otro turno.
    [Fact]
    public async Task Una_respuesta_que_uno_se_dio_a_si_mismo_se_recoge()
    {
        AskResult queued = await _channel.AskAsync(A, A, "¿céntimos o euros?", null, null, null, 0);
        await _channel.RespondAsync(A, queued.RequestId, "céntimos", null);

        AskResult collected = await _channel.AwaitResponseAsync(A, queued.RequestId, 5);

        Assert.Equal("answered", collected.Outcome);
        Assert.Equal("céntimos", collected.Response!.Body);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("MAYÚSCULAS")]
    [InlineData("con espacio")]
    public async Task Un_destinatario_mal_formado_se_rechaza(string destinatario)
    {
        ChannelException error = await Assert.ThrowsAsync<ChannelException>(
            () => _channel.AskAsync(A, destinatario, "x", null, null, null, 0));

        Assert.Equal("bad_recipient", error.Code);
        Assert.Equal(422, error.Status);
    }

    [Fact]
    public async Task Una_peticion_sin_cuerpo_se_rechaza()
    {
        ChannelException error = await Assert.ThrowsAsync<ChannelException>(
            () => _channel.AskAsync(A, B, "", null, null, null, 0));

        Assert.Equal("empty_body", error.Code);
        Assert.Equal(422, error.Status);
    }

    [Fact]
    public async Task Un_cuerpo_por_encima_del_limite_se_rechaza()
    {
        string enorme = new string('x', MessageStore.MaxBodyBytes + 1);

        ChannelException error = await Assert.ThrowsAsync<ChannelException>(
            () => _channel.AskAsync(A, B, enorme, null, null, null, 0));

        Assert.Equal("body_too_large", error.Code);
        Assert.Equal(422, error.Status);
    }

    [Fact]
    public async Task Una_espera_fuera_de_rango_se_rechaza_en_vez_de_recortarse()
    {
        ChannelException error = await Assert.ThrowsAsync<ChannelException>(
            () => _channel.AskAsync(A, B, "x", null, null, null, wait: 999));

        Assert.Equal("invalid_wait", error.Code);
        Assert.Equal(422, error.Status);
    }

    [Fact]
    public async Task Una_espera_rechazada_no_deja_la_peticion_creada()
    {
        await Assert.ThrowsAsync<ChannelException>(
            () => _channel.AskAsync(A, B, "x", null, null, null, wait: 999));

        // La validación va antes de insertar: contestar 422 y dejar la pregunta
        // en el canal sería lo peor de las dos opciones.
        Assert.Empty(await _store.GetInboxAsync(B));
    }

    [Fact]
    public async Task Preguntar_sin_esperar_deja_la_peticion_en_el_buzon()
    {
        AskResult result = await _channel.AskAsync(A, B, "¿céntimos o euros?", "Pagos", null, null, wait: 0);

        Assert.Equal("queued", result.Outcome);
        Message pendiente = Assert.Single(await _store.GetInboxAsync(B));
        Assert.Equal(result.RequestId, pendiente.Id);
        Assert.Equal("¿céntimos o euros?", pendiente.Body);
    }

    [Fact]
    public async Task Solo_el_destinatario_responde()
    {
        AskResult pregunta = await _channel.AskAsync(A, B, "x", null, null, null, wait: 0);

        ChannelException error = await Assert.ThrowsAsync<ChannelException>(
            () => _channel.RespondAsync(A, pregunta.RequestId!, "no me toca", null));

        Assert.Equal("forbidden", error.Code);
        Assert.Equal(403, error.Status);
    }

    [Fact]
    public async Task Responder_dos_veces_da_conflicto()
    {
        AskResult pregunta = await _channel.AskAsync(A, B, "x", null, null, null, wait: 0);
        await _channel.RespondAsync(B, pregunta.RequestId!, "céntimos", null);

        ChannelException error = await Assert.ThrowsAsync<ChannelException>(
            () => _channel.RespondAsync(B, pregunta.RequestId!, "otra vez", null));

        Assert.Equal("already_answered", error.Code);
        Assert.Equal(409, error.Status);
    }

    [Fact]
    public async Task Responder_a_algo_que_no_existe_es_un_404()
    {
        ChannelException error = await Assert.ThrowsAsync<ChannelException>(
            () => _channel.RespondAsync(B, "req_noexiste", "x", null));

        Assert.Equal("not_found", error.Code);
        Assert.Equal(404, error.Status);
    }

    /// <summary>
    /// El caso que abre el incremento 09: la respuesta HTTP del poll se perdió, así que el aviso
    /// ya salió del buzón por defecto y el cliente nunca llegó a verlo. Aquí el buzón vaciado es
    /// exactamente eso — la primera lectura ocurrió y su resultado no llegó a ninguna parte.
    /// </summary>
    [Fact]
    public async Task Un_aviso_cuya_respuesta_se_perdio_se_recupera_con_su_cuerpo_intacto()
    {
        await _channel.NoteAsync(A, B, "rama feat/pagos subida", "Despliegue", null, null);

        Assert.Single(await _channel.InboxAsync(B, B, false, 0));
        Assert.Empty(await _channel.InboxAsync(B, B, false, 0));
        Assert.Empty(await _channel.InboxAsync(B, B, true, 0));

        Message recuperado = Assert.Single(await _channel.InboxAsync(B, B, false, 0, replay: 60));

        Assert.Equal("rama feat/pagos subida", recuperado.Body);
        Assert.Equal("Despliegue", recuperado.Subject);
        Assert.Equal(MessageKind.Note, recuperado.Kind);
    }

    /// <summary>
    /// Releer no escribe: la segunda relectura devuelve lo mismo que la primera. Una recuperación
    /// que se consumiera a sí misma repetiría el defecto que arregla, esta vez sin red.
    /// </summary>
    [Fact]
    public async Task Releer_el_buzon_no_cambia_nada()
    {
        Message aviso = await _channel.NoteAsync(A, B, "rama subida", null, null, null);
        await _channel.InboxAsync(B, B, false, 0);

        Message primera = Assert.Single(await _channel.InboxAsync(B, B, false, 0, replay: 60));
        Message segunda = Assert.Single(await _channel.InboxAsync(B, B, false, 0, replay: 60));

        Assert.Equal(primera.Id, segunda.Id);
        Assert.Equal(MessageStatus.Delivered, (await _store.GetAsync(aviso.Id))!.Status);
        Assert.Empty(await _channel.InboxAsync(B, B, false, 0));
    }

    /// <summary>
    /// Rechaza en vez de recortar, por lo mismo que <c>wait</c>: una ventana estrechada en
    /// silencio devuelve menos mensajes y el llamante no puede distinguirlo de no haberlos.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(ChannelService.MaxReplaySeconds + 1)]
    public async Task Una_ventana_fuera_de_rango_se_rechaza(int replay)
    {
        ChannelException error = await Assert.ThrowsAsync<ChannelException>(
            () => _channel.InboxAsync(B, B, false, 0, replay));

        Assert.Equal("invalid_replay", error.Code);
        Assert.Equal(422, error.Status);
    }

    /// <summary>Cero es no mirar atrás, igual que <c>wait</c> a 0 es no esperar.</summary>
    [Fact]
    public async Task Una_ventana_de_cero_no_mira_atras()
    {
        await _channel.NoteAsync(A, B, "rama subida", null, null, null);
        await _channel.InboxAsync(B, B, false, 0);

        Assert.Empty(await _channel.InboxAsync(B, B, false, 0, replay: 0));
    }

    [Fact]
    public async Task Un_agente_solo_lee_su_propio_buzon()
    {
        ChannelException error = await Assert.ThrowsAsync<ChannelException>(
            () => _channel.InboxAsync(A, B, false, 0));

        Assert.Equal("forbidden", error.Code);
        Assert.Equal(403, error.Status);
    }

    [Fact]
    public async Task Solo_el_emisor_espera_su_respuesta()
    {
        AskResult pregunta = await _channel.AskAsync(A, B, "x", null, null, null, wait: 0);

        ChannelException error = await Assert.ThrowsAsync<ChannelException>(
            () => _channel.AwaitResponseAsync(B, pregunta.RequestId!, 0));

        Assert.Equal("forbidden", error.Code);
        Assert.Equal(403, error.Status);
    }

    [Fact]
    public async Task Una_respuesta_que_llego_antes_de_esperarla_no_se_pierde()
    {
        AskResult pregunta = await _channel.AskAsync(A, B, "x", null, null, null, wait: 0);
        await _channel.RespondAsync(B, pregunta.RequestId!, "céntimos", null);

        // Sin espera: la respuesta ya está en el almacén y AwaitResponseAsync la
        // encuentra sin bloquear. Es el cinturón del long-poll.
        AskResult recogida = await _channel.AwaitResponseAsync(A, pregunta.RequestId!, wait: 0);

        Assert.Equal("answered", recogida.Outcome);
        Assert.Equal("céntimos", recogida.Response!.Body);
    }

    [Fact]
    public async Task Preguntar_y_responder_mientras_se_espera_despierta_al_emisor()
    {
        Task<AskResult> pregunta = _channel.AskAsync(A, B, "¿céntimos o euros?", null, null, null, wait: 20);

        // El destinatario sondea su buzón hasta ver la pregunta y la contesta.
        Message entrante = await LeerDelBuzonAsync(B);
        await _channel.RespondAsync(B, entrante.Id, "céntimos", null);

        AskResult result = await pregunta;

        Assert.Equal("answered", result.Outcome);
        Assert.Equal("céntimos", result.Response!.Body);
    }

    [Fact]
    public async Task Un_aviso_no_espera_respuesta_y_llega_al_buzon()
    {
        Message aviso = await _channel.NoteAsync(A, B, "he tocado el endpoint de pagos", null, null, null);

        Assert.Equal(MessageKind.Note, aviso.Kind);
        Message recibido = Assert.Single(await _store.GetInboxAsync(B));
        Assert.Equal(aviso.Id, recibido.Id);
    }

    // ---------- Leer un mensaje y leer un hilo ----------

    [Fact]
    public async Task Los_dos_extremos_de_un_mensaje_lo_leen()
    {
        Message aviso = await _channel.NoteAsync(A, B, "he tocado el endpoint de pagos", null, null, null);

        Assert.Equal(aviso.Id, (await _channel.MessageAsync(A, aviso.Id)).Id);
        Assert.Equal(aviso.Id, (await _channel.MessageAsync(B, aviso.Id)).Id);
    }

    [Fact]
    public async Task Un_mensaje_ajeno_no_se_distingue_de_uno_que_no_existe()
    {
        Message aviso = await _channel.NoteAsync(A, B, "la clave está en el fichero", null, null, null);

        ChannelException ajeno = await Assert.ThrowsAsync<ChannelException>(() => _channel.MessageAsync(C, aviso.Id));
        ChannelException inventado = await Assert.ThrowsAsync<ChannelException>(
            () => _channel.MessageAsync(C, "not_0000000000000000"));

        // Ni el código, ni el estado, ni el texto: cualquiera de los tres que difiriera
        // contestaría la pregunta que el 404 existe para no contestar.
        Assert.Equal("not_found", ajeno.Code);
        Assert.Equal(404, ajeno.Status);
        Assert.Equal(inventado.Message, ajeno.Message);
    }

    [Fact]
    public async Task Un_hilo_se_recorta_a_las_filas_del_llamante()
    {
        Message deA = await _channel.NoteAsync(A, B, "he tocado el endpoint de pagos", null, null, null);

        // C se cuela en el hilo mandando un aviso: aparece en él, y eso no puede bastar
        // para leer lo que se dijo antes de que llegara.
        Message deC = await _channel.NoteAsync(C, A, "yo también miro esto", null, null, deA.ThreadId);

        Message soloDeC = Assert.Single(await _channel.ThreadAsync(C, deA.ThreadId));
        Assert.Equal(deC.Id, soloDeC.Id);

        Message soloDeB = Assert.Single(await _channel.ThreadAsync(B, deA.ThreadId));
        Assert.Equal(deA.Id, soloDeB.Id);

        // A está en los dos, y ve los dos.
        Assert.Equal(2, (await _channel.ThreadAsync(A, deA.ThreadId)).Count);
    }

    [Fact]
    public async Task Un_hilo_en_el_que_no_apareces_no_se_distingue_de_uno_que_no_existe()
    {
        Message aviso = await _channel.NoteAsync(A, B, "he tocado el endpoint de pagos", null, null, null);

        ChannelException ajeno = await Assert.ThrowsAsync<ChannelException>(() => _channel.ThreadAsync(C, aviso.ThreadId));
        ChannelException inventado = await Assert.ThrowsAsync<ChannelException>(
            () => _channel.ThreadAsync(C, "thr_0000000000000000"));

        Assert.Equal("not_found", ajeno.Code);
        Assert.Equal(404, ajeno.Status);
        Assert.Equal(inventado.Message, ajeno.Message);
    }

    private async Task<Message> LeerDelBuzonAsync(string agente)
    {
        for (int intento = 0; intento < 100; intento++)
        {
            IReadOnlyList<Message> buzon = await _channel.InboxAsync(agente, agente, false, 0);
            if (buzon.Count > 0)
            {
                return buzon[0];
            }

            await Task.Delay(20);
        }

        throw new InvalidOperationException($"No llegó nada al buzón de {agente}.");
    }
}
