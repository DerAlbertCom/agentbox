using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Abox;
using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class PublishedSquadSourceTests
{
    public static bool IsNotWindows => !OperatingSystem.IsWindows();

    private const string Reference = "rwest.jfrog.io/release/agentbox-rwest-mti-ca/ac:latest";
    private const string Config =
        "rwest.jfrog.io/published/agentbox-rwest-mti-ca/ac/components/config@"
        + PublishedSquadTest.Digest;
    private const string IndexDigest =
        "sha256:2222222222222222222222222222222222222222222222222222222222222222";
    private static readonly string ArchitectureName =
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "amd64";
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();
    private readonly IHostPrograms _programs = Substitute.For<IHostPrograms>();
    private readonly List<ProcessRequest> _calls = [];
    private readonly TestConsole _console = new();
    private readonly JsonObject _metadata = JsonNode
        .Parse(
            """
            {"SchemaVersion":1,"Team":"mti-ca","Squad":"ac","Tag":"latest","CreatedAt":"2026-11-09T12:00:00Z",
             "State":[{"SandboxPath":"/home/agent/.published","RelativePath":".published"}],
             "Mcp":[{"Name":"elastic","Url":"https://example.com/mcp"}]}
            """
        )!
        .AsObject();
    private bool _missingConfig;
    private bool _wrongDescriptor;
    private bool _missingMount;
    private bool _index;
    private bool _gzip;
    private byte[]? _layerBytes;
    private bool _lockArchive;
    private FileStream? _lockedArchive;
    private string? _metadataPath = "usr/share/abox/squad.json";
    private string? _fail;

    public PublishedSquadSourceTests()
    {
        _programs.Exists("docker").Returns(true);
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessRequest>();
                _calls.Add(request);
                var args = request.Arguments;
                if (args[0] == _fail)
                    return new ProcessResult(1, "", "access denied SECRET-NOT-FOR-OUTPUT");
                if (args[0] == "buildx")
                {
                    var config = args[^1] == Config;
                    if (args.Contains("--format"))
                    {
                        return Result(
                            JsonSerializer.Serialize(
                                new
                                {
                                    mediaType = "application/vnd.oci.image.manifest.v1+json",
                                    digest = _index ? IndexDigest : PublishedSquadTest.Digest,
                                    size = 123,
                                }
                            )
                        );
                    }
                    if (_index && args[^1].EndsWith(IndexDigest, StringComparison.Ordinal))
                    {
                        return Result(
                            JsonSerializer.Serialize(
                                new
                                {
                                    schemaVersion = 2,
                                    digest = PublishedSquadTest.Digest,
                                    manifests = new[]
                                    {
                                        new
                                        {
                                            digest = PublishedSquadTest.Digest,
                                            platform = new
                                            {
                                                os = "linux",
                                                architecture = ArchitectureName,
                                            },
                                        },
                                        new
                                        {
                                            digest = PublishedSquadTest.Digest,
                                            platform = new
                                            {
                                                os = "unknown",
                                                architecture = "unknown",
                                            },
                                        },
                                    },
                                }
                            )
                        );
                    }
                    var descriptor = new
                    {
                        schemaVersion = _wrongDescriptor ? "2" : "3",
                        kind = config ? "mixin" : "workload",
                        kits = _missingConfig ? Array.Empty<object>() : [new { @ref = Config }],
                        capabilities = _missingMount
                            ? Array.Empty<object>()
                            :
                            [
                                new
                                {
                                    type = "com.docker.sandbox/host-mount@1",
                                    config = new { path = "/home/agent/.published" },
                                },
                            ],
                    };
                    return Result(
                        JsonSerializer.Serialize(
                            new
                            {
                                schemaVersion = 2,
                                annotations = new Dictionary<string, string>
                                {
                                    ["vnd.docker.sandbox.kit.descriptor"] =
                                        JsonSerializer.Serialize(descriptor),
                                },
                            }
                        )
                    );
                }
                if (args[0] == "image")
                    WriteArchive(args[3]);
                return Result("");
            });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ReadsBundledMetadataAndPinsTheProcessedSetWithoutBuildingOrRunningAnything(
        bool index,
        bool gzip
    )
    {
        _index = index;
        _gzip = gzip;

        var result = await LoadAsync();

        result
            .Reference.Should()
            .Be("rwest.jfrog.io/release/agentbox-rwest-mti-ca/ac@" + PublishedSquadTest.Digest);
        result.Metadata.State.Single().RelativePath.Should().Be(".published");
        result.Metadata.Mcp.Single().Url.Should().Be("https://example.com/mcp");
        result.Metadata.CreatedAt.Should().Be(DateTimeOffset.Parse("2026-11-09T12:00:00Z"));
        _calls.Should().OnlyContain(call => call.FileName == "docker" && !call.AttachConsole);
        _calls
            .Select(call => call.Arguments[0])
            .Should()
            .Equal(
                index
                    ? ["buildx", "buildx", "buildx", "buildx", "pull", "image"]
                    : ["buildx", "buildx", "buildx", "pull", "image"]
            );
        _calls
            .Single(call => call.Arguments[0] == "pull")
            .Arguments.Should()
            .Equal("pull", "--platform", "linux/" + ArchitectureName, Config);
        File.Exists(_calls.Single(call => call.Arguments[0] == "image").Arguments[3])
            .Should()
            .BeFalse("temporary archives are cleaned up");
    }

    [Fact]
    public async Task AcceptsTheOriginalPublishedMetadataFormatWithoutAnExplicitVersion()
    {
        _metadata.Remove("SchemaVersion");
        (await LoadAsync()).Metadata.Squad.Should().Be("ac");
    }

    [Fact]
    public async Task RegistryIdentityIsCaseInsensitiveButTagsAreNot()
    {
        _metadata["Team"] = "MTI-CA";
        _metadata["Squad"] = "AC";
        (await LoadAsync()).Metadata.Squad.Should().Be("ac");
        _metadata["Tag"] = "LATEST";
        await RejectAsync();
    }

    [Theory]
    [InlineData("Team", "other")]
    [InlineData("Squad", "other")]
    [InlineData("Tag", "other")]
    [InlineData("CreatedAt", "not-a-date")]
    public async Task RejectsWrongIdentityAndMalformedTimestamps(string field, string value)
    {
        _metadata[field] = value;
        await RejectAsync();
    }

    [Theory]
    [InlineData("State")]
    [InlineData("Mcp")]
    [InlineData("Team")]
    public async Task MissingRequiredFieldsCannotSilentlyDisableRuntimeConfiguration(string field)
    {
        _metadata.Remove(field);
        await RejectAsync();
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("/absolute")]
    [InlineData("C:/outside")]
    [InlineData("..\\outside")]
    [InlineData("different")]
    public async Task RejectsUnsafeOrInconsistentHostStatePaths(string path)
    {
        _metadata["State"]![0]!["RelativePath"] = path;
        await RejectAsync();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("//")]
    [InlineData("/home/agent/../outside")]
    [InlineData("/home/agent/.published/")]
    [InlineData("/home/agent//.published")]
    public async Task RejectsNonCanonicalSandboxPaths(string path)
    {
        _metadata["State"]![0]!["SandboxPath"] = path;
        await RejectAsync();
    }

    [Theory]
    [InlineData("State")]
    [InlineData("Mcp")]
    public async Task RejectsDuplicateRuntimeEntries(string field)
    {
        _metadata[field]!.AsArray().Add(_metadata[field]![0]!.DeepClone());
        await RejectAsync();
    }

    [Theory]
    [InlineData("file:///local")]
    [InlineData("https://user:password@example.com/mcp")]
    [InlineData("not-a-url")]
    public async Task RejectsInvalidMcpUrls(string url)
    {
        _metadata["Mcp"]![0]!["Url"] = url;
        await RejectAsync();
    }

    [Theory]
    [InlineData("State")]
    [InlineData("Mcp")]
    public async Task RejectsNullEntries(string field)
    {
        _metadata[field] = JsonNode.Parse("[null]");
        await RejectAsync();
    }

    [Fact]
    public async Task RejectsUnsupportedMetadataSchema()
    {
        _metadata["SchemaVersion"] = 2;
        await RejectAsync();
    }

    [Fact]
    public async Task MissingBundledMetadataRequiresRepublishingInsteadOfLocalPreparation()
    {
        _metadataPath = null;
        await RejectAsync();
    }

    [Fact]
    public async Task SymlinkMetadataIsNotFollowed()
    {
        _metadataPath = "symlink";
        await RejectAsync();
    }

    [Fact]
    public async Task CorruptGzipIsReportedAsInvalidPublishedMetadata()
    {
        _layerBytes = [0x1f, 0x8b, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

        await RejectAsync();
    }

    [Theory(
        SkipWhen = nameof(IsNotWindows),
        Skip = "Windows file sharing is needed to reproduce a locked download archive."
    )]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupFailureWarnsWithoutMaskingTheResultOrMetadataError(bool corrupt)
    {
        _lockArchive = true;
        if (corrupt)
        {
            _layerBytes = [0x1f, 0x8b, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        }

        try
        {
            if (corrupt)
            {
                await RejectAsync();
            }
            else
            {
                (await LoadAsync()).Metadata.Squad.Should().Be("ac");
            }
            _console
                .Errors.Should()
                .Contain(error => error.Contains("warning:") && error.Contains("could not remove"));
        }
        finally
        {
            _lockedArchive?.Dispose();
            var archive = _calls.Single(call => call.Arguments[0] == "image").Arguments[3];
            Directory.Delete(Path.GetDirectoryName(archive)!, recursive: true);
        }
    }

    [Theory]
    [InlineData("config")]
    [InlineData("descriptor")]
    [InlineData("mount")]
    public async Task InvalidPublishedDescriptorsFailClosed(string error)
    {
        _missingConfig = error == "config";
        _wrongDescriptor = error == "descriptor";
        _missingMount = error == "mount";
        await RejectAsync();
    }

    [Theory]
    [InlineData("buildx")]
    [InlineData("pull")]
    [InlineData("image")]
    public async Task RegistryErrorsAreActionableAndDoNotEchoToolSecrets(string operation)
    {
        _fail = operation;
        var action = LoadAsync;
        var error = (await action.Should().ThrowAsync<AboxException>()).Which;
        error
            .Message.Should()
            .Contain("read access")
            .And.Contain("not replaced")
            .And.NotContain("SECRET-NOT-FOR-OUTPUT");
    }

    [Fact]
    public async Task MissingDockerDoesNotRecommendALocalBuild()
    {
        _programs.Exists("docker").Returns(false);
        var action = LoadAsync;
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*Install Docker*no local build*"
        );
        _calls.Should().BeEmpty();
    }

    private Task<PublishedSquad> LoadAsync() =>
        new PublishedSquadSource(new OciRegistry(_runner), _programs, _console).LoadAsync(
            Reference,
            "mti-ca",
            "ac",
            "latest",
            CancellationToken.None
        );

    private async Task RejectAsync()
    {
        var action = LoadAsync;
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*republish*No local metadata fallback*not replaced*"
        );
    }

    private static ProcessResult Result(string output) => new(0, output, "");

    private void WriteArchive(string path)
    {
        using var layer = new MemoryStream();
        using (var writer = new TarWriter(layer, leaveOpen: true))
        {
            if (_metadataPath == "symlink")
            {
                writer.WriteEntry(
                    new PaxTarEntry(TarEntryType.SymbolicLink, "usr/share/abox/squad.json")
                    {
                        LinkName = "../../outside",
                    }
                );
            }
            else if (_metadataPath is not null)
            {
                Write(writer, _metadataPath, Encoding.UTF8.GetBytes(_metadata.ToJsonString()));
            }
        }
        var bytes = layer.ToArray();
        if (_gzip)
        {
            using var compressed = new MemoryStream();
            using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
                gzip.Write(bytes);
            bytes = compressed.ToArray();
        }
        using (var file = File.Create(path))
        using (var outer = new TarWriter(file))
        {
            Write(outer, "manifest.json", Encoding.UTF8.GetBytes("[{\"Layers\":[\"layer.tar\"]}]"));
            Write(outer, "layer.tar", _layerBytes ?? bytes);
        }
        if (_lockArchive)
        {
            _lockedArchive = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
    }

    private static void Write(TarWriter writer, string name, byte[] bytes)
    {
        using var data = new MemoryStream(bytes);
        writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = data });
    }
}
