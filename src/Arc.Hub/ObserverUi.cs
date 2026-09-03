using System.Reflection;

namespace Arc.Hub;

/// <summary>
/// La página del panel, incrustada en el ensamblado. Va dentro y no junto al binario
/// para que el hub publicado en un solo fichero siga sirviéndola.
/// </summary>
internal static class ObserverUi
{
    private const string ResourceName = "Arc.Hub.ui.index.html";

    public static string Html { get; } = Load();

    private static string Load()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Falta el recurso incrustado {ResourceName}.");
        using StreamReader reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
