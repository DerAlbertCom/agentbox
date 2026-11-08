namespace Abox.Kits;

/// <summary>
/// A directory in the sandbox that is kept when the sandbox is replaced.
/// </summary>
/// <param name="SandboxPath">Where the directory is in the sandbox.</param>
/// <param name="RelativePath">The path relative to the sandbox home, used for the host directory.</param>
public sealed record StateEntry(string SandboxPath, string RelativePath)
{
    /// <summary>The home directory of the sandbox user.</summary>
    public const string SandboxHome = "/home/agent";

    /// <summary>
    /// Reads a state entry: <c>~/.config/opencode/state</c> or an absolute path.
    /// </summary>
    public static StateEntry Parse(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new AboxException("A state entry cannot be empty.");
        }

        var sandboxPath = trimmed.StartsWith('~') ? SandboxHome + trimmed[1..] : trimmed;

        if (!sandboxPath.StartsWith('/'))
        {
            throw new AboxException($"The state entry '{value}' must start with '~' or '/'.");
        }

        sandboxPath = Normalize(sandboxPath);
        if (sandboxPath == "/")
        {
            throw new AboxException($"The state entry '{value}' cannot be the root directory.");
        }

        var relative = sandboxPath.StartsWith(SandboxHome + "/", StringComparison.Ordinal)
            ? sandboxPath[(SandboxHome.Length + 1)..]
            : sandboxPath.TrimStart('/');

        return new StateEntry(sandboxPath, relative);
    }

    /// <summary>Whether this entry contains the other one.</summary>
    public bool Contains(StateEntry other) =>
        other.SandboxPath.StartsWith(SandboxPath + "/", StringComparison.Ordinal);

    private static string Normalize(string path)
    {
        var normalized = path.Replace("//", "/", StringComparison.Ordinal);
        if (normalized.Length > 1 && normalized.EndsWith('/'))
        {
            normalized = normalized.TrimEnd('/');
        }

        return normalized;
    }
}
