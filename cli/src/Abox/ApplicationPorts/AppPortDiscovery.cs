using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Abox.ApplicationPorts;

internal sealed record AppPortOccurrence(string SourceFile, string Profile, int Port);

internal sealed record AppPortDiscoveryPlan(
    IReadOnlyList<AppPortOccurrence> Occurrences,
    bool HasDotnetProjects
);

internal static class AppPortDiscovery
{
    internal static async Task<AppPortDiscoveryPlan> DiscoverAsync(
        string location,
        CancellationToken token
    )
    {
        var result = new List<AppPortOccurrence>();
        var dotnet = false;
        foreach (var file in ConfigurationFiles(location, location, 0, token))
        {
            var source = Path.GetRelativePath(location, file);
            try
            {
                if (
                    (
                        File.GetAttributes(file)
                        & (FileAttributes.ReparsePoint | FileAttributes.Device)
                    ) != 0
                )
                    throw new IOException();
                await using var stream = File.OpenRead(file);
                var bytes = new byte[1024 * 1024 + 1];
                var count = await stream.ReadAtLeastAsync(
                    bytes,
                    bytes.Length,
                    throwOnEndOfStream: false,
                    token
                );
                if (count == bytes.Length)
                    throw new AboxException(
                        $"Application port discovery file-size limit (1 MiB) exceeded by {JsonSerializer.Serialize(source)}. Reduce the file and retry; no ports were published."
                    );
                var start =
                    count >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
                if (IsDotnetProject(file))
                {
                    using var xmlStream = new MemoryStream(bytes, start, count - start);
                    using var reader = XmlReader.Create(
                        xmlStream,
                        new XmlReaderSettings
                        {
                            DtdProcessing = DtdProcessing.Prohibit,
                            XmlResolver = null,
                        }
                    );
                    var project = XDocument.Load(reader).Root;
                    dotnet |=
                        project?.Name.LocalName == "Project"
                        && (
                            ((string?)project.Attribute("Sdk"))
                                ?.Split(';', StringSplitOptions.TrimEntries)
                                .Any(IsWebSdk) == true
                            || project
                                .Elements()
                                .Any(element =>
                                    element.Name.LocalName == "Sdk"
                                    && IsWebSdk((string?)element.Attribute("Name"))
                                )
                        );
                    continue;
                }
                using var document = JsonDocument.Parse(
                    bytes.AsMemory(start, count - start),
                    new JsonDocumentOptions
                    {
                        CommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true,
                        AllowDuplicateProperties = false,
                    }
                );
                switch (Path.GetFileName(file))
                {
                    case "launchSettings.json":
                        DotnetLaunchSettings.AddPorts(document.RootElement, source, result);
                        break;
                    case "angular.json":
                        AngularConfiguration.AddPorts(document.RootElement, source, result);
                        break;
                    case "package.json":
                        AngularStartScript.AddPorts(document.RootElement, source, result);
                        break;
                }
            }
            catch (Exception exception)
                when (exception
                        is JsonException
                            or XmlException
                            or InvalidOperationException
                            or FormatException
                            or IOException
                            or UnauthorizedAccessException
                )
            {
                var hint = Path.GetFileName(file) switch
                {
                    "angular.json" =>
                        "use object projects and serve options/named configurations with literal integer port values 1..65535; define serve in architect or targets, not both",
                    "package.json" =>
                        "use an object scripts and a literal start script: ng serve (or ng s / ng dev) with one --port N or --port=N, N in 1..65535; remove shell operators, variables and escapes, or configure the port in angular.json and remove dynamic --port flags",
                    _ =>
                        "use object profiles and semicolon-separated absolute HTTP(S) applicationUrl strings with literal ports 1..65535",
                };
                if (IsDotnetProject(file))
                    hint =
                        "use readable ordinary project XML with an explicit Microsoft.NET.Sdk.Web SDK; project imports and MSBuild are never evaluated";
                throw new AboxException(
                    $"Cannot discover application ports in {JsonSerializer.Serialize(source)}. Fix file access, JSON structure or port configuration: {hint}. No ports were published."
                );
            }
        }
        return new AppPortDiscoveryPlan(result, dotnet);
    }

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

    private static bool IsDotnetProject(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".csproj" or ".fsproj" or ".vbproj";

    private static bool IsWebSdk(string? value) =>
        value?.Split('/')[0].Equals("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase)
        == true;

    private static IEnumerable<string> ConfigurationFiles(
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
            throw new AboxException(
                $"Cannot scan {JsonSerializer.Serialize(Path.GetRelativePath(root, directory))} for application ports. Check the --location and directory permissions; no ports were published."
            );
        }
        foreach (var entry in entries.OrderBy(entry => entry.Name, StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            if ((entry.Attributes & FileAttributes.Directory) != 0)
            {
                if (
                    !ExcludedDirectories.Contains(entry.Name)
                    && (entry.Attributes & FileAttributes.ReparsePoint) == 0
                )
                    foreach (var file in ConfigurationFiles(root, entry.FullName, depth + 1, token))
                        yield return file;
            }
            else if (
                entry.Name is "angular.json" or "package.json"
                || IsDotnetProject(entry.Name)
                || (
                    entry.Name == "launchSettings.json"
                    && Path.GetFileName(directory) == "Properties"
                )
            )
                yield return entry.FullName;
        }
    }
}
