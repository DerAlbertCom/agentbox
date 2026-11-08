using Abox.Configuration;
using Abox.Infrastructure;

namespace Abox.Configuration;

/// <summary>
/// Reads the user configuration file.
/// </summary>
public static class AboxConfigLoader
{
    private static readonly string[] KnownKeys =
    [
        "defaultSquad",
        "defaultLocation",
        "kitsDirectory",
        "defaultTag",
    ];

    public static AboxConfig Load(AboxPaths paths)
    {
        if (!File.Exists(paths.ConfigFile))
        {
            return new AboxConfig();
        }

        var reader = YamlReader.LoadFile(paths.ConfigFile);
        reader.RejectUnknownKeys(KnownKeys);

        var defaults = new AboxConfig();

        return new AboxConfig
        {
            DefaultSquad = Or(reader.GetString("defaultSquad"), defaults.DefaultSquad),
            DefaultLocation = reader.GetString("defaultLocation"),
            KitsDirectory = Or(reader.GetString("kitsDirectory"), defaults.KitsDirectory),
            DefaultTag = Or(reader.GetString("defaultTag"), defaults.DefaultTag),
        };
    }

    private static string Or(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
