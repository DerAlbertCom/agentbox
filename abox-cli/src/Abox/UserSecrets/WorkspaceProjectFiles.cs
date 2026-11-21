namespace Abox.UserSecrets;

// One bounded traversal; resolve links before containment, exclusion and deduplication.
internal static class WorkspaceProjectFiles
{
    private static readonly string[] ExcludedDirectoryNames =
    [
        ".git",
        "bin",
        "obj",
        "node_modules",
    ];

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    internal static (string Root, string[] Projects) Read(
        string workspace,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var root = ResolveDirectory(workspace);
            var projects = ProjectFiles(
                root,
                root,
                new HashSet<string>(PathComparer),
                cancellationToken
            );
            return (root, projects.ToArray());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AboxException(
                "Cannot discover projects beneath the selected workspace for the selected stores. Check directory permissions and directory links; no stores were copied."
            );
        }
    }

    internal static void EnsureOrdinaryFile(string path)
    {
        if (
            (
                File.GetAttributes(path)
                & (FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.Device)
            ) != 0
        )
            throw new IOException("Configuration inputs must be ordinary files.");
    }

    private static string ResolveDirectory(string path) =>
        ResolveDirectory(path, new HashSet<string>(PathComparer));

    private static string ResolveDirectory(string path, HashSet<string> resolving)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(path));
        if (!resolving.Add(directory.FullName))
            throw new IOException("Directory link cycle.");
        try
        {
            if (directory.Parent is null)
                return directory.FullName;
            var resolved = new DirectoryInfo(
                Path.Combine(ResolveDirectory(directory.Parent.FullName, resolving), directory.Name)
            );
            var target = resolved.ResolveLinkTarget(true);
            // A link's target can itself have linked ancestors, even after following its final link.
            return target is null
                ? resolved.FullName
                : ResolveDirectory(target.FullName, resolving);
        }
        finally
        {
            resolving.Remove(directory.FullName);
        }
    }

    private static IEnumerable<string> ProjectFiles(
        string root,
        string resolvedDirectory,
        HashSet<string> visited,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The root and each parent are already resolved; ordinary children reuse that ancestry.
        if (!IsIncludedDirectory(root, resolvedDirectory) || !visited.Add(resolvedDirectory))
            yield break;
        foreach (var path in NonArtifactProjectFiles(resolvedDirectory, cancellationToken))
        {
            yield return path;
        }
        foreach (
            var child in Directory
                .EnumerateDirectories(resolvedDirectory)
                .Order(StringComparer.Ordinal)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsExcludedDirectory(Path.GetFileName(child)))
                continue;
            var target = new DirectoryInfo(child).ResolveLinkTarget(true);
            // Link targets can contain linked ancestors; resolve those before containment checks.
            var resolvedChild = target is null ? child : ResolveDirectory(target.FullName);
            foreach (var path in ProjectFiles(root, resolvedChild, visited, cancellationToken))
                yield return path;
        }
    }

    private static bool IsIncludedDirectory(string root, string directory)
    {
        var relative = Path.GetRelativePath(root, directory);
        if (
            Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar)
        )
            return false;
        return !relative.Split(Path.DirectorySeparatorChar).Any(IsExcludedDirectory);
    }

    private static IEnumerable<string> NonArtifactProjectFiles(
        string directory,
        CancellationToken cancellationToken
    )
    {
        foreach (
            var path in Directory
                .EnumerateFiles(directory, "*.csproj")
                .Order(StringComparer.Ordinal)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                Path.GetFileName(path)
                    .StartsWith("nCrunchTemp_", StringComparison.OrdinalIgnoreCase)
            )
                continue;
            yield return path;
        }
    }

    private static bool IsExcludedDirectory(string name) =>
        ExcludedDirectoryNames.Contains(name, StringComparer.OrdinalIgnoreCase);
}
