using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sbx;

namespace Abox.Sandboxes;

/// <summary>Finds a folder's existing sandbox, preferring current names over exact legacy names.</summary>
public sealed class SandboxResolver(ISbxClient sbx, IProcessRunner runner)
{
    /// <summary>Returns the actual identity, or null only when neither candidate exists. Discovery errors propagate.</summary>
    public async Task<SandboxInfo?> FindAsync(string location, CancellationToken cancellationToken)
    {
        var current = await sbx.FindSandboxAsync(SandboxName.For(location), cancellationToken);
        if (current is not null)
            return current;
        var realPath = await LegacyRealPathAsync(location, cancellationToken);
        var legacy = LegacySandboxName.For(location, realPath);
        return await sbx.FindSandboxAsync(legacy, cancellationToken);
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
