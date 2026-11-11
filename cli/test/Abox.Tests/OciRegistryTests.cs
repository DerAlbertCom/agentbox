using System.Text.Json;
using Abox;
using Abox.Configuration;
using Abox.Features.Publish;
using Abox.Infrastructure;
using Abox.LocalState;
using Abox.Sandboxes;
using NSubstitute;

namespace Abox.Tests;

public sealed class OciRegistryTests
{
    private const string Reference = "registry.example/team/kit:latest";
    private const string Digest =
        "sha256:1111111111111111111111111111111111111111111111111111111111111111";
    private const string Pinned = "registry.example/team/kit@" + Digest;
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();

    [Theory]
    [InlineData(null, false)]
    [InlineData("sha256:abc", false)]
    [InlineData("SHA256:1111111111111111111111111111111111111111111111111111111111111111", false)]
    [InlineData(Digest, true)]
    public void DigestValidationRetainsItsExactFormat(string? digest, bool valid) =>
        OciRegistry.IsDigest(digest).Should().Be(valid);

    [Fact]
    public async Task PublicationInspectsEachChildBeforeReadingTheNextSibling()
    {
        using var root = new TempDirectory();
        var calls = new List<string>();
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var reference = call.Arg<ProcessRequest>().Arguments[^1];
                calls.Add(reference);
                return Success(
                    reference == Reference
                        ? JsonSerializer.Serialize(
                            new
                            {
                                schemaVersion = 2,
                                manifests = new object[]
                                {
                                    new { digest = Digest },
                                    new { annotations = 42 },
                                },
                            }
                        )
                        : Leaf("mixin")
                );
            });
        var publisher = Publisher(root.Path);

        var act = () => publisher.InspectAsync(Reference, "mixin", CancellationToken.None);

        await act.Should()
            .ThrowAsync<AboxException>()
            .WithMessage(
                $"Registry artifact '{Reference}' did not retain a valid v3 mixin descriptor. Publication stopped: *"
            );
        calls.Should().Equal(Reference, Pinned);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NestedIndexesRetainTheirOperationSpecificDepthErrors(bool publication)
    {
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Success(
                    JsonSerializer.Serialize(
                        new
                        {
                            schemaVersion = 2,
                            manifests = new[]
                            {
                                new
                                {
                                    digest = Digest,
                                    platform = new { os = "linux", architecture = "amd64" },
                                },
                            },
                        }
                    )
                )
            );
        using var root = new TempDirectory();

        if (publication)
        {
            var act = () =>
                Publisher(root.Path).InspectAsync(Pinned, "mixin", CancellationToken.None);
            await act.Should()
                .ThrowAsync<AboxException>()
                .WithMessage($"Registry artifact '{Pinned}' has too many nested image indexes.");
        }
        else
        {
            var act = () =>
                MetadataSource().ResolveManifestAsync(Pinned, "amd64", CancellationToken.None);
            await act.Should()
                .ThrowAsync<JsonException>()
                .WithMessage("Too many nested image indexes.");
        }
        await _runner.Received(5).RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RuntimeReadsRejectAmbiguousPlatformsBeforeInspectingAnyChild()
    {
        var platform = new
        {
            digest = Digest,
            platform = new { os = "linux", architecture = "amd64" },
        };
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Success(
                    JsonSerializer.Serialize(
                        new { schemaVersion = 2, manifests = new[] { platform, platform } }
                    )
                )
            );

        var act = () =>
            MetadataSource().ResolveManifestAsync(Pinned, "amd64", CancellationToken.None);

        await act.Should()
            .ThrowAsync<JsonException>()
            .WithMessage("No unambiguous runnable platform manifest.");
        await _runner.Received(1).RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    private static ProcessResult Success(string output) => new(0, output, "");

    private PublishService Publisher(string root) =>
        new(
            new BuildStore(AboxPaths.For(root, root)),
            Substitute.For<IHostPrograms>(),
            new OciRegistry(_runner),
            new TestConsole()
        );

    private PublishedSquadSource MetadataSource() =>
        new(new OciRegistry(_runner), Substitute.For<IHostPrograms>(), new TestConsole());

    [Fact]
    public async Task FailedDockerInvocationsReturnTheUnmodifiedResult()
    {
        var failure = new ProcessResult(17, "output", "error");
        _runner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>()).Returns(failure);

        var result = await new OciRegistry(_runner).RunAsync(
            ["buildx", "version"],
            true,
            CancellationToken.None
        );

        result.Should().BeSameAs(failure);
        await _runner
            .Received()
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "docker" && request.AttachConsole
                ),
                CancellationToken.None
            );
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "publisher")]
    public async Task InspectionOnlySelectsDockerArguments(bool raw, string? builder)
    {
        var expected = new List<string> { "buildx", "imagetools" };
        if (builder is not null)
            expected.AddRange(["--builder", builder]);
        expected.AddRange(
            raw
                ? ["inspect", "--raw", Reference]
                : ["inspect", "--format", "{{json .Manifest}}", Reference]
        );
        var response = Success("not parsed by infrastructure");
        _runner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>()).Returns(response);

        var result = await new OciRegistry(_runner).InspectAsync(
            Reference,
            raw,
            builder,
            CancellationToken.None
        );

        result.Should().BeSameAs(response);
        await _runner
            .Received()
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.SequenceEqual(expected) && !request.AttachConsole
                ),
                CancellationToken.None
            );
    }

    private static string Leaf(string kind) =>
        JsonSerializer.Serialize(
            new
            {
                schemaVersion = 2,
                annotations = new Dictionary<string, string>
                {
                    ["vnd.docker.sandbox.kit.descriptor"] = JsonSerializer.Serialize(
                        new { schemaVersion = "3", kind }
                    ),
                },
            }
        );
}
