using Abox.Infrastructure;

namespace Abox.Kits;

/// <summary>
/// The descriptor of a kit: what the kit needs from the host.
/// </summary>
public sealed class KitDescriptor
{
    /// <summary>The capability a kit uses to ask for a credential.</summary>
    public const string CredentialCapability = "com.docker.sandbox/credential@1";

    private KitDescriptor(string descriptorPath, IReadOnlyList<KitService> services)
    {
        DescriptorPath = descriptorPath;
        Services = services;
    }

    /// <summary>The descriptor file.</summary>
    public string DescriptorPath { get; }

    /// <summary>The services the kit authenticates to.</summary>
    public IReadOnlyList<KitService> Services { get; }

    /// <summary>
    /// Reads the descriptor of a kit directory. Returns null when the directory has no
    /// descriptor.
    /// </summary>
    public static KitDescriptor? Load(string kitDirectory)
    {
        var path = FindDescriptor(kitDirectory);
        if (path is null)
        {
            return null;
        }

        var reader = YamlReader.LoadFile(path);
        var services = new List<KitService>();

        foreach (var capability in reader.GetEntries("capabilities"))
        {
            if (capability.GetString("type") != CredentialCapability)
            {
                continue;
            }

            var config = capability.GetEntry("config");
            var service = config?.GetString("service");
            if (string.IsNullOrWhiteSpace(service))
            {
                continue;
            }

            var apiKey = config?.GetEntry("apiKey");
            var inject = apiKey?.GetEntries("inject") ?? [];

            services.Add(
                new KitService(
                    service,
                    ReadPhases(config),
                    apiKey?.GetString("name"),
                    inject
                        .Select(rule => rule.GetString("domain"))
                        .Where(domain => !string.IsNullOrWhiteSpace(domain))
                        .Select(domain => domain!)
                        .ToList()
                )
            );
        }

        return new KitDescriptor(path, services);
    }

    private static IReadOnlyList<string> ReadPhases(YamlEntry? config) =>
        config?.GetStrings("phase") ?? [];

    private static string? FindDescriptor(string kitDirectory)
    {
        if (!Directory.Exists(kitDirectory))
        {
            return null;
        }

        var conventional = Path.Combine(kitDirectory, Path.GetFileName(kitDirectory) + ".yaml");
        if (File.Exists(conventional))
        {
            return conventional;
        }

        var spec = Path.Combine(kitDirectory, "spec.yaml");
        if (File.Exists(spec))
        {
            return spec;
        }

        return Directory
            .EnumerateFiles(kitDirectory, "*.yaml")
            .OrderBy(file => file)
            .FirstOrDefault();
    }
}
