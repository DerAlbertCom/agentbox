using System.Security.Cryptography;
using System.Text;

namespace Abox.Naming;

/// <summary>The generated name contract of cape-agent-sandbox; never used for new sandboxes.</summary>
internal static class LegacySandboxName
{
    public static string For(string location, string realPath)
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
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(realPath));
        return $"{safe}-ai-sbx-{Convert.ToHexStringLower(digest.AsSpan(0, 3))}";
    }
}
