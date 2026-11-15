using System.Text.Json;
using Abox.Infrastructure;
using Abox.LocalState;
using NSubstitute;

namespace Abox.Tests;

public sealed partial class PublishServiceTests
{
    private const string VerifiedDigest =
        "sha256:5555555555555555555555555555555555555555555555555555555555555555";

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"schemaVersion":1}""")]
    [InlineData("""{"schemaVersion":2,"annotations":42}""")]
    [InlineData(
        """{"schemaVersion":2,"annotations":{"vnd.docker.sandbox.kit.descriptor":"not-json"}}"""
    )]
    [InlineData(
        """{"schemaVersion":2,"annotations":{"vnd.docker.sandbox.kit.descriptor":"{\"schemaVersion\":\"2\",\"kind\":\"mixin\"}"}}"""
    )]
    [InlineData(
        """{"schemaVersion":2,"annotations":{"vnd.docker.sandbox.kit.descriptor":"{\"schemaVersion\":\"3\",\"kind\":\"workload\"}"}}"""
    )]
    [InlineData("""{"schemaVersion":2,"manifests":[]}""")]
    [InlineData("""{"schemaVersion":2,"manifests":[{"annotations":42}]}""")]
    [InlineData("""{"schemaVersion":2,"manifests":[{"digest":"sha256:abc"}]}""")]
    public async Task InvalidRegistryKitsStopPublicationBeforeAnyPush(string manifest)
    {
        // Arrange
        SaveRegistryKitBuild();
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("--raw")),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ProcessResult(0, manifest, ""));

        // Act
        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*did not retain a valid v3 mixin descriptor*Publication stopped*"
        );
        _calls.Should().NotContain(call => call.Arguments.Contains("--push"));
        _calls.Should().NotContain(call => call.Arguments.Contains("create"));
        _console.AllText.Should().NotContain("Published squad");
    }

    [Fact]
    public async Task ExcessivelyNestedRegistryKitsStopPublicationWithoutPromotingATag()
    {
        // Arrange
        SaveRegistryKitBuild();
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("--raw")),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ProcessResult(0, PublicationIndex(VerifiedDigest), ""));

        // Act
        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*has too many nested image indexes*"
        );
        _calls.Should().NotContain(call => call.Arguments.Contains("--push"));
        _calls.Should().NotContain(call => call.Arguments.Contains("create"));
    }

    [Fact]
    public async Task AnInvalidPlatformKitPreventsPublicationEvenWhenAnotherPlatformIsValid()
    {
        // Arrange
        SaveRegistryKitBuild();
        const string invalidDigest =
            "sha256:6666666666666666666666666666666666666666666666666666666666666666";
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("--raw")),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var reference = call.Arg<ProcessRequest>().Arguments.Last();
                var manifest =
                    reference.EndsWith(VerifiedDigest, StringComparison.Ordinal)
                        ? PublishTestProcess.Manifest("mixin")
                    : reference.EndsWith(invalidDigest, StringComparison.Ordinal)
                        ? PublishTestProcess.Manifest("workload")
                    : PublicationIndex(VerifiedDigest, invalidDigest);
                return new ProcessResult(0, manifest, "");
            });

        // Act
        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*did not retain a valid v3 mixin descriptor*"
        );
        _calls
            .Should()
            .Contain(call => call.Arguments.Last() == "ghcr.io/example/tool@" + invalidDigest);
        _calls.Should().NotContain(call => call.Arguments.Contains("--push"));
        _calls.Should().NotContain(call => call.Arguments.Contains("create"));
    }

    [Fact]
    public async Task NestedRegistryKitsUseThePublicationBuilderAndPermitAVerifiedSet()
    {
        // Arrange
        SaveRegistryKitBuild();
        const string leafDigest =
            "sha256:7777777777777777777777777777777777777777777777777777777777777777";
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call =>
                    call.Arguments.Contains("--raw") && call.Arguments.Last().StartsWith("ghcr.io/")
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var reference = call.Arg<ProcessRequest>().Arguments.Last();
                var manifest = reference.EndsWith(leafDigest, StringComparison.Ordinal)
                    ? PublishTestProcess.Manifest("mixin")
                    : PublicationIndex(
                        reference.EndsWith(VerifiedDigest, StringComparison.Ordinal)
                            ? leafDigest
                            : VerifiedDigest
                    );
                return new ProcessResult(0, manifest, "");
            });

        // Act
        var result = await _service.PublishAsync(
            Request("ac") with
            {
                Builder = "publisher",
            },
            CancellationToken.None
        );

        // Assert
        result.Should().Be(0);
        _calls
            .Where(call => call.Arguments.Contains("--raw"))
            .Should()
            .OnlyContain(call =>
                call.Arguments.Contains("--builder") && call.Arguments.Contains("publisher")
            );
        _calls
            .Should()
            .Contain(call => call.Arguments.Last() == "ghcr.io/example/tool@" + leafDigest);
        _calls.Should().ContainSingle(call => call.Arguments.Contains("create"));
        _console.AllText.Should().Contain("Published squad 'ac'");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not-json")]
    [InlineData("""{"containerimage.digest":"sha256:abc"}""")]
    [InlineData("""{"containerimage.digest":42}""")]
    public async Task MissingOrInvalidPushedDigestsNeverReachPromotion(string metadata)
    {
        // Arrange
        SaveBuild("ac");
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("--push")),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var args = call.Arg<ProcessRequest>().Arguments.ToList();
                File.WriteAllText(args[args.IndexOf("--metadata-file") + 1], metadata);
                return new ProcessResult(0, "", "");
            });

        // Act
        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*Cannot read the pushed digest*requested set tag was not promoted*"
        );
        _calls.Should().NotContain(call => call.Arguments.Contains("create"));
        _console.AllText.Should().NotContain("Published squad");
    }

    [Fact]
    public async Task PublicationInspectsPushedDigestsAndPromotesOnlyTheImmutableCandidate()
    {
        // Arrange
        SaveBuild("ac");
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("--push")),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var args = call.Arg<ProcessRequest>().Arguments.ToList();
                File.WriteAllText(
                    args[args.IndexOf("--metadata-file") + 1],
                    JsonSerializer.Serialize(
                        new Dictionary<string, string>
                        {
                            ["containerimage.digest"] = VerifiedDigest,
                        }
                    )
                );
                return new ProcessResult(0, "", "");
            });

        // Act
        await _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        const string repository =
            "rwest.jfrog.io/mtiae-developer-container-images-local/agentbox-rwest-sample/ac";
        var inspected = _calls
            .Where(call => call.Arguments.Contains("--raw"))
            .Select(call => call.Arguments.Last());
        inspected.Should().Contain(repository + "/components/workload@" + VerifiedDigest);
        inspected.Should().Contain(repository + "/components/config@" + VerifiedDigest);
        inspected.Should().Contain(repository + "@" + VerifiedDigest);
        var promotion = _calls.Single(call => call.Arguments.Contains("create"));
        promotion.Arguments.Should().Contain("--prefer-index=false");
        promotion.Arguments.Should().Contain(repository + ":latest");
        promotion.Arguments.Last().Should().Be(repository + "@" + VerifiedDigest);
        _calls.Last().Arguments.Should().Contain(repository + ":latest");
    }

    private static string PublicationIndex(params string[] digests) =>
        JsonSerializer.Serialize(
            new { schemaVersion = 2, manifests = digests.Select(digest => new { digest }) }
        );

    private void SaveRegistryKitBuild()
    {
        SaveBuild("ac");
        var build = _store.Load("sample", "ac", "latest")!;
        build.Kits.Add(new ManifestKit { Name = "tool", Reference = "ghcr.io/example/tool:1" });
        SaveVerifiedBuild(build);
    }
}
