namespace Abox.UserSecrets;

internal sealed class OracleEasyConnectOptions(string suffix)
{
    private int position;

    internal static bool Valid(string suffix)
    {
        var question = suffix.IndexOf('?');
        return question < 0
            || new OracleEasyConnectOptions(suffix) { position = question + 1 }.Read();
    }

    private bool Read()
    {
        while (position < suffix.Length)
        {
            if (!ReadKey() || !ReadValue())
                return false;
            if (
                position < suffix.Length
                && (suffix[position++] != '&' || position == suffix.Length)
            )
                return false;
        }
        return true;
    }

    private bool ReadKey()
    {
        var start = position;
        while (
            position < suffix.Length
            && (char.IsAsciiLetterOrDigit(suffix[position]) || suffix[position] is '_' or '.')
        )
            position++;
        var key = suffix[start..position].ToUpperInvariant();
        return key.Length > 0
            && position < suffix.Length
            && suffix[position++] == '='
            && key
                is not (
                    "HOST"
                    or "PORT"
                    or "PROTOCOL"
                    or "ADDRESS"
                    or "ADDRESS_LIST"
                    or "DESCRIPTION"
                    or "DESCRIPTION_LIST"
                    or "HTTPS_PROXY"
                    or "HTTPS_PROXY_PORT"
                );
    }

    private bool ReadValue()
    {
        if (position < suffix.Length && suffix[position] is '\'' or '"')
            return ReadQuotedValue(suffix[position++]);
        while (position < suffix.Length)
        {
            var character = suffix[position++];
            if (character == '&')
            {
                position--;
                return true;
            }
            else if (char.IsWhiteSpace(character) || character is '\'' or '"' or '(' or ')' or '\\')
                return false;
        }
        return true;
    }

    private bool ReadQuotedValue(char quote)
    {
        while (position < suffix.Length)
        {
            var character = suffix[position++];
            if (character == '\\' && position < suffix.Length)
                position++;
            else if (character == quote)
                return true;
        }
        return false;
    }
}
