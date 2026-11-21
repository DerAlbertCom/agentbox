using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sandboxes;
using NSubstitute;

namespace Abox.Tests;

internal sealed class PublishedSquadSourceFixture
{
    internal const string Reference = "rwest.jfrog.io/release/agentic-box-rwest-mti-ca/ac:latest";
    internal const string Config =
        "rwest.jfrog.io/published/agentic-box-rwest-mti-ca/ac/components/config@"
        + PublishedSquadFactory.Digest;
    private const string IndexDigest =
        "sha256:2222222222222222222222222222222222222222222222222222222222222222";
    internal const string PinnedSet =
        "rwest.jfrog.io/release/agentic-box-rwest-mti-ca/ac@" + PublishedSquadFactory.Digest;
    internal static readonly string ArchitectureName =
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "amd64";
    internal IProcessRunner Runner { get; } = Substitute.For<IProcessRunner>();
    internal IHostPrograms Programs { get; } = Substitute.For<IHostPrograms>();
    internal List<ProcessRequest> Calls { get; } = [];
    internal TestConsole Console { get; } = new();
    internal JsonObject Metadata { get; } =
        JsonNode
            .Parse(
                """
                {"SchemaVersion":1,"Team":"mti-ca","Squad":"ac","Tag":"latest","CreatedAt":"2026-11-09T12:00:00Z",
                 "State":[{"SandboxPath":"/home/agent/.published","RelativePath":".published"}],
                 "Mcp":[{"Name":"elastic","Url":"https://example.com/mcp"}]}
                """
            )!
            .AsObject();
    internal Dictionary<string, string> RawManifests { get; } = [];
    internal bool MissingConfig { get; set; }
    internal bool WrongDescriptor { get; set; }
    internal bool MissingMount { get; set; }
    internal bool Index { get; set; }
    internal bool Gzip { get; set; }
    internal byte[]? LayerBytes { get; set; }
    internal bool LockArchive { get; set; }
    internal FileStream? LockedArchive { get; private set; }
    internal string? MetadataPath { get; set; } = "usr/share/abox/squad.json";
    internal string? Failure { get; set; }

    internal PublishedSquadSourceFixture()
    {
        Programs.Exists("docker").Returns(true);
        Runner
            .When(runner =>
                runner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            )
            .Do(call => Calls.Add(call.Arg<ProcessRequest>()));
        Runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Respond(call.Arg<ProcessRequest>()));
    }

    private ProcessResult Respond(ProcessRequest request)
    {
        var args = request.Arguments;
        if (args[0] == Failure)
            return new ProcessResult(1, "", "access denied SECRET-NOT-FOR-OUTPUT");
        if (args[0] == "buildx")
        {
            if (args.Contains("--raw") && RawManifests.TryGetValue(args[^1], out var raw))
                return Result(raw);
            var config = args[^1] == Config;
            if (args.Contains("--format"))
                return Result(
                    JsonSerializer.Serialize(
                        new
                        {
                            mediaType = "application/vnd.oci.image.manifest.v1+json",
                            digest = Index ? IndexDigest : PublishedSquadFactory.Digest,
                            size = 123,
                        }
                    )
                );
            if (Index && args[^1].EndsWith(IndexDigest, StringComparison.Ordinal))
                return Result(
                    JsonSerializer.Serialize(
                        new
                        {
                            schemaVersion = 2,
                            digest = PublishedSquadFactory.Digest,
                            manifests = new[]
                            {
                                new
                                {
                                    digest = PublishedSquadFactory.Digest,
                                    platform = new
                                    {
                                        os = "linux",
                                        architecture = ArchitectureName,
                                    },
                                },
                                new
                                {
                                    digest = PublishedSquadFactory.Digest,
                                    platform = new { os = "unknown", architecture = "unknown" },
                                },
                            },
                        }
                    )
                );
            var descriptor = new
            {
                schemaVersion = WrongDescriptor ? "2" : "3",
                kind = config ? "mixin" : "workload",
                kits = MissingConfig ? Array.Empty<object>() : [new { @ref = Config }],
                capabilities = MissingMount
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
                            ["vnd.docker.sandbox.kit.descriptor"] = JsonSerializer.Serialize(
                                descriptor
                            ),
                        },
                    }
                )
            );
        }
        if (args[0] == "image")
            WriteArchive(args[3]);
        return Result("");
    }

    internal Task<PublishedSquad> LoadAsync() =>
        new PublishedSquadSource(new OciRegistry(Runner), Programs, Console).LoadAsync(
            Reference,
            new SquadBuildIdentity("mti-ca", "ac", "latest"),
            CancellationToken.None
        );

    internal static async Task RejectAsync(Func<Task<PublishedSquad>> action)
    {
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*republish*No local metadata fallback*not replaced*"
        );
    }

    internal static ProcessResult Result(string output) => new(0, output, "");

    private void WriteArchive(string path)
    {
        using var layer = new MemoryStream();
        using (var writer = new TarWriter(layer, leaveOpen: true))
        {
            if (MetadataPath == "symlink")
                writer.WriteEntry(
                    new PaxTarEntry(TarEntryType.SymbolicLink, "usr/share/abox/squad.json")
                    {
                        LinkName = "../../outside",
                    }
                );
            else if (MetadataPath is not null)
                Write(writer, MetadataPath, Encoding.UTF8.GetBytes(Metadata.ToJsonString()));
        }
        var bytes = layer.ToArray();
        if (Gzip)
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
            Write(outer, "layer.tar", LayerBytes ?? bytes);
        }
        if (LockArchive)
            LockedArchive = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private static void Write(TarWriter writer, string name, byte[] bytes)
    {
        using var data = new MemoryStream(bytes);
        writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = data });
    }
}
