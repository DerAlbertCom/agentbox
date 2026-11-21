using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sbx;

namespace Abox.Sandboxes;

/// <summary>
/// Answers "this folder's Sandbox": current names win over exact legacy names, matched
/// against one sandbox listing. Without one, it gives the create-recovery hint.
/// </summary>
public sealed class FolderSandbox(ISbxClient sbx, IProcessRunner runner, CreateRecoveryHints hints)
{
    /// <summary>The name a new Sandbox for the folder gets, used when none exists yet.</summary>
    public static string FallbackName(string location) => SandboxName.For(location);

    /// <summary>Returns the actual identity, or null when no supported identity exists. Discovery errors propagate.</summary>
    public async Task<SandboxInfo?> FindAsync(string location, CancellationToken cancellationToken)
    {
        var sandboxes = await sbx.ListSandboxesAsync(cancellationToken);
        SandboxInfo? Named(string name) =>
            sandboxes.FirstOrDefault(sandbox =>
                sandbox.Name.Equals(name, StringComparison.Ordinal)
            );

        var current = Named(FallbackName(location));
        if (current is not null)
            return current;
        var withoutSeparator = Path.TrimEndingDirectorySeparator(location);
        if (OperatingSystem.IsWindows() && withoutSeparator != location)
        {
            current = Named(SandboxName.For(withoutSeparator));
            if (current is not null)
                return current;
        }
        var realPath = await LegacyRealPathAsync(location, cancellationToken);
        var legacy = Named(LegacySandboxName.For(location, realPath));
        if (legacy is not null || !OperatingSystem.IsWindows())
            return legacy;
        return await FindWindowsWorkspaceAsync(sandboxes, location, cancellationToken);
    }

    /// <summary>
    /// Returns the folder's Sandbox, or fails with the create-recovery hint. The note, such as
    /// "ports never creates or replaces one", follows the missing-sandbox sentence.
    /// </summary>
    public async Task<SandboxInfo> RequireAsync(
        string location,
        FolderConfig? folderConfig,
        string? note,
        CancellationToken cancellationToken
    )
    {
        var sandbox = await FindAsync(location, cancellationToken);
        if (sandbox is not null)
            return sandbox;

        var hint = hints.Resolve(FallbackName(location), location, folderConfig);
        var lines = new List<string>
        {
            note is null
                ? $"There is no sandbox for '{location}'."
                : $"There is no sandbox for '{location}'; {note}.",
        };
        if (hint.Diagnostic is not null)
            lines.Add(hint.Diagnostic);
        lines.Add("Create it with: " + hint.Command);
        throw new MissingFolderSandboxException(string.Join(Environment.NewLine, lines));
    }

    private async Task<SandboxInfo?> FindWindowsWorkspaceAsync(
        IReadOnlyList<SandboxInfo> sandboxes,
        string location,
        CancellationToken cancellationToken
    )
    {
        var matches = new List<SandboxInfo>();
        foreach (var sandbox in sandboxes)
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

/// <summary>The folder has no Sandbox; the message carries the create-recovery hint.</summary>
public sealed class MissingFolderSandboxException(string message) : AboxException(message);
