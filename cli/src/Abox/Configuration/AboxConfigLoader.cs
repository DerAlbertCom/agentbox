using Abox.Infrastructure;
using Abox.Naming;

namespace Abox.Configuration;

/// <summary>
/// Reads the user configuration file.
/// </summary>
public static class AboxConfigLoader
{
    private static readonly string[] KnownKeys =
    [
        "defaultTeam",
        "defaultLocation",
        "teamsDirectory",
        "kitsDirectory",
        "defaultTag",
        "pullRepository",
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
        var pullRepository = Or(reader.GetString("pullRepository"), defaults.PullRepository);
        if (!RegistryImageName.IsValidRepository(pullRepository))
        {
            throw new AboxException(
                $"'{paths.ConfigFile}': 'pullRepository' is not a valid repository name. Use one lowercase registry path segment, without a host, slash or tag."
            );
        }

        return new AboxConfig
        {
            DefaultTeam = reader.GetString("defaultTeam"),
            DefaultLocation = reader.GetString("defaultLocation"),
            TeamsDirectory = Or(reader.GetString("teamsDirectory"), defaults.TeamsDirectory),
            KitsDirectory = Or(reader.GetString("kitsDirectory"), defaults.KitsDirectory),
            DefaultTag = Or(reader.GetString("defaultTag"), defaults.DefaultTag),
            PullRepository = pullRepository,
        };
    }

    private static string Or(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
