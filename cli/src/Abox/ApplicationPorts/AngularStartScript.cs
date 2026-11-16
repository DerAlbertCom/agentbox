using System.Globalization;
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
        var args = AcceptedCommandArguments(start.GetString()!);
        if (args is null)
            return;
        result.Add(new AppPortOccurrence(source, "scripts.start", ReadSinglePort(args)));
    }

    private static IReadOnlyList<string>? AcceptedCommandArguments(string script)
    {
        // Check applicability before tokenizing: unrelated scripts and scripts without
        // an explicit port are not interpreted, even when they contain shell syntax.
        if (!AngularCommand().IsMatch(script) || !PortArgument().IsMatch(script))
            return null;
        var args = LiteralScriptArguments.Tokenize(script);
        if (args.Count < 3 || args.Contains("--"))
            throw new FormatException();
        if (args[0] != "ng" || args[1] is not ("serve" or "s" or "dev"))
            throw new FormatException();
        return args;
    }

    private static int ReadSinglePort(IReadOnlyList<string> args)
    {
        int? port = null;
        for (var index = 2; index < args.Count; index++)
        {
            if (!IsPortFlag(args[index]))
                continue;
            if (port.HasValue)
                throw new FormatException();
            port = DecodePortValue(args, ref index);
        }
        return port ?? throw new FormatException();
    }

    private static bool IsPortFlag(string argument)
    {
        if (argument == "--port" || argument.StartsWith("--port=", StringComparison.Ordinal))
            return true;
        if (PortArgument().IsMatch(argument))
            throw new FormatException();
        return false;
    }

    private static int DecodePortValue(IReadOnlyList<string> args, ref int index)
    {
        string value;
        if (args[index] == "--port")
            value = ++index < args.Count ? args[index] : "";
        else
            value = args[index]["--port=".Length..];
        if (
            !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535
        )
            throw new FormatException();
        return port;
    }

    [GeneratedRegex(@"(?<![\w-])--port(?![\w-])", RegexOptions.CultureInvariant)]
    private static partial Regex PortArgument();

    [GeneratedRegex("^\\s*(?:ng|'ng'|\"ng\")(?=\\s|$|[;&|<>])", RegexOptions.CultureInvariant)]
    private static partial Regex AngularCommand();
}
