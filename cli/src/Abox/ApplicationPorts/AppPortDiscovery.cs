using System.Text.Json;

namespace Abox.ApplicationPorts;

internal sealed record AppPortOccurrence(string SourceFile, string Profile, int Port);

internal static class AppPortDiscovery
{
    internal static async Task<IReadOnlyList<AppPortOccurrence>> DiscoverAsync(
        string location,
        CancellationToken token
    )
    {
        var result = new List<AppPortOccurrence>();
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
                        "use an object scripts and a literal start script: ng serve (or ng s) with one --port N or --port=N, N in 1..65535; remove shell operators, variables, escapes and wrappers, or configure the port in angular.json",
                    _ =>
                        "use object profiles and semicolon-separated absolute HTTP(S) applicationUrl strings with literal ports 1..65535",
                };
                throw new AboxException(
                    $"Cannot discover application ports in {JsonSerializer.Serialize(source)}. Fix file access, JSON structure or port configuration: {hint}. No ports were published."
                );
            }
        }
        return result;
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
                || (
                    entry.Name == "launchSettings.json"
                    && Path.GetFileName(directory) == "Properties"
                )
            )
                yield return entry.FullName;
        }
    }
}
