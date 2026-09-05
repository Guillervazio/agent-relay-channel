using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Arc.Core;
using Microsoft.AspNetCore.Http.Json;

namespace Arc.Hub;

/// <summary>
/// El hub montado a partir de una configuración que se le entrega, en vez de leerla
/// del entorno donde caiga. Es lo que permite levantarlo dentro de un test sin pisar
/// variables de proceso, y lo que deja a <c>Program</c> con lo único que no se puede
/// probar: leer ese entorno, rechazarlo si no sirve, y correr.
/// </summary>
public static class HubApp
{
    /// <summary>
    /// Monta el hub y crea el esquema. <paramref name="configureWebHost"/> es la
    /// puerta por la que un test sustituye el servidor por uno en memoria.
    /// </summary>
    public static async Task<WebApplication> BuildAsync(HubOptions hub, Action<IWebHostBuilder>? configureWebHost = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();

        builder.WebHost.UseUrls(hub.Urls.Split(';'));
        configureWebHost?.Invoke(builder.WebHost);

        string databasePath = hub.DatabasePath;
        string? token = hub.Token;
        int maxWaitSeconds = hub.MaxWaitSeconds;

        builder.WebHost.ConfigureKestrel(options =>
        {
            // Las esperas largas son el modo normal de operación, no una anomalía.
            options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(maxWaitSeconds + 60);
            options.Limits.MinResponseDataRate = null;
            options.Limits.MaxRequestBodySize = MessageStore.MaxBodyBytes * 2;
        });

        // Sin esto, Minimal APIs contesta un 400 mudo ante un cuerpo mal formado.
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

        builder.Services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = ArcJson.Options.PropertyNamingPolicy;
            options.SerializerOptions.DefaultIgnoreCondition = ArcJson.Options.DefaultIgnoreCondition;
            foreach (JsonConverter converter in ArcJson.Options.Converters)
            {
                options.SerializerOptions.Converters.Add(converter);
            }
        });

        TimeProvider time = hub.Time;
        MessageStore store = new MessageStore(databasePath, time);
        WaiterRegistry registry = new WaiterRegistry();
        EventStream events = new EventStream(time);
        ChannelService channel = new ChannelService(store, registry, maxWaitSeconds, events, time);

        builder.Services.AddSingleton(time);
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton(registry);
        builder.Services.AddSingleton(events);
        builder.Services.AddSingleton(channel);
        builder.Services.AddHttpContextAccessor();

        // Superficie MCP: las mismas operaciones, como herramientas nativas del agente.
        builder.Services
            // Las instrucciones viajan en el handshake: el canal se explica a sí mismo en vez
            // de que cada proyecto pegue las mismas reglas en su CLAUDE.md y las deje divergir.
            .AddMcpServer(options =>
            {
                options.ServerInstructions = ArcInstructions.Text;
                options.Filters.Request.CallToolFilters.Add(next => async (context, ct) =>
                {
                    try
                    {
                        return await next(context, ct);
                    }
                    catch (ChannelException exception)
                    {
                        return ArcTools.Refusal(exception);
                    }
                });
            })
            .WithHttpTransport()
            .WithTools<ArcTools>();

        WebApplication app = builder.Build();
        await store.InitializeAsync();

        DateTimeOffset startedAt = time.GetUtcNow();
        byte[]? tokenBytes = token is null ? null : Encoding.UTF8.GetBytes(token);

        // ---------- Errores ----------

        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (ChannelException exception) when (!context.Response.HasStarted)
            {
                await Results.Json(new ErrorBody(exception.Code, exception.Message),
                    ArcJson.Options, statusCode: exception.Status).ExecuteAsync(context);
            }
            // Un 400 mudo es inútil para un agente. El caso frecuente es un cuerpo que no
            // llega como UTF-8 válido (pasar acentos por argv en Git Bash lo provoca).
            catch (BadHttpRequestException exception) when (!context.Response.HasStarted)
            {
                await Results.Json(new ErrorBody(ArcErrors.InvalidJson, exception.InnerException?.Message ?? exception.Message),
                    ArcJson.Options, statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context);
            }
        });

        // ---------- Autenticación e identidad ----------

        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/healthz") || context.Request.Path.StartsWithSegments("/ui"))
            {
                await next();
                return;
            }

            if (tokenBytes is not null)
            {
                byte[] presented = Encoding.UTF8.GetBytes(context.Request.Headers["X-ARC-Token"].ToString());
                if (!CryptographicOperations.FixedTimeEquals(presented, tokenBytes))
                {
                    await Results.Json(new ErrorBody(ArcErrors.Unauthorized, "Cabecera X-ARC-Token ausente o incorrecta."),
                        ArcJson.Options, statusCode: StatusCodes.Status401Unauthorized).ExecuteAsync(context);
                    return;
                }
            }

            // El panel sólo mira: presenta el token, pero no es un agente del canal y no
            // debe aparecer en /v1/agents ni tocar el estado de nadie.
            if (context.Request.Path.StartsWithSegments("/v1/observe"))
            {
                await next();
                return;
            }

            string agent = context.Request.Headers["X-ARC-Agent"].ToString();
            if (string.IsNullOrWhiteSpace(agent) || !ChannelService.AgentNamePattern.IsMatch(agent))
            {
                await Results.Json(new ErrorBody(ArcErrors.BadAgent,
                        "Cabecera X-ARC-Agent obligatoria. Formato: minúsculas, dígitos, punto, guion o guion bajo (máx. 64)."),
                    ArcJson.Options, statusCode: StatusCodes.Status422UnprocessableEntity).ExecuteAsync(context);
                return;
            }

            context.Items[ArcTools.AgentKey] = agent;
            await store.TouchAgentAsync(
                agent,
                Blank(context.Request.Headers["X-ARC-Provider"].ToString()),
                context.Connection.RemoteIpAddress?.ToString(),
                ct: context.RequestAborted);

            await next();
        });

        // ---------- Endpoints ----------

        app.MapGet("/healthz", async () => Results.Json(new
        {
            status = "ok",
            started_at = startedAt,
            uptime_seconds = (int)(time.GetUtcNow() - startedAt).TotalSeconds,
            authenticated = tokenBytes is not null,
            max_wait_seconds = maxWaitSeconds,
            database = databasePath,
            // Esperas activas: dos agentes esperándose mutuamente se ven aquí.
            waiters = registry.Snapshot(),
            agents = await store.ListAgentsAsync()
        }, ArcJson.Options));

        // Crear una petición. Con ?wait=N bloquea hasta la respuesta.
        app.MapPost("/v1/requests", async (CreateRequestBody input, HttpContext context, int? wait) =>
        {
            AskResult result = await channel.AskAsync(ArcTools.Caller(context), input.To, input.Body, input.Subject,
                input.Refs, input.ThreadId, wait, context.RequestAborted);

            return result.Outcome == "answered"
                ? Results.Ok(result)
                : Results.Accepted($"/v1/messages/{result.RequestId}", result);
        });

        // Reanudar la espera de una petición que ya expiró antes.
        app.MapGet("/v1/requests/{id}/response", async (string id, HttpContext context, int? wait) =>
        {
            AskResult result = await channel.AwaitResponseAsync(ArcTools.Caller(context), id, wait, context.RequestAborted);
            return result.Outcome == "answered"
                ? Results.Ok(result)
                : Results.Accepted($"/v1/messages/{id}", result);
        });

        // Contestar una petición. Despierta al emisor que estuviera esperando.
        app.MapPost("/v1/requests/{id}/response", async (string id, CreateResponseBody input, HttpContext context) =>
            Results.Ok(await channel.RespondAsync(ArcTools.Caller(context), id, input.Body, input.Refs, context.RequestAborted)));

        // Aviso sin respuesta esperada.
        app.MapPost("/v1/notes", async (CreateRequestBody input, HttpContext context) =>
            Results.Ok(await channel.NoteAsync(ArcTools.Caller(context), input.To, input.Body, input.Subject,
                input.Refs, input.ThreadId, context.RequestAborted)));

        // Buzón propio. Con ?wait=N espera a que llegue algo.
        app.MapGet("/v1/inbox/{agent}", async (string agent, HttpContext context, int? wait, bool? unanswered) =>
        {
            IReadOnlyList<Message> messages = await channel.InboxAsync(ArcTools.Caller(context), agent, unanswered ?? false, wait, context.RequestAborted);
            return messages.Count == 0
                ? Results.NoContent()
                : Results.Ok(new InboxResult { Agent = agent, Messages = messages });
        });

        // Leer un mensaje y leer un hilo deciden quién puede verlos, así que no son
        // proyecciones del panel: pasan por el canal como cualquier otra operación.
        app.MapGet("/v1/messages/{id}", async (string id, HttpContext context) =>
            Results.Ok(await channel.MessageAsync(ArcTools.Caller(context), id, context.RequestAborted)));

        app.MapGet("/v1/threads/{id}", async (string id, HttpContext context) =>
            Results.Ok(await channel.ThreadAsync(ArcTools.Caller(context), id, context.RequestAborted)));

        // El token de cancelación se pide por parámetro, no por HttpContext: un manejador
        // cuyo único parámetro es HttpContext encaja con la forma de RequestDelegate y
        // ASP.NET descarta lo que devuelva — la respuesta salía 200 con el cuerpo vacío.
        app.MapGet("/v1/agents", async (CancellationToken ct) =>
            Results.Ok(await store.ListAgentsAsync(ct)));

        // ---------- Panel de observación ----------

        // La página es un fichero estático sin datos dentro: pide el token al abrirse y lo
        // guarda en el navegador. Servirla sin autenticar evita el problema del huevo y la
        // gallina de tener que autenticarse para poder pedir la autenticación.
        app.MapGet("/ui", () => Results.Content(ObserverUi.Html, "text/html; charset=utf-8"));

        // Carga inicial del panel: la cola del historial más el estado del canal.
        app.MapGet("/v1/observe/history", async (int? limit, string? thread, CancellationToken ct) => Results.Json(new
        {
            messages = await store.GetRecentAsync(limit ?? 200, Blank(thread), ct),
            agents = await store.ListAgentsAsync(ct),
            waiters = registry.Snapshot(),
            max_wait_seconds = maxWaitSeconds,
            authenticated = tokenBytes is not null,
            server_time = time.GetUtcNow()
        }, ArcJson.Options));

        // El índice de conversaciones del canal: una fila por hilo, sin cuerpos. Es lo que
        // permite al panel elegir una — terminada o en curso — y mirarla entera con
        // /v1/observe/history?thread=...
        app.MapGet("/v1/observe/threads", async (int? limit, CancellationToken ct) =>
            Results.Json(await store.ListThreadsAsync(limit ?? 200, ct), ArcJson.Options));

        // Lo que pasa, según pasa. Cada mensaje se emite en el acto; el estado (quién está
        // esperando a quién) se recalcula por sondeo y sólo se envía cuando cambia.
        app.MapGet("/v1/observe/stream", async (HttpContext context) =>
        {
            CancellationToken ct = context.RequestAborted;
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers["X-Accel-Buffering"] = "no"; // por si algún día hay un proxy delante

            using Subscription subscription = events.Subscribe();

            async Task SendAsync(string type, object payload)
            {
                string data = System.Text.Json.JsonSerializer.Serialize(payload, ArcJson.Compact);
                await context.Response.WriteAsync($"event: {type}\ndata: {data}\n\n", ct);
                await context.Response.Body.FlushAsync(ct);
            }

            string? lastState = null;
            async Task SendStateIfChangedAsync()
            {
                var payload = new
                {
                    waiters = registry.Snapshot(),
                    agents = await store.ListAgentsAsync(ct),
                    observers = events.SubscriberCount,
                    server_time = time.GetUtcNow()
                };
                string serialized = System.Text.Json.JsonSerializer.Serialize(payload, ArcJson.Compact);
                // server_time cambia siempre; se compara sin él para no emitir estado inmóvil.
                string fingerprint = System.Text.Json.JsonSerializer.Serialize(new { payload.waiters, payload.agents, payload.observers }, ArcJson.Compact);
                if (fingerprint == lastState)
                {
                    return;
                }

                lastState = fingerprint;
                await context.Response.WriteAsync($"event: state\ndata: {serialized}\n\n", ct);
                await context.Response.Body.FlushAsync(ct);
            }

            try
            {
                await SendAsync("hello", new { max_wait_seconds = maxWaitSeconds, database = databasePath, server_time = time.GetUtcNow() });
                await SendStateIfChangedAsync();

                Task<bool> pending = subscription.Reader.WaitToReadAsync(ct).AsTask();
                Task tick = Task.Delay(TimeSpan.FromSeconds(2), ct);

                while (!ct.IsCancellationRequested)
                {
                    Task finished = await Task.WhenAny(pending, tick);

                    if (finished == pending)
                    {
                        if (!await pending)
                        {
                            break;
                        }

                        while (subscription.Reader.TryRead(out ChannelEvent? channelEvent))
                        {
                            await SendAsync(channelEvent.Event, channelEvent);
                        }

                        pending = subscription.Reader.WaitToReadAsync(ct).AsTask();
                    }
                    else
                    {
                        await tick; // propaga la cancelación
                        tick = Task.Delay(TimeSpan.FromSeconds(2), ct);
                        // Latido: mantiene viva la conexión aunque el canal esté en silencio.
                        await context.Response.WriteAsync(": ping\n\n", ct);
                        await context.Response.Body.FlushAsync(ct);
                    }

                    await SendStateIfChangedAsync();
                }
            }
            catch (OperationCanceledException)
            {
                // El navegador cerró la pestaña. No es un fallo.
            }
        });

        app.MapMcp("/mcp");

        return app;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
