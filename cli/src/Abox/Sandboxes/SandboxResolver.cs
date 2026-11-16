using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sbx;

namespace Abox.Sandboxes;

/// <summary>Finds a folder's existing sandbox, preferring current names over exact legacy names.</summary>
public sealed class SandboxResolver(ISbxClient sbx, IProcessRunner runner)
{
    /// <summary>Returns the actual identity, or null when no supported identity exists. Discovery errors propagate.</summary>
    public async Task<SandboxInfo?> FindAsync(string location, CancellationToken cancellationToken)
    {
        var current = await sbx.FindSandboxAsync(SandboxName.For(location), cancellationToken);
        if (current is not null)
            return current;
        var withoutSeparator = Path.TrimEndingDirectorySeparator(location);
        if (OperatingSystem.IsWindows() && withoutSeparator != location)
        {
            current = await sbx.FindSandboxAsync(
                SandboxName.For(withoutSeparator),
                cancellationToken
            );
            if (current is not null)
                return current;
        }
        var realPath = await LegacyRealPathAsync(location, cancellationToken);
        var legacy = await sbx.FindSandboxAsync(
            LegacySandboxName.For(location, realPath),
            cancellationToken
        );
        if (legacy is not null || !OperatingSystem.IsWindows())
            return legacy;
        return await FindWindowsWorkspaceAsync(location, cancellationToken);
    }

    private async Task<SandboxInfo?> FindWindowsWorkspaceAsync(
        string location,
        CancellationToken cancellationToken
    )
    {
        var matches = new List<SandboxInfo>();
        foreach (var sandbox in await sbx.ListSandboxesAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var workspace = sandbox.Workspaces.FirstOrDefault();
            if (workspace is null || !SameWindowsWorkspace(workspace, location))
                continue;
            // Recompute the historical identity from recorded spelling, never guessed casing.
            var realPath = await LegacyRealPathAsync(workspace, cancellationToken);
            if (sandbox.Name == LegacySandboxName.For(workspace, realPath))
                matches.Add(sandbox);
        }
        if (matches.Count > 1)
            throw new AboxException(
                "Multiple legacy sandboxes match this Windows workspace. Use the original --location spelling of the intended sandbox; no sandbox was selected."
            );
        return matches.SingleOrDefault();
    }

    private static bool SameWindowsWorkspace(string workspace, string location)
    {
        if (!Path.IsPathFullyQualified(workspace))
            return false;
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace))
                .Equals(
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(location)),
                    StringComparison.OrdinalIgnoreCase
                );
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private async Task<string> LegacyRealPathAsync(
        string location,
        CancellationToken cancellationToken
    )
    {
        if (OperatingSystem.IsWindows())
        {
            var full = Path.GetFullPath(location);
            try
            {
                return Directory.ResolveLinkTarget(full, returnFinalTarget: true)?.FullName ?? full;
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                return full;
            }
        }

        var result = await runner.RunAsync(
            new ProcessRequest("bash", ["-c", "cd \"$1\" 2>/dev/null && pwd -P", "bash", location]),
            cancellationToken
        );
        var output = result.StandardOutput.TrimEnd('\r', '\n');
        return result.ExitCode == 0 && output.Length > 0 ? output.Split('\n')[^1] : location;
    }
}
