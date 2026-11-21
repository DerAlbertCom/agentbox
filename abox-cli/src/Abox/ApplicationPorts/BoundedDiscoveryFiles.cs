namespace Abox.ApplicationPorts;

/// <summary>One ordered walk; never follows child directory links.</summary>
internal static class BoundedDiscoveryFiles
{
    private static readonly HashSet<string> ExcludedDirectories = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ".git",
        "bin",
        "obj",
        "node_modules",
        "dist",
        "coverage",
        ".angular",
        ".abox",
        ".vs",
    };

    internal static IEnumerable<string> Enumerate(string location, CancellationToken token) =>
        Walk(location, location, 0, token);

    private static IEnumerable<string> Walk(
        string root,
        string directory,
        int depth,
        CancellationToken token
    )
    {
        token.ThrowIfCancellationRequested();
        if (depth > 64)
            throw new AboxException(
                "Application port discovery depth limit (64 directories) exceeded. Select a smaller --location; no ports were published."
            );
        FileSystemInfo[] entries;
        try
        {
            entries = new DirectoryInfo(directory).GetFileSystemInfos();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw DiscoveryDiagnostics.CannotScan(Path.GetRelativePath(root, directory));
        }
        foreach (var entry in entries.OrderBy(entry => entry.Name, StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            if ((entry.Attributes & FileAttributes.Directory) != 0)
            {
                if (
                    ExcludedDirectories.Contains(entry.Name)
                    || (entry.Attributes & FileAttributes.ReparsePoint) != 0
                )
                    continue;
                foreach (var file in Walk(root, entry.FullName, depth + 1, token))
                    yield return file;
            }
            else if (IsDiscoveryInput(entry.Name, directory))
                yield return entry.FullName;
        }
    }

    private static bool IsDiscoveryInput(string name, string directory) =>
        name is "angular.json" or "package.json"
        || StaticAspNetProject.IsProject(name)
        || (name == "launchSettings.json" && Path.GetFileName(directory) == "Properties");
}
