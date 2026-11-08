using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Abox.Infrastructure;

/// <summary>
/// The YAML reading abox does. One place, so every file is read the same way.
/// </summary>
public static class Yaml
{
    /// <summary>
    /// Reads a YAML document into <typeparamref name="T"/>. Unknown keys and duplicate keys are errors.
    /// </summary>
    public static T Deserialize<T>(string yaml, string source)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithDuplicateKeyChecking()
            .Build();

        try
        {
            return deserializer.Deserialize<T>(yaml)
                ?? throw new AboxException($"'{source}' is empty.");
        }
        catch (YamlException exception)
        {
            throw new AboxException(
                $"'{source}' is not valid YAML: {exception.Message}",
                exception
            );
        }
    }
}
