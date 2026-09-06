using System.Text;

namespace Arc.Cli;

// ---------- Argumentos ----------

/// <summary>Parser mínimo: --clave valor, --bandera, y posicionales.</summary>
internal sealed class Flags
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _switches = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Positional { get; } = [];

    public static Flags Parse(IEnumerable<string> args)
    {
        Flags flags = new Flags();
        Queue<string> queue = new Queue<string>(args);
        while (queue.Count > 0)
        {
            string token = queue.Dequeue();
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                flags.Positional.Add(token);
                continue;
            }

            string name = token[2..];
            if (name.Contains('='))
            {
                string[] parts = name.Split('=', 2);
                flags._values[parts[0]] = parts[1];
            }
            // "-" es un valor legítimo (stdin), no el inicio de otra bandera.
            else if (queue.Count > 0 && (queue.Peek() == "-" || !queue.Peek().StartsWith("--", StringComparison.Ordinal)))
            {
                flags._values[name] = queue.Dequeue();
            }
            else
            {
                flags._switches.Add(name);
            }
        }
        return flags;
    }

    public string? Value(string name) => _values.GetValueOrDefault(name);
    public bool Has(string name) => _switches.Contains(name) || _values.ContainsKey(name);
    public int? Number(string name) => int.TryParse(Value(name), out int value) ? value : null;
}

internal static class Help
{
    public const string Text = """
        arc — canal de peticiones entre agentes

        Uso:
          arc ask      --to <agente> --body-file <f> [--wait N] [--subject S] [--refs JSON] [--thread ID]
          arc await    <request_id> [--wait N]
          arc inbox    [--wait N] [--unanswered] [--replay N]
          arc respond  <request_id> --body-file <f> [--refs JSON]
          arc note     --to <agente> --body-file <f>
          arc thread   <thread_id>
          arc agents
          arc health

        El cuerpo se pasa por fichero (--body-file f), por stdin (--body-file -)
        o en línea (--body "texto"). En Windows, --body-file evita que los acentos
        se corrompan al atravesar la codepage de la consola.

        Configuración por entorno:
          ARC_URL      hub al que conectarse        (por defecto http://127.0.0.1:8765)
          ARC_AGENT    identidad de este agente     (obligatoria)
          ARC_TOKEN    secreto compartido del hub
          ARC_PROVIDER etiqueta informativa: claude-code, codex...

        Opciones comunes:
          --json       salida JSON cruda, sin formato de lectura
          --url --agent --token   equivalen a las variables de entorno

        Códigos de salida:
          0 éxito · 1 error · 2 uso incorrecto · 3 espera expirada · 4 sin mensajes

        Ejemplo de ciclo bloqueante:
          arc ask --to codex-pc2 --subject "Contrato" --body-file pregunta.md --wait 180
          arc inbox --wait 300
          arc respond req_1234 --body-file respuesta.md
        """;
}
