namespace Abox.Configuration;

/// <summary>
/// The directories abox keeps its files in, following the XDG base directory rules.
/// </summary>
public sealed class AboxPaths
{
    /// <summary>The name of the configuration file inside the config directory.</summary>
    public const string ConfigFileName = "config.yaml";

    private AboxPaths(string configDirectory, string stateDirectory)
    {
        ConfigDirectory = configDirectory;
        StateDirectory = stateDirectory;
    }

    /// <summary>Where the user configuration lives.</summary>
    public string ConfigDirectory { get; }

    /// <summary>Where abox keeps its own state, including the local builds.</summary>
    public string StateDirectory { get; }

    /// <summary>The configuration file itself.</summary>
    public string ConfigFile => Path.Combine(ConfigDirectory, ConfigFileName);

    /// <summary>The root of all local builds, one directory per squad and tag.</summary>
    public string BuildsDirectory => Path.Combine(StateDirectory, "builds");

    /// <summary>The directory of one local build.</summary>
    public string BuildDirectory(string squad, string tag) =>
        Path.Combine(BuildsDirectory, squad, tag);

    /// <summary>
    /// Resolves the directories for the current user: XDG variables when they are set to an
    /// absolute path, the platform defaults otherwise.
    /// </summary>
    public static AboxPaths Create() =>
        Resolve(
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"),
            Environment.GetEnvironmentVariable("XDG_STATE_HOME"),
            HomeDirectory(),
            OperatingSystem.IsWindows()
        );

    /// <summary>
    /// Resolves the directories from the values of the environment, so the rule can be
    /// read and tested on its own.
    /// </summary>
    /// <param name="xdgConfigHome">The value of XDG_CONFIG_HOME, if it is set.</param>
    /// <param name="xdgStateHome">The value of XDG_STATE_HOME, if it is set.</param>
    /// <param name="home">The home directory of the user.</param>
    /// <param name="windows">Whether the user is on Windows.</param>
    public static AboxPaths Resolve(
        string? xdgConfigHome,
        string? xdgStateHome,
        string home,
        bool windows
    )
    {
        var config =
            Usable(xdgConfigHome) ? Path.Combine(xdgConfigHome!, "abox")
            : windows ? Path.Combine(home, ".abox", "config")
            : Path.Combine(home, ".config", "abox");

        var state =
            Usable(xdgStateHome) ? Path.Combine(xdgStateHome!, "abox")
            : windows ? Path.Combine(home, ".abox", "state")
            : Path.Combine(home, ".local", "state", "abox");

        return new AboxPaths(config, state);
    }

    /// <summary>For tests and for hosts that want their own directories.</summary>
    public static AboxPaths For(string configDirectory, string stateDirectory) =>
        new(configDirectory, stateDirectory);

    private static string HomeDirectory()
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(home))
        {
            return home;
        }

        var profile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrWhiteSpace(profile))
        {
            return profile;
        }

        return AppContext.BaseDirectory;
    }

    private static bool Usable(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value);
}
