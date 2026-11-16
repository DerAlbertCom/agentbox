namespace Abox.UserSecrets;

// Value spans exclude outer quotes and unquoted trailing whitespace. Doubled quote
// escapes remain encoded in the original source; the scanner does not decode values.
internal sealed record ConnectionField(string Key, int Start, int Length);

// Complete fields remain usable for fail-closed endpoint recognition after a later error.
// PartialField retains a known key and the attempted value prefix up to StoppedAt,
// not a successfully parsed field (an invalid delimiter can leave a closing quote
// in this prefix). Unique covers detected keys, including that partial field.
// Consumers must opt into inspecting it, never search inside another field's value,
// and never treat Complete=false as an empty connection string. StoppedAt is the
// parser cursor, not the next field's start; no missing syntax is inferred.
internal sealed record ConnectionStringScan(
    bool Complete,
    bool Unique,
    IReadOnlyList<ConnectionField> Fields,
    ConnectionField? PartialField,
    int StoppedAt
)
{
    internal bool CompleteAndUnique => Complete && Unique;
}

internal sealed class ConnectionStringScanner(string value)
{
    private int position;
    private ConnectionField? partial;

    internal static ConnectionStringScan Scan(string value) =>
        new ConnectionStringScanner(value).Read();

    private ConnectionStringScan Read()
    {
        var fields = new List<ConnectionField>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = true;
        while (position < value.Length)
        {
            SkipSeparators();
            if (position == value.Length)
                break;
            var field = ReadField();
            if (field is null)
            {
                unique &= partial is null || keys.Add(partial.Key);
                return new(false, unique, fields, partial, position);
            }
            unique &= keys.Add(field.Key);
            fields.Add(field);
        }
        return new(true, unique, fields, null, position);
    }

    private ConnectionField? ReadField()
    {
        partial = null;
        var key = ReadKey();
        if (key is null)
            return null;
        position++;
        SkipWhitespace();
        var start = position;
        var end = ReadValue(ref start);
        if (end is null)
        {
            partial = new(key, start, position - start);
            return null;
        }
        return new(key, start, end.Value - start);
    }

    private string? ReadKey()
    {
        var start = position;
        while (position < value.Length && value[position] != '=')
        {
            var character = value[position++];
            if (!(char.IsAsciiLetterOrDigit(character) || character is ' ' or '_' or '-'))
                return null;
        }
        if (position == value.Length)
            return null;
        var key = value[start..position].Trim();
        return key.Length == 0 ? null : key;
    }

    private int? ReadValue(ref int start)
    {
        if (position < value.Length && value[position] is '\'' or '"')
        {
            var quote = value[position++];
            start = position;
            return ReadQuotedValue(quote);
        }
        while (position < value.Length && value[position] != ';')
        {
            if (value[position] is '\'' or '"' or '{' or '}')
                return null;
            position++;
        }
        var end = position;
        while (end > start && char.IsWhiteSpace(value[end - 1]))
            end--;
        return end;
    }

    private int? ReadQuotedValue(char quote)
    {
        while (position < value.Length)
        {
            if (value[position] != quote)
            {
                position++;
                continue;
            }
            if (position + 1 < value.Length && value[position + 1] == quote)
            {
                position += 2;
                continue;
            }
            var end = position++;
            SkipWhitespace();
            return position < value.Length && value[position] != ';' ? null : end;
        }
        return null;
    }

    private void SkipWhitespace()
    {
        while (position < value.Length && char.IsWhiteSpace(value[position]))
            position++;
    }

    private void SkipSeparators()
    {
        while (
            position < value.Length
            && (char.IsWhiteSpace(value[position]) || value[position] == ';')
        )
            position++;
    }
}
