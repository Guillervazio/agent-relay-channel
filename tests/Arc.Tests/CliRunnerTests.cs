using System.Net;
using System.Text;
using System.Text.Json;
using Arc.Cli;

namespace Arc.Tests;

/// <summary>
/// Los cinco códigos de salida del cliente, que son contrato publicado
/// ([P009](../../docs/adr/P009-the-cli-exit-codes-are-contract.md)) y hasta ahora
/// no tenían un solo test: un agente ramifica sobre ellos, no sobre el texto.
/// </summary>
/// <remarks>
/// Sin red y sin hub. El transporte es un doble que contesta lo que el caso
/// necesita, que es lo único que hacía falta para poder afirmar nada de esto.
/// </remarks>
public sealed class CliRunnerTests
{
    /// <summary>Un transporte que contesta siempre lo mismo, y recuerda lo que le pidieron.</summary>
    private sealed class Answering(HttpStatusCode status, string body = "{}") : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    /// <summary>Un transporte que no llega a ninguna parte, como un hub apagado.</summary>
    private sealed class Unreachable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("conexión rechazada");
    }

    private static async Task<(int Code, string Out, string Error)> RunAsync(
        string[] args, HttpMessageHandler? handler = null, string input = "")
    {
        StringWriter output = new StringWriter();
        StringWriter error = new StringWriter();

        CliRunner runner = new CliRunner(output, error, new StringReader(input));
        // El comando va primero: las banderas se analizan a partir del segundo argumento.
        int code = await runner.RunAsync([.. args, "--agent", "claude-pc1"], handler ?? new Answering(HttpStatusCode.OK));

        return (code, output.ToString(), error.ToString());
    }

    // ---------- 2: el comando está mal usado ----------

    [Fact]
    public async Task Un_comando_que_no_existe_es_2_y_enseña_la_ayuda()
    {
        (int code, _, string error) = await RunAsync(["inventado"]);

        Assert.Equal(ExitCodes.Usage, code);
        Assert.Contains("Comando desconocido", error, StringComparison.Ordinal);
        Assert.Contains("arc ask", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preguntar_sin_destinatario_es_2()
    {
        (int code, _, string error) = await RunAsync(["ask", "--body", "x"]);

        Assert.Equal(ExitCodes.Usage, code);
        Assert.Contains("--to", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preguntar_sin_cuerpo_es_2()
    {
        (int code, _, string error) = await RunAsync(["ask", "--to", "codex-pc2"]);

        Assert.Equal(ExitCodes.Usage, code);
        Assert.Contains("cuerpo", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_body_file_que_no_existe_es_2()
    {
        (int code, _, string error) = await RunAsync(
            ["ask", "--to", "codex-pc2", "--body-file", Path.Combine(Path.GetTempPath(), "no-existe-jamas.md")]);

        Assert.Equal(ExitCodes.Usage, code);
        Assert.Contains("No existe el fichero", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unas_refs_mal_formadas_son_2_y_el_mensaje_no_sale()
    {
        Answering handler = new Answering(HttpStatusCode.OK);

        (int code, _, string error) = await RunAsync(
            ["ask", "--to", "codex-pc2", "--body", "x", "--refs", "{roto"], handler);

        Assert.Equal(ExitCodes.Usage, code);
        Assert.Contains("--refs", error, StringComparison.Ordinal);
        Assert.Null(handler.LastRequest);
    }

    // ---------- 1: la red o el hub ----------

    [Fact]
    public async Task Un_hub_inalcanzable_es_1()
    {
        (int code, _, string error) = await RunAsync(["agents"], new Unreachable());

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("No se pudo contactar", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Una_espera_por_encima_del_tope_es_1_y_no_3()
    {
        // El hub contesta 422 invalid_wait: es un error del llamante, no un plazo agotado.
        // Es la distinción más fácil de romper sin darse cuenta.
        Answering handler = new Answering(HttpStatusCode.UnprocessableEntity,
            @"{""error"":""invalid_wait"",""detail"":""'wait' va entre 0 y 300 segundos.""}");

        (int code, _, _) = await RunAsync(["ask", "--to", "codex-pc2", "--body", "x", "--wait", "600"], handler);

        Assert.Equal(ExitCodes.Error, code);
    }

    [Fact]
    public async Task Un_token_equivocado_es_1()
    {
        Answering handler = new Answering(HttpStatusCode.Unauthorized,
            @"{""error"":""unauthorized"",""detail"":""Cabecera X-ARC-Token ausente o incorrecta.""}");

        (int code, _, _) = await RunAsync(["agents"], handler);

        Assert.Equal(ExitCodes.Error, code);
    }

    // ---------- 3 y 4: los dos finales que no son fallos ----------

    [Fact]
    public async Task Una_espera_agotada_es_3()
    {
        Answering handler = new Answering(HttpStatusCode.Accepted,
            @"{""outcome"":""timeout"",""request_id"":""req_1a2b3c"",""thread_id"":""thr_1a2b3c""}");

        (int code, string output, _) = await RunAsync(
            ["ask", "--to", "codex-pc2", "--body", "x", "--wait", "1"], handler);

        Assert.Equal(ExitCodes.Timeout, code);
        Assert.Contains("req_1a2b3c", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_buzon_vacio_es_4_y_no_0()
    {
        // La distinción sobre la que ramifica un agente: "no hay nada" no es "hay algo".
        Answering handler = new Answering(HttpStatusCode.NoContent, string.Empty);

        (int code, _, _) = await RunAsync(["inbox"], handler);

        Assert.Equal(ExitCodes.Empty, code);
    }

    // ---------- 0, y lo que viaja en la petición ----------

    [Fact]
    public async Task El_diagnostico_correcto_es_0()
    {
        Answering handler = new Answering(HttpStatusCode.OK,
            @"{""status"":""ok"",""uptime_seconds"":12,""authenticated"":true,""max_wait_seconds"":300,""database"":""arc.db"",""waiters"":{},""agents"":[]}");

        (int code, string output, _) = await RunAsync(["health"], handler);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("ok", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task La_identidad_viaja_en_la_cabecera_de_cada_peticion()
    {
        Answering handler = new Answering(HttpStatusCode.OK, "[]");

        await RunAsync(["agents"], handler);

        Assert.Equal("claude-pc1", handler.LastRequest!.Headers.GetValues("X-ARC-Agent").Single());
    }

    [Fact]
    public async Task Sin_identidad_no_se_llega_a_tocar_la_red()
    {
        Answering handler = new Answering(HttpStatusCode.OK);

        StringWriter output = new StringWriter();
        StringWriter error = new StringWriter();
        CliRunner runner = new CliRunner(output, error, new StringReader(string.Empty));

        // Sin --agent, y con la variable de entorno fuera de juego mediante una vacía.
        int code = await runner.RunAsync(["agents", "--agent", "   "], handler);

        Assert.Equal(ExitCodes.Usage, code);
        Assert.Contains("ARC_AGENT", error.ToString(), StringComparison.Ordinal);
        Assert.Null(handler.LastRequest);
    }

    // ---------- El cuerpo, que en Windows es donde se pierden los acentos ----------

    [Fact]
    public async Task El_cuerpo_por_stdin_llega_entero_y_con_sus_acentos()
    {
        Answering handler = new Answering(HttpStatusCode.Accepted,
            @"{""outcome"":""queued"",""request_id"":""req_1"",""thread_id"":""thr_1""}");

        await RunAsync(["ask", "--to", "codex-pc2", "--body-file", "-", "--wait", "0"],
            handler, input: "¿Viaja en céntimos o en euros?");

        // El JSON escapa lo que no es ASCII, así que lo que se comprueba es lo que
        // el hub leerá al deserializar, no la forma en que viajó.
        string sent = await handler.LastRequest!.Content!.ReadAsStringAsync();
        using JsonDocument body = JsonDocument.Parse(sent);
        Assert.Equal("¿Viaja en céntimos o en euros?", body.RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public async Task La_ayuda_es_0_cuando_se_pide_y_2_cuando_no_se_pidio_nada()
    {
        StringWriter output = new StringWriter();
        CliRunner runner = new CliRunner(output, new StringWriter(), new StringReader(string.Empty));

        Assert.Equal(ExitCodes.Ok, await runner.RunAsync(["--help"]));
        Assert.Equal(ExitCodes.Usage, await runner.RunAsync([]));
    }
}
