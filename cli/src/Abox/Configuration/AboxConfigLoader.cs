using Abox.Infrastructure;
using YamlDotNet.Serialization;

namespace Abox.Configuration;

/// <summary>
/// Reads the user configuration file.
/// </summary>
public static class AboxConfigLoader
{
    public static AboxConfig Load(AboxPaths paths)
    {
        if (!File.Exists(paths.ConfigFile))
        {
            return new AboxConfig();
        }

        var yaml = File.ReadAllText(paths.ConfigFile);
        var file = Yaml.Deserialize<UserConfigFile>(yaml, paths.ConfigFile);

        return new AboxConfig
        {
            DefaultSquad = string.IsNullOrWhiteSpace(file.DefaultSquad)
                ? new AboxConfig().DefaultSquad
                : file.DefaultSquad,
            DefaultLocation = file.DefaultLocation,
            KitsDirectory = string.IsNullOrWhiteSpace(file.KitsDirectory)
                ? new AboxConfig().KitsDirectory
                : file.KitsDirectory,
            DefaultTag = string.IsNullOrWhiteSpace(file.DefaultTag)
                ? new AboxConfig().DefaultTag
                : file.DefaultTag,
        };
    }

    private sealed class UserConfigFile
    {
        public string? DefaultSquad { get; set; }

        public string? DefaultLocation { get; set; }

        public string? KitsDirectory { get; set; }

        public string? DefaultTag { get; set; }
    }
}
