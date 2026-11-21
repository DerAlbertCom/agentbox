using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Abox.Sandboxes;

/// <summary>Reads the effective squad.json from ordered Docker-save layers, never extracting files.</summary>
internal static class PublishedConfigArchive
{
    private const int MaximumFileSize = 16 * 1024 * 1024;

    internal static string Read(string archive)
    {
        var layers = ReadLayerOrder(archive);
        var content = ReadLayers(archive, layers);
        string? json = null;
        foreach (var layer in layers)
            json = ApplyLayer(content[layer], json);
        return json ?? throw new JsonException("No bundled squad.json.");
    }

    private static List<string> ReadLayerOrder(string archive)
    {
        // Docker save supplies manifest.json and an ordered Layers list on classic and containerd stores.
        using var stream = File.OpenRead(archive);
        using var reader = new TarReader(stream);
        while (reader.GetNextEntry() is { } entry)
        {
            if (entry.Name != "manifest.json")
                continue;
            using var document = JsonDocument.Parse(ReadBytes(entry));
            var images = document.RootElement.EnumerateArray().ToList();
            if (images.Count != 1)
                throw new JsonException("Expected one config image.");
            return images[0]
                .GetProperty("Layers")
                .EnumerateArray()
                .Select(layer => layer.GetString()!)
                .ToList();
        }
        throw new JsonException("Missing Docker save manifest.");
    }

    private static Dictionary<string, byte[]> ReadLayers(string archive, List<string> layers)
    {
        var content = new Dictionary<string, byte[]>();
        using var stream = File.OpenRead(archive);
        using var reader = new TarReader(stream);
        while (reader.GetNextEntry() is { } entry)
        {
            if (layers.Contains(entry.Name))
                content.Add(entry.Name, ReadBytes(entry));
        }
        return content;
    }

    private static string? ApplyLayer(byte[] content, string? json)
    {
        using var bytes = new MemoryStream(content);
        using Stream decoded = content is [0x1f, 0x8b, ..]
            ? new GZipStream(bytes, CompressionMode.Decompress)
            : bytes;
        using var tar = new TarReader(decoded);
        while (tar.GetNextEntry() is { } entry)
        {
            var name = entry.Name.TrimStart('/');
            if (name.StartsWith("./", StringComparison.Ordinal))
                name = name[2..];
            if (name == "usr/share/abox/squad.json")
                json = ReadMetadata(entry);
            if (name is "usr/share/abox/.wh.squad.json" or "usr/share/abox/.wh..wh..opq")
                json = null;
        }
        return json;
    }

    private static string ReadMetadata(TarEntry entry)
    {
        if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
            throw new JsonException("Metadata must be a regular file.");
        return Encoding.UTF8.GetString(ReadBytes(entry));
    }

    private static byte[] ReadBytes(TarEntry entry)
    {
        if (entry.Length > MaximumFileSize || entry.DataStream is null)
            throw new JsonException("Invalid metadata layer size.");
        using var result = new MemoryStream();
        entry.DataStream.CopyTo(result);
        return result.ToArray();
    }
}
