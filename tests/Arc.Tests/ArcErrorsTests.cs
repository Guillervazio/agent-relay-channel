using System.Reflection;
using System.Text;
using Arc.Core;

namespace Arc.Tests;

/// <summary>
/// Los códigos publicados, congelados. Cada aserción escribe el literal a
/// propósito: comparar la constante consigo misma pasa justo cuando alguien la
/// cambia, que es la rotura que estos tests existen para detectar (H012).
/// </summary>
public sealed class ArcErrorsTests
{
    [Fact]
    public void Los_codigos_publicados_no_cambian()
    {
        Assert.Equal("unauthorized", ArcErrors.Unauthorized);
        Assert.Equal("bad_agent", ArcErrors.BadAgent);
        Assert.Equal("bad_recipient", ArcErrors.BadRecipient);
        Assert.Equal("empty_body", ArcErrors.EmptyBody);
        Assert.Equal("body_too_large", ArcErrors.BodyTooLarge);
        Assert.Equal("invalid_json", ArcErrors.InvalidJson);
        Assert.Equal("invalid_refs", ArcErrors.InvalidRefs);
        Assert.Equal("self_addressed", ArcErrors.SelfAddressed);
        Assert.Equal("forbidden", ArcErrors.Forbidden);
        Assert.Equal("not_found", ArcErrors.NotFound);
        Assert.Equal("already_answered", ArcErrors.AlreadyAnswered);
    }

    [Fact]
    public void Todo_codigo_definido_esta_publicado_en_el_protocolo()
    {
        // Aquí el test descubre el valor en vez de congelarlo: su trabajo es
        // detectar que las dos copias divergieron. Una lista escrita a mano
        // pasaría justo cuando alguien añade el código doce y no toca ninguna
        // de las dos — que es como `invalid_refs` estuvo sin publicar.
        string protocol = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "PROTOCOL.md"), Encoding.UTF8);

        string[] sinPublicar = typeof(ArcErrors)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(code => !protocol.Contains($"| `{code}` |", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(sinPublicar);
    }

    [Fact]
    public void Los_codigos_de_salida_del_cli_no_cambian()
    {
        // docs/AGENTES.md le dice a un agente que ramifique sobre estos números.
        Assert.Equal(0, Arc.Cli.ExitCodes.Ok);
        Assert.Equal(1, Arc.Cli.ExitCodes.Error);
        Assert.Equal(2, Arc.Cli.ExitCodes.Usage);
        Assert.Equal(3, Arc.Cli.ExitCodes.Timeout);
        Assert.Equal(4, Arc.Cli.ExitCodes.Empty);
    }
}
