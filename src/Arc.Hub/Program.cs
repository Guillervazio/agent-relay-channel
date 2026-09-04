using Arc.Hub;

// Lo único que no tiene costura y no la necesita: leer el entorno, rechazarlo si no
// sirve, y correr. Todo lo demás vive en HubApp, donde un test puede montarlo.

string databasePath = Environment.GetEnvironmentVariable("ARC_DB")
                   ?? Path.Combine(AppContext.BaseDirectory, "arc.db");
string? token = Environment.GetEnvironmentVariable("ARC_TOKEN");
bool allowAnonymous = Environment.GetEnvironmentVariable("ARC_ALLOW_ANONYMOUS") == "1";

if (string.IsNullOrWhiteSpace(token) && !allowAnonymous)
{
    Console.Error.WriteLine("""
        ARC_TOKEN no está definido.

        El hub acepta instrucciones entre agentes: no debe quedar abierto sin autenticar.
        Define un secreto compartido antes de arrancar:

            PowerShell    $env:ARC_TOKEN = "<secreto>"
            bash          export ARC_TOKEN='<secreto>'
            contenedor    docker run -e ARC_TOKEN='<secreto>' ...

        Para pruebas en local, ARC_ALLOW_ANONYMOUS=1 desactiva la comprobación
        y fuerza la escucha en loopback.
        """);
    return 1;
}

// El tope de una espera acota también el KeepAliveTimeout derivado de él, así que un
// valor absurdo no es una preferencia rara: desborda la suma o deja el canal inservible.
// Un día es más de lo que cualquier turno de agente puede aprovechar.
const int maxWaitCeiling = 86_400;
string? maxWaitRaw = Environment.GetEnvironmentVariable("ARC_MAX_WAIT");
int maxWaitSeconds = 300;

if (!string.IsNullOrWhiteSpace(maxWaitRaw)
    && (!int.TryParse(maxWaitRaw, out maxWaitSeconds) || maxWaitSeconds <= 0 || maxWaitSeconds > maxWaitCeiling))
{
    // Un valor no positivo hacía que ValidateWait rechazara toda espera, incluida la de
    // cero segundos: el hub arrancaba, se reportaba sano y contestaba 422 a todo el canal.
    // Uno ilegible caía a 300 sin decir palabra, que es la misma mentira más callada.
    Console.Error.WriteLine($"""
        ARC_MAX_WAIT no es un número de segundos utilizable: "{maxWaitRaw}".

        Es el tope de cada espera y ha de estar entre 1 y {maxWaitCeiling}. Un valor
        negativo o cero deja el hub en pie contestando 422 a toda petición, y uno
        ilegible haría que el tope real no fuera el que creías haber puesto.

        Quita la variable para usar los 300 segundos por defecto.
        """);
    return 1;
}

// Sin token sólo se escucha en loopback: un canal anónimo no sale de la máquina.
string defaultUrls = allowAnonymous ? "http://127.0.0.1:8765" : "http://0.0.0.0:8765";

WebApplication app = await HubApp.BuildAsync(new HubOptions
{
    DatabasePath = databasePath,
    Token = string.IsNullOrWhiteSpace(token) ? null : token,
    MaxWaitSeconds = maxWaitSeconds,
    Urls = Environment.GetEnvironmentVariable("ARC_URLS") ?? defaultUrls
});

// La dirección del panel no se adivina; se dice al arrancar.
app.Lifetime.ApplicationStarted.Register(() =>
    Console.WriteLine($"Panel en vivo: {app.Urls.FirstOrDefault()?.Replace("0.0.0.0", "localhost")}/ui"));

app.Run();
return 0;
