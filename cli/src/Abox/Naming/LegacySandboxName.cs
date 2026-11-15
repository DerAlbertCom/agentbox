using System.Security.Cryptography;
using System.Text;
using Abox.Infrastructure;

namespace Abox.Naming;

/// <summary>The generated name contract of cape-agent-sandbox; never used for new sandboxes.</summary>
internal static class LegacySandboxName
{
    public static async Task<string> ForAsync(
        string location,
        IProcessRunner runner,
        CancellationToken cancellationToken
    )
    {
        var leaf = location.Replace('\\', '/').TrimEnd('/').Split('/')[^1];
        var prefix = new StringBuilder();
        foreach (var character in leaf)
            prefix.Append(
                char.IsAsciiLetterOrDigit(character) || character is '_' or '-' ? character : '-'
            );
        var safe = prefix.ToString().ToLowerInvariant().Trim('-');
        if (safe.Length == 0)
            safe = "root";
        var realPath = await RealPathAsync(location, runner, cancellationToken);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(realPath));
        return $"{safe}-ai-sbx-{Convert.ToHexStringLower(digest.AsSpan(0, 3))}";
    }

    private static async Task<string> RealPathAsync(
        string location,
        IProcessRunner runner,
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
