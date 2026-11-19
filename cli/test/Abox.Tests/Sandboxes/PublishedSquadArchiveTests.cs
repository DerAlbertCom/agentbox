using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sandboxes;
using NSubstitute;

namespace Abox.Tests;

public sealed class PublishedSquadArchiveTests
{
    private const string MetadataPath = "usr/share/abox/squad.json";
    private const string Metadata = """
        {"Team":"mti-ca","Squad":"ac","Tag":"latest","CreatedAt":"2026-11-09T12:00:00Z",
         "State":[],"Mcp":[{"Name":"elastic","Url":"https://example.com/current"}]}
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManifestLayerOrderWinsOverArchiveEntryOrder(bool gzip)
    {
        // Arrange
        var old = Layer(MetadataPath, Metadata.Replace("/current", "/obsolete"));
        var current = Layer("./" + MetadataPath, Metadata);
        var scenario = new PublishedArchiveScenario(Save(old, gzip ? Gzip(current) : current));

        // Act
        var result = await scenario.LoadAsync();

        // Assert
        result.Metadata.Mcp.Single().Url.Should().Be("https://example.com/current");
        scenario.AssertReadOnlyAndCleaned();
    }

    [Theory]
    [InlineData(".wh.squad.json", false)]
    [InlineData(".wh..wh..opq", false)]
    [InlineData(".wh.squad.json", true)]
    [InlineData(".wh..wh..opq", true)]
    public async Task WhiteoutsRemoveOlderMetadataButLaterLayersCanRestoreIt(
        string marker,
        bool restore
    )
    {
        // Arrange
        var original = Layer(MetadataPath, Metadata.Replace("/current", "/obsolete"));
        var whiteout = Layer("usr/share/abox/" + marker, "");
        var layers = restore
            ? new[] { original, whiteout, Layer(MetadataPath, Metadata) }
            : new[] { original, whiteout };
        var scenario = new PublishedArchiveScenario(Save(layers));

        // Act
        var action = scenario.LoadAsync;

        // Assert
        if (restore)
            (await action()).Metadata.Mcp.Single().Url.Should().Be("https://example.com/current");
        else
            (await action.Should().ThrowAsync<AboxException>()).WithMessage(
                "*republish*No local metadata fallback*not replaced*"
            );
        scenario.AssertReadOnlyAndCleaned();
    }

    [Theory]
    [InlineData(TarEntryType.SymbolicLink)]
    [InlineData(TarEntryType.HardLink)]
    [InlineData(TarEntryType.Directory)]
    [InlineData(TarEntryType.Fifo)]
    public async Task NonregularMetadataCannotReplaceAnEarlierRegularFile(TarEntryType type)
    {
        // Arrange
        using var stream = new MemoryStream();
        using (var writer = new TarWriter(stream, leaveOpen: true))
        {
            var entry = new PaxTarEntry(type, MetadataPath);
            if (type is TarEntryType.SymbolicLink or TarEntryType.HardLink)
                entry.LinkName = "../../outside";
            writer.WriteEntry(entry);
        }
        var scenario = new PublishedArchiveScenario(
            Save(Layer(MetadataPath, Metadata), stream.ToArray())
        );

        // Act
        var action = scenario.LoadAsync;

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*republish*No local metadata fallback*not replaced*"
        );
        scenario.AssertReadOnlyAndCleaned();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedMetadataIsRejectedEvenWhenItsLayerIsCompressed(bool gzip)
    {
        // Arrange
        var oversized = Layer(MetadataPath, Metadata + new string(' ', 16 * 1024 * 1024));
        var scenario = new PublishedArchiveScenario(Save(gzip ? Gzip(oversized) : oversized));

        // Act
        var action = scenario.LoadAsync;

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*republish*No local metadata fallback*not replaced*"
        );
        scenario.AssertReadOnlyAndCleaned();
    }

    [Fact]
    public async Task MetadataAtTheSizeLimitRemainsReadableFromAGzipLayer()
    {
        // Arrange
        var metadata = Metadata.PadRight(16 * 1024 * 1024, ' ');
        var scenario = new PublishedArchiveScenario(Save(Gzip(Layer(MetadataPath, metadata))));

        // Act
        var result = await scenario.LoadAsync();

        // Assert
        result.Metadata.Mcp.Single().Name.Should().Be("elastic");
        scenario.AssertReadOnlyAndCleaned();
    }

    [Theory]
    [InlineData("/usr/share/abox/squad.json")]
    [InlineData("./usr/share/abox/squad.json")]
    public async Task V7RegularMetadataAndSupportedArchivePathPrefixesRemainCompatible(string path)
    {
        // Arrange
        using var stream = new MemoryStream();
        using var data = new MemoryStream(Encoding.UTF8.GetBytes(Metadata));
        using (var writer = new TarWriter(stream, leaveOpen: true))
            writer.WriteEntry(
                new V7TarEntry(TarEntryType.V7RegularFile, path) { DataStream = data }
            );
        var scenario = new PublishedArchiveScenario(Save(stream.ToArray()));

        // Act
        var result = await scenario.LoadAsync();

        // Assert
        result.Metadata.Mcp.Single().Name.Should().Be("elastic");
        scenario.AssertReadOnlyAndCleaned();
    }

    [Theory]
    [InlineData("missing-manifest")]
    [InlineData("multiple-images")]
    [InlineData("missing-layer")]
    public async Task IncompleteDockerSavesCannotProvideRuntimeMetadata(string error)
    {
        // Arrange
        using var stream = new MemoryStream();
        using (var writer = new TarWriter(stream, leaveOpen: true))
        {
            if (error == "multiple-images")
                Write(writer, "manifest.json", "[{\"Layers\":[]},{\"Layers\":[]}]"u8.ToArray());
            if (error == "missing-layer")
                Write(writer, "manifest.json", "[{\"Layers\":[\"missing.tar\"]}]"u8.ToArray());
        }
        var scenario = new PublishedArchiveScenario(stream.ToArray());

        // Act
        var action = scenario.LoadAsync;

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*republish*No local metadata fallback*not replaced*"
        );
        scenario.AssertReadOnlyAndCleaned();
    }

    private static byte[] Layer(string path, string content)
    {
        using var stream = new MemoryStream();
        using (var writer = new TarWriter(stream, leaveOpen: true))
            Write(writer, path, Encoding.UTF8.GetBytes(content));
        return stream.ToArray();
    }

    private static byte[] Gzip(byte[] bytes)
    {
        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(bytes);
        return stream.ToArray();
    }

    private static byte[] Save(params byte[][] layers)
    {
        using var stream = new MemoryStream();
        using (var writer = new TarWriter(stream, leaveOpen: true))
        {
            var names = Enumerable.Range(0, layers.Length).Select(i => $"layer-{i}.tar").ToArray();
            Write(
                writer,
                "manifest.json",
                JsonSerializer.SerializeToUtf8Bytes(new[] { new { Layers = names } })
            );
            // Physical order deliberately disagrees with the manifest, as valid Docker saves may do.
            for (var i = layers.Length - 1; i >= 0; i--)
                Write(writer, names[i], layers[i]);
        }
        return stream.ToArray();
    }

    private static void Write(TarWriter writer, string path, byte[] bytes)
    {
        using var data = new MemoryStream(bytes);
        writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, path) { DataStream = data });
    }
}

// A single saved config image, with fixed registry responses and no mutable fake-dispatch modes.
internal sealed class PublishedArchiveScenario
{
    private const string Reference = "rwest.jfrog.io/release/agentbox-rwest-mti-ca/ac:latest";
    private const string Config =
        "rwest.jfrog.io/published/agentbox-rwest-mti-ca/ac/components/config@"
        + PublishedSquadFactory.Digest;
    private readonly List<ProcessRequest> _calls = [];
    private readonly PublishedSquadSource _source;
    private string? _archivePath;

    public PublishedArchiveScenario(byte[] archive)
    {
        var runner = Substitute.For<IProcessRunner>();
        var programs = Substitute.For<IHostPrograms>();
        programs.Exists("docker").Returns(true);
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Respond(call.Arg<ProcessRequest>(), archive));
        _source = new PublishedSquadSource(new OciRegistry(runner), programs, new TestConsole());
    }

    public Task<PublishedSquad> LoadAsync() =>
        _source.LoadAsync(
            Reference,
            new SquadBuildIdentity("mti-ca", "ac", "latest"),
            CancellationToken.None
        );

    public void AssertReadOnlyAndCleaned()
    {
        _calls.Should().OnlyContain(call => call.FileName == "docker" && !call.AttachConsole);
        _calls
            .Select(call => call.Arguments[0])
            .Should()
            .Equal("buildx", "buildx", "buildx", "pull", "image");
        Directory.Exists(Path.GetDirectoryName(_archivePath)).Should().BeFalse();
    }

    private ProcessResult Respond(ProcessRequest request, byte[] archive)
    {
        _calls.Add(request);
        var args = request.Arguments;
        if (args.Contains("--format"))
            return new ProcessResult(
                0,
                JsonSerializer.Serialize(new { digest = PublishedSquadFactory.Digest }),
                ""
            );
        if (args.Contains("--raw"))
            return new ProcessResult(0, Manifest(args[^1] == Config ? "mixin" : "workload"), "");
        if (args[0] == "image")
        {
            _archivePath = args[3];
            File.WriteAllBytes(_archivePath, archive);
        }
        else if (args[0] != "pull")
            throw new InvalidOperationException("Unexpected Docker operation.");
        return new ProcessResult(0, "", "");
    }

    private static string Manifest(string kind) =>
        JsonSerializer.Serialize(
            new
            {
                schemaVersion = 2,
                annotations = new Dictionary<string, string>
                {
                    [OciRegistry.DescriptorAnnotation] = JsonSerializer.Serialize(
                        new
                        {
                            schemaVersion = "3",
                            kind,
                            kits = new[] { new { @ref = Config } },
                            capabilities = Array.Empty<object>(),
                        }
                    ),
                },
            }
        );
}
