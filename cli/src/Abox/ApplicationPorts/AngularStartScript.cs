using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Abox.ApplicationPorts;

internal static partial class AngularStartScript
{
    internal static void AddPorts(JsonElement root, string source, List<AppPortOccurrence> result)
    {
        if (
            !root.TryGetProperty("scripts", out var scripts)
            || !scripts.TryGetProperty("start", out var start)
        )
            return;
        if (start.ValueKind != JsonValueKind.String)
            throw new FormatException();
        var script = start.GetString()!;
        if (!AngularCommand().IsMatch(script) || !PortArgument().IsMatch(script))
            return;
        var args = LiteralArguments(script);
        if (
            args.Count < 3
            || args[0] != "ng"
            || args[1] is not ("serve" or "s" or "dev")
            || args.Contains("--")
        )
            throw new FormatException();
        int? port = null;
        for (var index = 2; index < args.Count; index++)
        {
            var arg = args[index];
            if (arg != "--port" && !arg.StartsWith("--port=", StringComparison.Ordinal))
            {
                if (PortArgument().IsMatch(arg))
                    throw new FormatException();
                continue;
            }
            if (port.HasValue)
                throw new FormatException();
            var value =
                arg == "--port" && ++index < args.Count ? args[index]
                : arg.StartsWith("--port=", StringComparison.Ordinal) ? arg[7..]
                : "";
            if (
                !int.TryParse(
                    value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var number
                ) || number is < 1 or > 65535
            )
                throw new FormatException();
            port = number;
        }
        if (!port.HasValue)
            throw new FormatException();
        result.Add(new AppPortOccurrence(source, "scripts.start", port.Value));
    }

    private static List<string> LiteralArguments(string script)
    {
        var args = new List<string>();
        var value = new StringBuilder();
        var quote = '\0';
        var started = false;
        foreach (var character in script)
        {
            // This is a deliberately literal subset, not a platform shell interpreter.
            if (
                char.IsControl(character) && character != '\t'
                || "\\$%!`;|&<>()#*?[]{}~".Contains(character)
            )
                throw new FormatException();
            if (character == quote)
                quote = '\0';
            else if (quote == '\0' && character is '\'' or '"')
            {
                quote = character;
                started = true;
            }
            else if (quote == '\0' && char.IsWhiteSpace(character))
            {
                if (started)
                    args.Add(value.ToString());
                value.Clear();
                started = false;
            }
            else
            {
                value.Append(character);
                started = true;
            }
        }
        if (quote != '\0')
            throw new FormatException();
        if (started)
            args.Add(value.ToString());
        return args;
    }

    [GeneratedRegex(@"(?<![\w-])--port(?![\w-])", RegexOptions.CultureInvariant)]
    private static partial Regex PortArgument();

    [GeneratedRegex("^\\s*(?:ng|'ng'|\"ng\")(?=\\s|$|[;&|<>])", RegexOptions.CultureInvariant)]
    private static partial Regex AngularCommand();
}
