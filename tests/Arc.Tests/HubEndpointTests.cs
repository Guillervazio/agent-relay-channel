using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Arc.Core;
using Arc.Hub;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace Arc.Tests;

/// <summary>
/// El hub entero en memoria: el mismo pipeline que sirve en producción, montado
/// desde <see cref="HubApp"/> con su configuración escrita a mano en vez de leída
/// del entorno. Cubre lo que hasta ahora sólo tocaban las smokes, que no ejecuta
/// ninguna verificación automática.
/// </summary>
public sealed class HubEndpointTests : IAsyncLifetime
{
    private const string Token = "un-secreto-de-prueba";
    private const string A = "claude-pc1";
    private const string B = "codex-pc2";
    private const string C = "gemini-pc3";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"arc-hub-{Guid.NewGuid():n}.db");
    private WebApplication _app = null!;

    public async Task InitializeAsync()
    {
        _app = await HubApp.BuildAsync(
            new HubOptions { DatabasePath = _path, Token = Token, MaxWaitSeconds = 30 },
            web => web.UseTestServer());

        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (string file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    /// <summary>Un cliente que se presenta como un agente concreto, o como nadie.</summary>
    private HttpClient Client(string? agent, string? token = Token)
    {
        HttpClient client = _app.GetTestClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Add("X-ARC-Token", token);
        }

        if (agent is not null)
        {
            client.DefaultRequestHeaders.Add("X-ARC-Agent", agent);
        }

        return client;
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    // ---------- Autenticación e identidad ----------

    [Fact]
    public async Task Sin_token_el_canal_responde_401()
    {
        HttpResponseMessage response = await Client(A, token: null).GetAsync("/v1/agents");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ArcErrors.Unauthorized, await ErrorCodeOf(response));
    }

    [Fact]
    public async Task Con_un_token_equivocado_el_canal_responde_401()
    {
        HttpResponseMessage response = await Client(A, token: "otro").GetAsync("/v1/agents");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task El_diagnostico_no_pide_token_porque_es_con_lo_que_se_diagnostica()
    {
        HttpResponseMessage response = await Client(agent: null, token: null).GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", body.RootElement.GetProperty("status").GetString());
        Assert.True(body.RootElement.GetProperty("authenticated").GetBoolean());
    }

    [Fact]
    public async Task El_panel_se_sirve_sin_token_porque_no_lleva_datos_dentro()
    {
        HttpResponseMessage response = await Client(agent: null, token: null).GetAsync("/ui");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("MAYUSCULAS")]
    [InlineData("con espacio")]
    [InlineData("-empieza-por-guion")]
    public async Task Un_nombre_de_agente_mal_formado_es_422(string agent)
    {
        HttpResponseMessage response = await Client(agent).GetAsync("/v1/agents");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(ArcErrors.BadAgent, await ErrorCodeOf(response));
    }

    [Fact]
    public async Task El_observador_pasa_con_token_y_sin_identidad_porque_no_es_un_agente()
    {
        HttpResponseMessage response = await Client(agent: null).GetAsync("/v1/observe/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Mirar_el_canal_no_convierte_al_observador_en_agente()
    {
        await Client(agent: null).GetAsync("/v1/observe/history");

        AgentInfo[] agents = (await Client(A).GetFromJsonAsync<AgentInfo[]>("/v1/agents", ArcJson.Options))!;
        Assert.DoesNotContain(agents, agent => agent.Id != A);
    }

    // ---------- El mapeo de estados que el contrato publica ----------

    [Fact]
    public async Task Una_peticion_sin_contestar_es_202_con_su_Location()
    {
        HttpResponseMessage response = await Client(A).PostAsync("/v1/requests?wait=0",
            Json(@"{""to"":""codex-pc2"",""body"":""¿céntimos o euros?""}"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        AskResult result = (await response.Content.ReadFromJsonAsync<AskResult>(ArcJson.Options))!;
        Assert.Equal("queued", result.Outcome);
        Assert.Equal($"/v1/messages/{result.RequestId}", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Una_peticion_contestada_mientras_se_espera_es_200()
    {
        // A pregunta y bloquea; B la encuentra en su buzón y contesta; A despierta.
        Task<HttpResponseMessage> asking = Client(A).PostAsync("/v1/requests?wait=20",
            Json(@"{""to"":""codex-pc2"",""body"":""¿céntimos o euros?""}"));

        string requestId = await FirstRequestInInboxAsync();
        await Client(B).PostAsync($"/v1/requests/{requestId}/response", Json(@"{""body"":""céntimos""}"));

        HttpResponseMessage response = await asking;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AskResult result = (await response.Content.ReadFromJsonAsync<AskResult>(ArcJson.Options))!;
        Assert.Equal("answered", result.Outcome);
        Assert.Equal("céntimos", result.Response!.Body);
    }

    [Fact]
    public async Task Un_buzon_vacio_es_204_y_no_un_200_con_lista_vacia()
    {
        HttpResponseMessage response = await Client(B).GetAsync($"/v1/inbox/{B}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <summary>
    /// El aviso recuperado por el cable, y no sólo por el servicio: lo que se afirma es que
    /// llega el cuerpo, porque el 200 lo daría igual una respuesta con la fila vacía.
    /// </summary>
    [Fact]
    public async Task Un_aviso_ya_entregado_vuelve_por_el_cable_con_su_cuerpo()
    {
        await Client(A).PostAsync("/v1/notes", Json(
            $$"""{"to":"{{B}}","body":"la clave está en el fichero"}"""));

        Assert.Equal(HttpStatusCode.OK, (await Client(B).GetAsync($"/v1/inbox/{B}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Client(B).GetAsync($"/v1/inbox/{B}")).StatusCode);

        HttpResponseMessage releido = await Client(B).GetAsync($"/v1/inbox/{B}?replay=60");
        InboxResult buzon = (await releido.Content.ReadFromJsonAsync<InboxResult>(ArcJson.Options))!;

        Assert.Equal(HttpStatusCode.OK, releido.StatusCode);
        Assert.Equal("la clave está en el fichero", Assert.Single(buzon.Messages).Body);
    }

    [Fact]
    public async Task Un_replay_por_encima_del_tope_es_422_y_no_una_ventana_recortada()
    {
        HttpResponseMessage response = await Client(B).GetAsync($"/v1/inbox/{B}?replay=90000");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(ArcErrors.InvalidReplay, await ErrorCodeOf(response));
    }

    [Fact]
    public async Task El_buzon_de_otro_agente_es_403()
    {
        HttpResponseMessage response = await Client(A).GetAsync($"/v1/inbox/{B}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ArcErrors.Forbidden, await ErrorCodeOf(response));
    }

    // ---------- Los rechazos, y el único que sigue siendo 400 ----------

    [Fact]
    public async Task Un_cuerpo_que_no_es_JSON_es_400_y_lo_dice()
    {
        HttpResponseMessage response = await Client(A).PostAsync("/v1/requests", Json("{esto no es json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ArcErrors.InvalidJson, await ErrorCodeOf(response));
    }

    [Fact]
    public async Task Un_wait_por_encima_del_tope_es_422_y_no_una_espera_recortada()
    {
        HttpResponseMessage response = await Client(A).PostAsync("/v1/requests?wait=600",
            Json(@"{""to"":""codex-pc2"",""body"":""x""}"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(ArcErrors.InvalidWait, await ErrorCodeOf(response));

        // Y la petición no llegó a existir: rechazar después de crear sería peor que no rechazar.
        Assert.Equal(HttpStatusCode.NoContent, (await Client(B).GetAsync($"/v1/inbox/{B}")).StatusCode);
    }

    [Fact]
    public async Task Un_mensaje_que_no_existe_es_404()
    {
        HttpResponseMessage response = await Client(A).GetAsync("/v1/messages/req_0000000000000000");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Un_mensaje_de_otros_se_contesta_igual_que_uno_que_no_existe()
    {
        HttpResponseMessage creado = await Client(A).PostAsync("/v1/notes", Json(
            $$"""{"to":"{{B}}","body":"la clave está en el fichero"}"""));
        Message aviso = (await creado.Content.ReadFromJsonAsync<Message>(ArcJson.Options))!;

        HttpResponseMessage ajeno = await Client(C).GetAsync($"/v1/messages/{aviso.Id}");
        HttpResponseMessage inventado = await Client(C).GetAsync("/v1/messages/not_0000000000000000");

        Assert.Equal(HttpStatusCode.NotFound, ajeno.StatusCode);
        Assert.Equal("not_found", await ErrorCodeOf(ajeno));

        // El cuerpo entero, no sólo el código: un detalle distinto diría por la prosa
        // que ese identificador existe, que es lo que el 404 está ocultando.
        Assert.Equal(await inventado.Content.ReadAsStringAsync(), await ajeno.Content.ReadAsStringAsync());

        // Y el destinatario sí lo lee, que es la mitad que el 404 no debe romper.
        Assert.Equal(HttpStatusCode.OK, (await Client(B).GetAsync($"/v1/messages/{aviso.Id}")).StatusCode);
    }

    [Fact]
    public async Task Un_hilo_ajeno_es_404_y_el_propio_no()
    {
        HttpResponseMessage creado = await Client(A).PostAsync("/v1/notes", Json(
            $$"""{"to":"{{B}}","body":"he tocado el endpoint de pagos"}"""));
        Message aviso = (await creado.Content.ReadFromJsonAsync<Message>(ArcJson.Options))!;

        Assert.Equal(HttpStatusCode.NotFound, (await Client(C).GetAsync($"/v1/threads/{aviso.ThreadId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client(B).GetAsync($"/v1/threads/{aviso.ThreadId}")).StatusCode);
    }

    [Fact]
    public async Task El_panel_sigue_viendo_el_canal_entero()
    {
        await Client(A).PostAsync("/v1/notes", Json($$"""{"to":"{{B}}","body":"he tocado el endpoint de pagos"}"""));

        // /v1/observe es un lector deliberado de todo el canal sobre el mismo token: no es
        // un descuido que el 404 de arriba deba cerrar, y una suite que no lo notara si
        // desapareciera sería peor que ninguna.
        string historia = await Client(C).GetStringAsync("/v1/observe/history");

        Assert.Contains("endpoint de pagos", historia, StringComparison.Ordinal);
    }

    // ---------- El handshake de MCP ----------

    [Fact]
    public async Task El_handshake_de_MCP_lleva_las_instrucciones_del_canal()
    {
        HttpClient client = Client(A);
        client.DefaultRequestHeaders.Add("Accept", "application/json, text/event-stream");

        HttpResponseMessage response = await client.PostAsync("/mcp", Json(
            """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{
              "protocolVersion":"2025-06-18",
              "capabilities":{},
              "clientInfo":{"name":"arc-tests","version":"1.0"}}}
            """));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Streamable HTTP puede contestar como JSON o como un evento SSE: lo que se
        // afirma es el contenido, no el envoltorio en que llegó.
        string raw = await response.Content.ReadAsStringAsync();
        string instructions = InstructionsIn(raw);

        Assert.Contains("arc_inbox", instructions, StringComparison.Ordinal);
        Assert.Contains("referencias", instructions, StringComparison.Ordinal);
        Assert.Contains("los dos a la vez", instructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Las_instrucciones_no_nombran_ningun_proyecto_ni_agente_concreto()
    {
        HttpClient client = Client(A);
        client.DefaultRequestHeaders.Add("Accept", "application/json, text/event-stream");

        HttpResponseMessage response = await client.PostAsync("/mcp", Json(
            """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{
              "protocolVersion":"2025-06-18",
              "capabilities":{},
              "clientInfo":{"name":"arc-tests","version":"1.0"}}}
            """));

        // Son las instrucciones de un canal, no las de un montaje: un nombre de agente
        // concreto aquí volvería a atarlas a un proyecto, que es justo lo que evitan.
        string instructions = InstructionsIn(await response.Content.ReadAsStringAsync());

        Assert.DoesNotContain("claude-pc1", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("codex-pc2", instructions, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Lo que decide el pipeline sobre refs y sobre uno mismo ----------

    [Fact]
    public async Task Unas_refs_que_no_son_un_objeto_se_aceptan_y_vuelven_intactas()
    {
        HttpClient client = Client(A);
        HttpResponseMessage sent = await client.PostAsync("/v1/requests?wait=0", Json(
            """{"to":"codex-pc2","body":"revisa estos dos","refs":["src/x.cs","src/y.cs"]}"""));

        // 202 y no 200: encolar sin esperar es lo que 'queued' significa.
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);

        string inbox = await Client(B).GetStringAsync("/v1/inbox/codex-pc2?wait=0");
        Assert.Contains("src/y.cs", inbox, StringComparison.Ordinal);
    }

    // Por REST unas refs rotas rompen el cuerpo entero, así que la respuesta correcta es
    // la del cuerpo ilegible y no invalid_refs, que esta superficie no puede emitir.
    [Fact]
    public async Task Unas_refs_ilegibles_son_un_cuerpo_ilegible()
    {
        HttpResponseMessage response = await Client(A).PostAsync("/v1/requests?wait=0", Json(
            """{"to":"codex-pc2","body":"x","refs":{roto}}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_json", await ErrorCodeOf(response));
    }

    [Fact]
    public async Task Una_peticion_a_uno_mismo_se_encola_y_llega_al_propio_buzon()
    {
        HttpClient client = Client(A);
        HttpResponseMessage sent = await client.PostAsync("/v1/requests?wait=0", Json(
            """{"to":"claude-pc1","body":"revisar el pin"}"""));

        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);

        using JsonDocument result = JsonDocument.Parse(await sent.Content.ReadAsStringAsync());
        Assert.Equal("queued", result.RootElement.GetProperty("outcome").GetString());

        string id = result.RootElement.GetProperty("request_id").GetString()!;
        Assert.Contains(id, await client.GetStringAsync("/v1/inbox/claude-pc1?wait=0"), StringComparison.Ordinal);
    }

    // Las dos puertas por las que se puede pedir una espera. Si sólo se cerrara la
    // primera, encolar con wait 0 y esperar después la esquivaría en dos llamadas.
    [Fact]
    public async Task Ninguna_de_las_dos_puertas_deja_esperar_la_respuesta_de_uno_mismo()
    {
        HttpClient client = Client(A);

        HttpResponseMessage directa = await client.PostAsync("/v1/requests?wait=5", Json(
            """{"to":"claude-pc1","body":"revisar el pin"}"""));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, directa.StatusCode);
        Assert.Equal("self_addressed", await ErrorCodeOf(directa));

        HttpResponseMessage encolada = await client.PostAsync("/v1/requests?wait=0", Json(
            """{"to":"claude-pc1","body":"revisar el pin"}"""));
        using JsonDocument queued = JsonDocument.Parse(await encolada.Content.ReadAsStringAsync());
        string id = queued.RootElement.GetProperty("request_id").GetString()!;

        HttpResponseMessage segunda = await client.GetAsync($"/v1/requests/{id}/response?wait=5");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, segunda.StatusCode);
        Assert.Equal("self_addressed", await ErrorCodeOf(segunda));
    }

    // ---------- Lo que MCP dice cuando el canal se niega ----------

    // El SDK convierte cualquier excepción en "An error occurred invoking 'arc_x'":
    // el modelo se entera de que falló y no de por qué, y en una superficie sin
    // códigos de estado eso es enterarse de nada. Un filtro traduce la negativa.
    [Fact]
    public async Task Una_negativa_del_canal_llega_al_modelo_con_su_codigo()
    {
        string raw = await CallTool(A, """
            {"name":"arc_note","arguments":{"to":"codex-pc2","body":"rama subida","refs":"{roto"}}
            """);

        Assert.Contains("invalid_refs", raw, StringComparison.Ordinal);
        Assert.Contains("debe ser JSON", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("An error occurred", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Y_dice_como_arreglarlo_cuando_hay_forma_de_arreglarlo()
    {
        string raw = await CallTool(A, """
            {"name":"arc_ask","arguments":{"to":"claude-pc1","body":"revisar el pin","wait":5}}
            """);

        // Negar la espera sólo sirve si el que la pidió puede corregirse, y el que la
        // pide aquí es un modelo que no ve códigos de estado.
        Assert.Contains("self_addressed", raw, StringComparison.Ordinal);
        Assert.Contains("propia respuesta", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("An error occurred", raw, StringComparison.Ordinal);
    }

    // ---------- Utilidades ----------

    /// <summary>Llama una herramienta MCP y devuelve la respuesta cruda, venga como JSON o como SSE.</summary>
    private async Task<string> CallTool(string agent, string parameters)
    {
        HttpClient client = Client(agent);
        client.DefaultRequestHeaders.Add("Accept", "application/json, text/event-stream");

        await client.PostAsync("/mcp", Json(
            """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{
              "protocolVersion":"2025-06-18",
              "capabilities":{},
              "clientInfo":{"name":"arc-tests","version":"1.0"}}}
            """));

        HttpResponseMessage response = await client.PostAsync("/mcp", Json(
            $$"""{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{{parameters}}}"""));

        return await response.Content.ReadAsStringAsync();
    }


    /// <summary>Saca las instrucciones del resultado de initialize, venga como JSON o como SSE.</summary>
    private static string InstructionsIn(string raw)
    {
        string payload = raw.Contains("data: ", StringComparison.Ordinal)
            ? raw.Split("data: ")[1].Trim()
            : raw.Trim();

        using JsonDocument document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("result").GetProperty("instructions").GetString()!;
    }

    private static async Task<string> ErrorCodeOf(HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("error").GetString()!;
    }

    /// <summary>Espera a que la petición aparezca en el buzón de B y devuelve su identificador.</summary>
    private async Task<string> FirstRequestInInboxAsync()
    {
        HttpResponseMessage response = await Client(B).GetAsync($"/v1/inbox/{B}?wait=20");
        InboxResult inbox = (await response.Content.ReadFromJsonAsync<InboxResult>(ArcJson.Options))!;
        return inbox.Messages[0].Id;
    }
}
