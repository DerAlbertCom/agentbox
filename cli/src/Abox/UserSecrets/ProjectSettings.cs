using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Abox.Configuration;
using Abox.Naming;

namespace Abox.UserSecrets;

// Static, workspace-local configuration only. Never invoke MSBuild or application startup.
internal sealed class ProjectSettings(string project, UserSecretSnapshot settings, bool hasSource)
{
    internal string Project { get; } = project;
    internal UserSecretSnapshot Settings { get; } = settings;
    internal bool HasSource { get; } = hasSource;

    internal static async Task<IReadOnlyList<ProjectSettings>> ReadAsync(
        string workspace,
        IReadOnlyList<UserSecretSelection> selections,
        CancellationToken cancellationToken,
        Action<string>? diagnose
    )
    {
        var result = new List<ProjectSettings>();
        if (selections.Count == 0)
            return result;
        string root;
        string[] projects;
        try
        {
            root = ResolveDirectory(workspace);
            projects = ProjectFiles(
                    root,
                    root,
                    new HashSet<string>(PathComparer),
                    cancellationToken
                )
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AboxException(
                "Cannot discover projects beneath the selected workspace for the selected stores. Check directory permissions and directory links; no stores were copied."
            );
        }
        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var display = JsonSerializer.Serialize(Path.GetRelativePath(root, project));
            XDocument document;
            try
            {
                EnsureOrdinaryFile(project);
                using var reader = XmlReader.Create(
                    project,
                    new XmlReaderSettings
                    {
                        DtdProcessing = DtdProcessing.Prohibit,
                        XmlResolver = null,
                    }
                );
                document = XDocument.Load(reader);
            }
            catch (Exception exception)
                when (exception is XmlException or IOException or UnauthorizedAccessException)
            {
                throw new AboxException(
                    $"Cannot read project {display} while associating selected stores. Use an ordinary file with readable XML without DTDs, not a file link/reparse point; no stores were copied."
                );
            }
            var declarations = document
                .Descendants()
                .Where(element => element.Name.LocalName == "UserSecretsId")
                .ToArray();
            if (
                declarations.Length == 0
                && !document.Descendants().Any(element => element.Name.LocalName == "Import")
            )
                continue;
            var declaration = declarations.Length == 1 ? declarations[0] : null;
            var id = declaration?.Value.Trim();
            if (
                declaration is null
                || declaration.HasElements
                || !UserSecretId.IsValid(id ?? "")
                || document.Root?.Name.LocalName != "Project"
                || declaration.HasAttributes
                || declaration.Parent?.Name.LocalName != "PropertyGroup"
                || declaration.Parent.Parent != document.Root
                || declaration
                    .AncestorsAndSelf()
                    .Any(element => element.Attribute("Condition") is not null)
            )
            {
                diagnose?.Invoke(
                    $"Warning: project {display}: UserSecretsId is computed, imported, conditional, invalid or unresolved. Declare one unconditional literal ID in the project to enable appsettings overrides; MSBuild is never evaluated. Copy-only selections are unchanged."
                );
                continue;
            }
            var selection = selections.FirstOrDefault(selection =>
                selection.Id.Equals(id, StringComparison.OrdinalIgnoreCase)
            );
            if (selection is null)
                continue;
            var directory = Path.GetDirectoryName(project)!;
            var merged = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            var hasSource = false;
            foreach (var name in new[] { "appsettings.json", "appsettings.development.json" })
            {
                UserSecretSnapshot snapshot;
                try
                {
                    // Use the actual spelling even on case-insensitive hosts: Linux development uses lowercase.
                    var path = Directory
                        .EnumerateFiles(directory)
                        .FirstOrDefault(path => Path.GetFileName(path) == name);
                    if (path is null)
                        continue;
                    EnsureOrdinaryFile(path);
                    var json = await File.ReadAllTextAsync(
                        path,
                        new UTF8Encoding(false, true),
                        cancellationToken
                    );
                    snapshot = UserSecretSnapshot.Parse(selection, json);
                }
                catch (Exception exception)
                    when (exception
                            is AboxException
                                or IOException
                                or UnauthorizedAccessException
                                or DecoderFallbackException
                    )
                {
                    throw new AboxException(
                        $"Project {display}, store '{selection.Id}': cannot read {name} as a UTF-8 JSON object with unique effective .NET configuration keys (case-insensitive). Check file permissions and use an ordinary file, not a file link/reparse point; no stores were copied."
                    );
                }
                hasSource = true;
                foreach (var (key, value) in snapshot.Leaves)
                {
                    merged.Remove(key);
                    merged.Add(key, value);
                }
            }
            result.Add(
                new ProjectSettings(
                    Path.GetRelativePath(root, project),
                    UserSecretSnapshot.Parse(selection, JsonSerializer.Serialize(merged)),
                    hasSource
                )
            );
        }
        return result;
    }

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static void EnsureOrdinaryFile(string path)
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
        string directory,
        HashSet<string> visited,
        CancellationToken cancellationToken
    )
    {
        directory = ResolveDirectory(directory);
        var relative = Path.GetRelativePath(root, directory);
        if (
            Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar)
            || !visited.Add(directory)
        )
            yield break;
        if (relative.Split(Path.DirectorySeparatorChar).Any(IsExcludedDirectory))
            yield break;
        foreach (
            var path in Directory
                .EnumerateFiles(directory, "*.csproj")
                .Order(StringComparer.Ordinal)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                !Path.GetFileName(path)
                    .StartsWith("nCrunchTemp_", StringComparison.OrdinalIgnoreCase)
            )
                yield return path;
        }
        foreach (
            var child in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsExcludedDirectory(Path.GetFileName(child)))
                continue;
            foreach (var path in ProjectFiles(root, child, visited, cancellationToken))
                yield return path;
        }
    }

    private static bool IsExcludedDirectory(string name) =>
        new[] { ".git", "bin", "obj" }.Contains(name, StringComparer.OrdinalIgnoreCase);
}
