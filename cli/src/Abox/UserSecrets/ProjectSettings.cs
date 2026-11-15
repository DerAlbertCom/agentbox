using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Abox.Configuration;
using Abox.Naming;

namespace Abox.UserSecrets;

// Static, workspace-local configuration only. Never invoke MSBuild or application startup.
internal sealed class ProjectSettings(
    string project,
    UserSecretSnapshot selectedSettings,
    IReadOnlyDictionary<string, string?> allSettings,
    bool hasAppSettingsSource
)
{
    internal string Project { get; } = project;
    internal UserSecretSnapshot SelectedSettings { get; } = selectedSettings;
    internal IReadOnlyDictionary<string, string?> AllSettings { get; } = allSettings;
    internal bool HasAppSettingsSource { get; } = hasAppSettingsSource;

    internal static async Task<IReadOnlyList<ProjectSettings>> ReadAsync(
        string workspace,
        IReadOnlyList<UserSecretSelection> selections,
        CancellationToken cancellationToken
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
            UserSecretSelection? selection;
            try
            {
                selection = ResolveSelectedStore(root, project, selections);
            }
            catch (Exception exception)
                when (exception is XmlException or IOException or UnauthorizedAccessException)
            {
                // Unreadable/linked XML cannot establish an explicit selected-ID association.
                continue;
            }
            if (selection is null)
                continue;
            result.Add(await ReadSettingsAsync(root, project, selection, cancellationToken));
        }
        return result;
    }

    private static UserSecretSelection? ResolveSelectedStore(
        string root,
        string project,
        IReadOnlyList<UserSecretSelection> selections
    )
    {
        EnsureOrdinaryFile(project);
        using var reader = XmlReader.Create(
            project,
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }
        );
        var document = XDocument.Load(reader);
        var declarations = document
            .Descendants()
            .Where(element => element.Name.LocalName == "UserSecretsId")
            .ToArray();
        var selection = selections.FirstOrDefault(selection =>
            declarations.Any(declaration =>
                selection.Id.Equals(declaration.Value.Trim(), StringComparison.OrdinalIgnoreCase)
            )
        );
        if (selection is null)
            return null;
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
            throw new AboxException(
                $"Project {JsonSerializer.Serialize(Path.GetRelativePath(root, project))}, store '{selection.Id}': matching UserSecretsId must be one unconditional literal declaration in a top-level PropertyGroup. Fix conditional, multiple or malformed declarations; MSBuild is never evaluated. No stores were copied."
            );
        }
        return selection;
    }

    private static async Task<ProjectSettings> ReadSettingsAsync(
        string root,
        string project,
        UserSecretSelection selection,
        CancellationToken cancellationToken
    )
    {
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
                snapshot = UserSecretSnapshot.ParseAll(selection.Id, json);
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
                    $"Project {JsonSerializer.Serialize(Path.GetRelativePath(root, project))}, store '{selection.Id}': cannot read {name} as a UTF-8 JSON object with unique effective .NET configuration keys (case-insensitive). Check file permissions and use an ordinary file, not a file link/reparse point; no stores were copied."
                );
            }
            hasSource = true;
            foreach (var (key, value) in snapshot.EffectiveLeaves)
            {
                merged.Remove(key);
                merged.Add(key, value);
            }
        }
        var allSettings = UserSecretSnapshot.ParseAll(
            selection.Id,
            JsonSerializer.Serialize(merged)
        );
        return new ProjectSettings(
            Path.GetRelativePath(root, project),
            allSettings.SelectKeys(selection),
            allSettings.Values,
            hasSource
        );
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
        cancellationToken.ThrowIfCancellationRequested();
        // The root and each parent are already resolved; ordinary children reuse that ancestry.
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
            var target = new DirectoryInfo(child).ResolveLinkTarget(true);
            // Link targets can contain linked ancestors; resolve those before containment checks.
            var resolvedChild = target is null ? child : ResolveDirectory(target.FullName);
            foreach (var path in ProjectFiles(root, resolvedChild, visited, cancellationToken))
                yield return path;
        }
    }

    private static bool IsExcludedDirectory(string name) =>
        new[] { ".git", "bin", "obj", "node_modules" }.Contains(
            name,
            StringComparer.OrdinalIgnoreCase
        );
}
