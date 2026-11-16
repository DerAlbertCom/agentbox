using System.Text.RegularExpressions;

namespace Abox.UserSecrets;

// Bounded structural syntax retains partial trees and original leaf spans. A
// later syntax error must not erase already-known mapped ADDRESS candidates.
internal sealed partial class OracleDescriptorSyntax(string value)
{
    private int position;
    private bool valid = true;
    internal bool Complete { get; private set; }

    internal static bool Recognizes(string value) => OracleDescriptorPrefix().IsMatch(value);

    internal Node? Parse()
    {
        var root = ReadNode(0);
        Whitespace();
        Complete = valid && position == value.Length;
        return root;
    }

    private Node? ReadNode(int depth)
    {
        Whitespace();
        if (depth > 64 || !Take('('))
        {
            valid = false;
            return null;
        }
        var key = ReadKey();
        if (key.Length == 0 || !Take('='))
        {
            valid = false;
            return null;
        }
        var node = new Node(key);
        Whitespace();
        if (position < value.Length && value[position] == '(')
            ReadChildren(node, depth);
        else
            ReadLeaf(node);
        Whitespace();
        valid &= Take(')');
        return node;
    }

    private string ReadKey()
    {
        Whitespace();
        var start = position;
        while (
            position < value.Length
            && (char.IsAsciiLetterOrDigit(value[position]) || value[position] is '_' or '.')
        )
            position++;
        var key = value[start..position].ToUpperInvariant();
        Whitespace();
        return key;
    }

    private void ReadChildren(Node node, int depth)
    {
        while (valid && position < value.Length && value[position] == '(')
        {
            var child = ReadNode(depth + 1);
            if (child is not null)
                node.Children.Add(child);
            Whitespace();
        }
    }

    private void ReadLeaf(Node node)
    {
        if (position < value.Length && value[position] is '\'' or '"')
        {
            ReadQuotedLeaf(node, value[position++]);
            return;
        }
        node.Start = position;
        while (position < value.Length && value[position] != ')')
        {
            if (value[position] is '(' or '\'' or '"')
            {
                valid = false;
                break;
            }
            position++;
        }
        node.Length = position - node.Start;
        while (node.Length > 0 && char.IsWhiteSpace(value[node.Start + node.Length - 1]))
            node.Length--;
    }

    private void ReadQuotedLeaf(Node node, char quote)
    {
        node.Start = position;
        while (position < value.Length && value[position] != quote)
        {
            if (value[position] == '\\' && position + 1 < value.Length)
                position++;
            position++;
        }
        node.Length = position - node.Start;
        valid &= Take(quote);
    }

    private bool Take(char character)
    {
        if (position == value.Length || value[position] != character)
            return false;
        position++;
        return true;
    }

    private void Whitespace()
    {
        while (position < value.Length && char.IsWhiteSpace(value[position]))
            position++;
    }

    internal sealed class Node(string key)
    {
        internal string Key { get; } = key;
        internal int Start { get; set; }
        internal int Length { get; set; }
        internal List<Node> Children { get; } = [];
    }

    [GeneratedRegex(
        @"\A\s*\(\s*(?:DESCRIPTION(?:_LIST)?|ADDRESS(?:_LIST)?)\s*=",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex OracleDescriptorPrefix();
}
