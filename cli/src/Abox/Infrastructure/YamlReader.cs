using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Abox.Infrastructure;

/// <summary>
/// Reads the small YAML documents abox uses. The shapes are known, so the reader is
/// strict: an unknown key or a value of the wrong shape is an error, not a surprise.
/// </summary>
public sealed class YamlReader
{
    private readonly YamlMappingNode _root;
    private readonly string _source;

    private YamlReader(YamlMappingNode root, string source)
    {
        _root = root;
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
    public bool Has(string key) => _root.Children.ContainsKey(ScalarKey(key));

    /// <summary>The value of a key, when it is a scalar.</summary>
    public string? GetString(string key)
    {
        if (!_root.Children.TryGetValue(ScalarKey(key), out var node))
        {
            return null;
        }

        if (node is not YamlScalarNode scalar)
        {
            throw new AboxException($"'{_source}': '{key}' must be a value.");
        }

        return scalar.Value;
    }

    /// <summary>The value of a key, when it is a list of scalars.</summary>
    public IReadOnlyList<string> GetStringList(string key)
    {
        if (!_root.Children.TryGetValue(ScalarKey(key), out var node))
        {
            return [];
        }

        if (node is not YamlSequenceNode sequence)
        {
            throw new AboxException($"'{_source}': '{key}' must be a list.");
        }

        return sequence.Children.Select(child => Scalar(child, key)).ToList();
    }

    /// <summary>The value of a key, when it is a mapping of scalars.</summary>
    public IReadOnlyDictionary<string, string> GetStringMap(string key)
    {
        if (!_root.Children.TryGetValue(ScalarKey(key), out var node))
        {
            return new Dictionary<string, string>();
        }

        if (node is not YamlMappingNode mapping)
        {
            throw new AboxException($"'{_source}': '{key}' must be a mapping.");
        }

        var values = new Dictionary<string, string>();
        foreach (var (childKey, childValue) in mapping.Children)
        {
            values[KeyName(childKey)] = Scalar(childValue, $"{key}.{KeyName(childKey)}");
        }

        return values;
    }

    /// <summary>The value of a key, when it is a list of scalars or mappings.</summary>
    public IReadOnlyList<YamlEntry> GetEntries(string key)
    {
        if (!_root.Children.TryGetValue(ScalarKey(key), out var node))
        {
            return [];
        }

        if (node is not YamlSequenceNode sequence)
        {
            throw new AboxException($"'{_source}': '{key}' must be a list.");
        }

        return sequence.Children.Select(child => new YamlEntry(child, _source, key)).ToList();
    }

    /// <summary>Fails when the document has keys abox does not know.</summary>
    public void RejectUnknownKeys(params string[] known)
    {
        foreach (var (childKey, _) in _root.Children)
        {
            var name = KeyName(childKey);
            if (!known.Contains(name))
            {
                throw new AboxException(
                    $"'{_source}': '{name}' is not a known key. Known keys: {string.Join(", ", known)}."
                );
            }
        }
    }

    private string Scalar(YamlNode node, string key)
    {
        if (node is not YamlScalarNode scalar)
        {
            throw new AboxException($"'{_source}': '{key}' must be a value.");
        }

        return scalar.Value ?? string.Empty;
    }

    private static YamlScalarNode ScalarKey(string key) => new(key);

    private static string KeyName(YamlNode node) =>
        node is YamlScalarNode scalar ? scalar.Value ?? string.Empty : node.ToString();
}

/// <summary>
/// One element of a list, which is either a value or a mapping.
/// </summary>
public sealed class YamlEntry
{
    private readonly string _source;
    private readonly string _key;

    internal YamlEntry(YamlNode node, string source, string key)
    {
        Node = node;
        _source = source;
        _key = key;
    }

    private YamlNode Node { get; }

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
            throw new AboxException($"'{_source}': '{_key}.{key}' must be a value.");
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
            throw new AboxException($"'{_source}': '{_key}.{key}' must be a mapping.");
        }

        var result = new Dictionary<string, string>();
        foreach (var (childKey, childValue) in values.Children)
        {
            var name = childKey is YamlScalarNode childScalar
                ? childScalar.Value ?? string.Empty
                : childKey.ToString();
            if (childValue is not YamlScalarNode value)
            {
                throw new AboxException($"'{_source}': '{_key}.{key}.{name}' must be a value.");
            }

            result[name] = value.Value ?? string.Empty;
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
            throw new AboxException($"'{_source}': '{_key}.{key}' must be a list.");
        }

        return sequence
            .Children.Select(child =>
            {
                if (child is not YamlScalarNode scalar)
                {
                    throw new AboxException(
                        $"'{_source}': '{_key}.{key}' must be a list of values."
                    );
                }

                return scalar.Value ?? string.Empty;
            })
            .ToList();
    }

    /// <summary>Fails when the element has keys abox does not know.</summary>
    public void RejectUnknownKeys(params string[] known)
    {
        foreach (var (childKey, _) in Mapping().Children)
        {
            var name = childKey is YamlScalarNode scalar
                ? scalar.Value ?? string.Empty
                : childKey.ToString();
            if (!known.Contains(name))
            {
                throw new AboxException(
                    $"'{_source}': '{_key}.{name}' is not a known key. Known keys: {string.Join(", ", known)}."
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
