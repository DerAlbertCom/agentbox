using System.Security.Cryptography;
using System.Text;

namespace Abox.Naming;

/// <summary>
/// The name of a sandbox: the folder it works on, a marker, and a hash of its path.
/// </summary>
public static class SandboxName
{
    private const string Marker = "abox";

    /// <summary>The name of the sandbox for a folder.</summary>
    public static string For(string absolutePath)
    {
        var normalized = NormalizePath(absolutePath);
        var folder = NormalizeFolderName(
            Path.GetFileName(normalized.Replace('/', Path.DirectorySeparatorChar))
        );

        return $"{folder}-{Marker}-{Hash(normalized)}";
    }

    /// <summary>
    /// The name of the folder part: lowercase, only letters, digits and hyphens, and not
    /// starting with a hyphen.
    /// </summary>
    public static string NormalizeFolderName(string folder)
    {
        var builder = new StringBuilder(folder.Length);
        foreach (var character in folder.ToLowerInvariant())
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) ? character : '-');
        }

        var name = builder.ToString().Trim('-');
        return name.Length == 0 ? "folder" : name;
    }

    /// <summary>The first five characters of the hash of a path.</summary>
    public static string Hash(string normalizedPath)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        return Convert.ToHexString(digest)[..5];
    }

    /// <summary>
    /// The path as the hash sees it: forward slashes, and lower case on Windows, where
    /// the file system does not distinguish between cases.
    /// </summary>
    public static string NormalizePath(string path)
    {
        var full = Path.GetFullPath(path);
        var normalized = full.Replace('\\', '/');

        return OperatingSystem.IsWindows() ? normalized.ToLowerInvariant() : normalized;
    }
}
