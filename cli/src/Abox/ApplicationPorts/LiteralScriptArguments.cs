using System.Text;

namespace Abox.ApplicationPorts;

// A deliberately literal subset, not a platform shell interpreter.
internal static class LiteralScriptArguments
{
    internal static IReadOnlyList<string> Tokenize(string script)
    {
        var args = new List<string>();
        var value = new StringBuilder();
        var quote = '\0';
        var started = false;
        foreach (var character in script)
        {
            ValidateCharacter(character);
            if (character == quote)
            {
                quote = '\0';
                continue;
            }
            if (quote == '\0' && character is '\'' or '"')
            {
                quote = character;
                started = true;
                continue;
            }
            if (quote != '\0' || !char.IsWhiteSpace(character))
            {
                value.Append(character);
                started = true;
                continue;
            }
            if (started)
                args.Add(value.ToString());
            value.Clear();
            started = false;
        }
        if (quote != '\0')
            throw new FormatException();
        if (started)
            args.Add(value.ToString());
        return args;
    }

    private static void ValidateCharacter(char character)
    {
        // Unsafe characters remain unsafe inside quotes; quoting is not shell escaping.
        if (
            char.IsControl(character) && character != '\t'
            || "\\$%!`;|&<>()#*?[]{}~".Contains(character)
        )
            throw new FormatException();
    }
}
