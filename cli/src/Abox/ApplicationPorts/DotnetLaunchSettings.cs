using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Abox.ApplicationPorts;

internal sealed record AppPortOccurrence(string SourceFile, string Profile, int Port);

internal static partial class DotnetLaunchSettings
{
    internal static async Task<IReadOnlyList<AppPortOccurrence>> DiscoverAsync(
        string location,
        CancellationToken token
    )
    {
        var result = new List<AppPortOccurrence>();
        foreach (var file in LaunchSettingsFiles(location, location, 0, token))
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
                if (!document.RootElement.TryGetProperty("profiles", out var profiles))
                    continue;
                foreach (var profile in profiles.EnumerateObject())
                {
                    if (!profile.Value.TryGetProperty("applicationUrl", out var urls))
                        continue;
                    if (urls.ValueKind != JsonValueKind.String)
                        throw new FormatException();
                    foreach (
                        var url in urls.GetString()!
                            .Split(
                                ';',
                                StringSplitOptions.TrimEntries
                                    | StringSplitOptions.RemoveEmptyEntries
                            )
                    )
                    {
                        var match = ApplicationUrl().Match(url);
                        if (!match.Success)
                            throw new FormatException();
                        var host = match.Groups["host"];
                        var normalized = host.Value is "*" or "+"
                            ? url[..host.Index] + "localhost" + url[(host.Index + host.Length)..]
                            : url;
                        if (!Uri.TryCreate(normalized, UriKind.Absolute, out _))
                            throw new FormatException();
                        var explicitPort = match.Groups["port"];
                        if (!explicitPort.Success)
                            continue;
                        if (
                            !int.TryParse(
                                explicitPort.Value,
                                NumberStyles.None,
                                CultureInfo.InvariantCulture,
                                out var port
                            ) || port is < 1 or > 65535
                        )
                            throw new FormatException();
                        result.Add(new AppPortOccurrence(source, profile.Name, port));
                    }
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
                throw new AboxException(
                    $"Cannot discover application ports in {JsonSerializer.Serialize(source)}. Fix file access, JSON object/profile structure or applicationUrl: use semicolon-separated absolute HTTP(S) URLs with literal ports 1..65535. No ports were published."
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

    private static IEnumerable<string> LaunchSettingsFiles(
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
                    foreach (
                        var file in LaunchSettingsFiles(root, entry.FullName, depth + 1, token)
                    )
                        yield return file;
            }
            else if (
                entry.Name == "launchSettings.json"
                && Path.GetFileName(directory) == "Properties"
            )
                yield return entry.FullName;
        }
    }

    [GeneratedRegex(
        @"^https?://(?<host>\[[^\]\s]+\]|[^:/?#@\s]+)(?::(?<port>[0-9]+))?(?:[/?#][^\s]*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex ApplicationUrl();
}
