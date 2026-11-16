using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Abox.ApplicationPorts;

internal static partial class DotnetLaunchSettings
{
    internal static void AddPorts(JsonElement root, string source, List<AppPortOccurrence> result)
    {
        if (!root.TryGetProperty("profiles", out var profiles))
            return;
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
                        StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
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

    [GeneratedRegex(
        @"^https?://(?<host>\[[^\]\s]+\]|[^:/?#@\s]+)(?::(?<port>[0-9]+))?(?:[/?#][^\s]*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex ApplicationUrl();
}
