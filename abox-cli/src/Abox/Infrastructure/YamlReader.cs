using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Abox.Infrastructure;

/// <summary>
/// Reads the small YAML documents abox uses. The shapes are known, so the reader is
/// strict: an unknown key or a value of the wrong shape is an error, not a surprise.
/// </summary>
public sealed class YamlReader
{
    private readonly YamlEntry _root;
    private readonly string _source;

    private YamlReader(YamlMappingNode root, string source)
    {
        _root = new YamlEntry(root, source, null);
        _source = source;
    }

    /// <summary>Reads a YAML file.</summary>
    public static YamlReader LoadFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new AboxException($"'{path}' does not exist.");
        }

        return Parse(File.ReadAllText(path), path);
    }

    /// <summary>Reads a YAML document.</summary>
    public static YamlReader Parse(string yaml, string source)
    {
        YamlNode root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            root =
                stream.Documents.Count > 0
                    ? stream.Documents[0].RootNode
                    : new YamlScalarNode(string.Empty);
        }
        catch (YamlException exception)
        {
            throw new AboxException(
                $"'{source}' is not valid YAML: {exception.Message}",
                exception
            );
        }

        if (root is not YamlMappingNode mapping)
        {
            if (root is YamlScalarNode { Value: "" or null })
            {
                return new YamlReader(new YamlMappingNode(), source);
            }

            throw new AboxException($"'{source}' must contain a mapping at the top level.");
        }

        return new YamlReader(mapping, source);
    }

    /// <summary>The file this reader reads from.</summary>
    public string Source => _source;

    /// <summary>Whether the document has this key.</summary>
    public bool Has(string key) => _root.Has(key);

    /// <summary>The nested value of a key, or null when it is absent.</summary>
    public YamlEntry? GetEntry(string key) => _root.GetEntry(key);

    /// <summary>The value of a key, when it is a scalar.</summary>
    public string? GetString(string key) => _root.GetString(key);

    /// <summary>The value of a key, when it is a list of scalars.</summary>
    public IReadOnlyList<string> GetStringList(string key) => _root.GetStringList(key);

    /// <summary>The value of a key, when it is a mapping of scalars.</summary>
    public IReadOnlyDictionary<string, string> GetStringMap(string key) => _root.GetStringMap(key);

    /// <summary>The value of a key, when it is a list of scalars or mappings.</summary>
    public IReadOnlyList<YamlEntry> GetEntries(string key) => _root.GetEntries(key);

    /// <summary>Fails when the document has keys abox does not know.</summary>
    public void RejectUnknownKeys(params string[] known) => _root.RejectUnknownKeys(known);
}

/// <summary>
/// A YAML value or mapping, including the root of a document.
/// </summary>
public sealed class YamlEntry
{
    private readonly string _source;
    private readonly string? _key;

    internal YamlEntry(YamlNode node, string source, string? key)
    {
        Node = node;
        _source = source;
        _key = key;
    }

    private YamlNode Node { get; }

    private string KeyPath(string key) => _key is null ? key : $"{_key}.{key}";

    private static string KeyName(YamlNode node) =>
        node is YamlScalarNode scalar ? scalar.Value ?? string.Empty : node.ToString();

    /// <summary>Whether this mapping has a key.</summary>
    public bool Has(string key) => Mapping().Children.ContainsKey(new YamlScalarNode(key));

    /// <summary>Whether the element is a plain value.</summary>
    public bool IsScalar => Node is YamlScalarNode;

    /// <summary>The value of the element, when it is a plain value.</summary>
    public string ScalarValue =>
        Node is YamlScalarNode scalar ? scalar.Value ?? string.Empty : string.Empty;

    /// <summary>The value of a key of the element, when the element is a mapping.</summary>
    public string? GetString(string key)
    {
        var mapping = Mapping();
        if (!mapping.Children.TryGetValue(new YamlScalarNode(key), out var node))
        {
            return null;
        }

        if (node is not YamlScalarNode scalar)
        {
            throw new AboxException($"'{_source}': '{KeyPath(key)}' must be a value.");
        }

        return scalar.Value;
    }

    /// <summary>The value of a key of the element, when it is a mapping of values.</summary>
    public IReadOnlyDictionary<string, string> GetStringMap(string key)
    {
        var mapping = Mapping();
        if (!mapping.Children.TryGetValue(new YamlScalarNode(key), out var node))
        {
            return new Dictionary<string, string>();
        }

        if (node is not YamlMappingNode values)
        {
            throw new AboxException($"'{_source}': '{KeyPath(key)}' must be a mapping.");
        }

        var result = new Dictionary<string, string>();
        foreach (var (childKey, childValue) in values.Children)
        {
            var name = KeyName(childKey);
            result[name] = Scalar(childValue, $"{KeyPath(key)}.{name}");
        }

        return result;
    }

    /// <summary>The value of a key of the element, when it is a list of values.</summary>
    public IReadOnlyList<string> GetStringList(string key)
    {
        var mapping = Mapping();
        if (!mapping.Children.TryGetValue(new YamlScalarNode(key), out var node))
        {
            return [];
        }

        if (node is not YamlSequenceNode sequence)
        {
            throw new AboxException($"'{_source}': '{KeyPath(key)}' must be a list.");
        }

        return sequence.Children.Select(child => Scalar(child, KeyPath(key))).ToList();
    }

    /// <summary>
    /// The value of a key of the element, as a list of values: a single value counts as
    /// a list of one.
    /// </summary>
    public IReadOnlyList<string> GetStrings(string key)
    {
        var mapping = Mapping();
        if (!mapping.Children.TryGetValue(new YamlScalarNode(key), out var node))
        {
            return [];
        }

        if (node is YamlScalarNode)
        {
            var value = Scalar(node, KeyPath(key));
            return value.Length == 0 ? [] : [value];
        }

        if (node is not YamlSequenceNode sequence)
        {
            throw new AboxException($"'{_source}': '{KeyPath(key)}' must be a value or a list.");
        }

        return sequence.Children.Select(child => Scalar(child, KeyPath(key))).ToList();
    }

    private string Scalar(YamlNode node, string key)
    {
        if (node is not YamlScalarNode scalar)
        {
            throw new AboxException($"'{_source}': '{key}' must be a value.");
        }

        return scalar.Value ?? string.Empty;
    }

    /// <summary>The value of a key of the element, when it is a mapping.</summary>
    public YamlEntry? GetEntry(string key)
    {
        var mapping = Mapping();
        if (!mapping.Children.TryGetValue(new YamlScalarNode(key), out var node))
        {
            return null;
        }

        if (_key is not null && node is not YamlMappingNode)
        {
            throw new AboxException($"'{_source}': '{KeyPath(key)}' must be a mapping.");
        }

        return new YamlEntry(node, _source, KeyPath(key));
    }

    /// <summary>The value of a key of the element, when it is a list of mappings or values.</summary>
    public IReadOnlyList<YamlEntry> GetEntries(string key)
    {
        var mapping = Mapping();
        if (!mapping.Children.TryGetValue(new YamlScalarNode(key), out var node))
        {
            return [];
        }

        if (node is not YamlSequenceNode sequence)
        {
            throw new AboxException($"'{_source}': '{KeyPath(key)}' must be a list.");
        }

        return sequence
            .Children.Select(child => new YamlEntry(child, _source, KeyPath(key)))
            .ToList();
    }

    /// <summary>Fails when the element has keys abox does not know.</summary>
    public void RejectUnknownKeys(params string[] known)
    {
        foreach (var (childKey, _) in Mapping().Children)
        {
            var name = KeyName(childKey);
            if (!known.Contains(name))
            {
                throw new AboxException(
                    $"'{_source}': '{KeyPath(name)}' is not a known key. Known keys: {string.Join(", ", known)}."
                );
            }
        }
    }

    private YamlMappingNode Mapping()
    {
        if (Node is not YamlMappingNode mapping)
        {
            throw new AboxException(
                $"'{_source}': every '{_key}' entry must be a value or a mapping."
            );
        }

        return mapping;
    }
}
