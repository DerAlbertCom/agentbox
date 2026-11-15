namespace Abox.Kits;

/// <summary>
/// A directory in the sandbox that is kept when the sandbox is replaced.
/// </summary>
/// <param name="SandboxPath">Where the directory is in the sandbox.</param>
/// <param name="RelativePath">The portable path below the sandbox's host state root.</param>
public sealed record StateEntry(string SandboxPath, string RelativePath)
{
    /// <summary>The home directory of the sandbox user.</summary>
    public const string SandboxHome = "/home/agent";

    /// <summary>
    /// Reads a state entry: <c>~/.config/opencode/state</c> or an absolute path.
    /// </summary>
    public static StateEntry Parse(string? value)
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            throw new AboxException("A state entry cannot be empty.");
        }

        var homeRelative = trimmed == "~" || trimmed.StartsWith("~/", StringComparison.Ordinal);
        if (
            !homeRelative
            && (!trimmed.StartsWith('/') || trimmed.StartsWith("//", StringComparison.Ordinal))
        )
        {
            throw new AboxException(
                $"The state path '{value}' must start with '~/' or a single '/'."
            );
        }

        var sandboxPath = homeRelative ? SandboxHome + trimmed[1..] : trimmed;
        if (
            sandboxPath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(part => !SafeSegment(part))
        )
        {
            throw new AboxException(
                $"The state path '{value}' must use portable directory names, without dot segments, backslashes, drive syntax or reserved device names."
            );
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

    /// <summary>Reads stored paths without repairing or relocating an existing host mapping.</summary>
    public static StateEntry FromStored(string? sandboxPath, string? relativePath)
    {
        var entry = Parse(sandboxPath);
        if (entry.SandboxPath != sandboxPath || entry.RelativePath != relativePath)
        {
            throw new AboxException(
                "The stored state paths are non-canonical or have an inconsistent host mapping. Rebuild or republish the squad."
            );
        }
        return entry;
    }

    private static bool SafeSegment(string part) =>
        part is not ("." or "..")
        && !part.EndsWith('.')
        && !part.EndsWith(' ')
        && !part.Any(character => char.IsControl(character) || "\\:<>\"|?*".Contains(character))
        && !IsDeviceName(part);

    private static bool IsDeviceName(string part)
    {
        var name = part.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || (
                name.Length == 4
                && (
                    name.StartsWith("COM", StringComparison.Ordinal)
                    || name.StartsWith("LPT", StringComparison.Ordinal)
                )
                && "123456789¹²³".Contains(name[3])
            );
    }

    /// <summary>Whether this entry contains the other one.</summary>
    public bool Contains(StateEntry other) =>
        other.SandboxPath.StartsWith(SandboxPath + "/", StringComparison.Ordinal);

    /// <summary>Rejects overlapping sandbox mounts or portable host-directory mappings.</summary>
    public static void EnsureDisjoint(IReadOnlyList<StateEntry> entries)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            foreach (var existing in entries.Take(index))
            {
                if (
                    existing.SandboxPath == entry.SandboxPath
                    || existing.Contains(entry)
                    || entry.Contains(existing)
                    || HostContains(existing.RelativePath, entry.RelativePath)
                    || HostContains(entry.RelativePath, existing.RelativePath)
                )
                {
                    throw new AboxException(
                        $"The state directories '{existing.SandboxPath}' and '{entry.SandboxPath}' overlap in the sandbox or on a supported host."
                    );
                }
            }
        }
    }

    private static bool HostContains(string parent, string child)
    {
        // Case-insensitive hosts and macOS can alias distinct sandbox spellings.
        parent = parent.Normalize();
        child = child.Normalize();
        return child.Equals(parent, StringComparison.OrdinalIgnoreCase)
            || child.StartsWith(parent + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path) =>
        "/" + string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries));
}
