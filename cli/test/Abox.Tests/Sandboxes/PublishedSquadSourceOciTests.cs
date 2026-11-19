using System.Text.Json;
using Abox.Infrastructure;
using Abox.Sandboxes;
using NSubstitute;
using static Abox.Tests.PublishedSquadSourceFixture;

namespace Abox.Tests;

public sealed class PublishedSquadSourceOciTests
{
    private const string ChildDigest =
        "sha256:3333333333333333333333333333333333333333333333333333333333333333";
    private const string LeafDigest =
        "sha256:4444444444444444444444444444444444444444444444444444444444444444";
    private readonly PublishedSquadSourceFixture _fixture = new();
    private IProcessRunner _runner => _fixture.Runner;
    private List<ProcessRequest> _calls => _fixture.Calls;
    private Dictionary<string, string> _rawManifests => _fixture.RawManifests;

    private Task<PublishedSquad> LoadAsync() => _fixture.LoadAsync();

    [Theory]
    [InlineData(null)]
    [InlineData("sha256:abc")]
    [InlineData("SHA256:1111111111111111111111111111111111111111111111111111111111111111")]
    public async Task InvalidResolvedDigestsCannotBeUsedToReadPublishedMetadata(string? digest)
    {
        // Arrange
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("--format")),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result(JsonSerializer.Serialize(new { digest })));

        // Act
        var action = LoadAsync;

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*invalid bundled squad metadata*No local metadata fallback*not replaced*"
        );
        _calls.Should().NotContain(call => call.Arguments.Contains("--raw"));
        _calls
            .Should()
            .NotContain(call => call.Arguments[0] == "pull" || call.Arguments[0] == "image");
    }

    [Theory]
    [InlineData("ambiguous")]
    [InlineData("missing-platform")]
    [InlineData("malformed-platform")]
    [InlineData("invalid-digest")]
    [InlineData("malformed-child")]
    [InlineData("nested-indexes")]
    public async Task UnusablePlatformIndexesFailBeforeDownloadingConfiguration(string error)
    {
        // Arrange
        var platform = new
        {
            digest = ChildDigest,
            platform = new { os = "linux", architecture = ArchitectureName },
        };
        object[] children = error switch
        {
            "ambiguous" => [platform, platform],
            "missing-platform" => [new { digest = ChildDigest }],
            "malformed-platform" => [new { digest = ChildDigest, platform = 42 }],
            "invalid-digest" =>
            [
                new
                {
                    digest = "sha256:abc",
                    platform = new { os = "linux", architecture = ArchitectureName },
                },
            ],
            "malformed-child" => [42],
            _ =>
            [
                new
                {
                    digest = PublishedSquadFactory.Digest,
                    platform = new { os = "linux", architecture = ArchitectureName },
                },
            ],
        };
        _rawManifests[PinnedSet] = JsonSerializer.Serialize(
            new { schemaVersion = 2, manifests = children }
        );

        // Act
        var action = LoadAsync;

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*republish*No local metadata fallback*not replaced*"
        );
        _calls
            .Should()
            .NotContain(call => call.Arguments[0] == "pull" || call.Arguments[0] == "image");
        _calls
            .Should()
            .NotContain(call =>
                call.Arguments.Last().EndsWith(ChildDigest, StringComparison.Ordinal)
            );
    }

    [Fact]
    public async Task NestedIndexesPinMetadataToTheSelectedPlatformWithoutRefloatingTags()
    {
        // Arrange
        _rawManifests[PinnedSet] = PlatformIndex(ChildDigest);
        var child = "rwest.jfrog.io/release/agentbox-rwest-mti-ca/ac@" + ChildDigest;
        _rawManifests[child] = PlatformIndex(LeafDigest);

        // Act
        var result = await LoadAsync();

        // Assert
        result
            .Reference.Should()
            .Be("rwest.jfrog.io/release/agentbox-rwest-mti-ca/ac@" + LeafDigest);
        result.Metadata.State.Single().RelativePath.Should().Be(".published");
        _calls
            .Where(call => call.Arguments.Contains("--format"))
            .Should()
            .ContainSingle()
            .Which.Arguments[^1]
            .Should()
            .Be(Reference);
        _calls
            .Should()
            .Contain(call => call.Arguments.Contains("--raw") && call.Arguments.Last() == child);
        _calls
            .Should()
            .Contain(call =>
                call.Arguments.Contains("--raw") && call.Arguments.Last() == result.Reference
            );
        _calls
            .Should()
            .NotContain(call =>
                call.Arguments.Contains("build") || call.Arguments.Contains("create")
            );
    }

    private static string PlatformIndex(string digest) =>
        JsonSerializer.Serialize(
            new
            {
                schemaVersion = 2,
                manifests = new[]
                {
                    new
                    {
                        digest,
                        platform = new { os = "linux", architecture = ArchitectureName },
                    },
                },
            }
        );

    [Theory]
    [InlineData("wrong-registry")]
    [InlineData("wrong-team")]
    [InlineData("wrong-squad")]
    [InlineData("tag-not-digest")]
    [InlineData("extra-path")]
    [InlineData("nested-repository")]
    [InlineData("duplicate")]
    public async Task OnlyOneExactDigestPinnedConfigurationIdentityCanBeDownloaded(string error)
    {
        // Arrange
        var config = error switch
        {
            "wrong-registry" => Config.Replace("rwest.jfrog.io", "unrelated.example"),
            "wrong-team" => Config.Replace("agentbox-rwest-mti-ca", "agentbox-rwest-other"),
            "wrong-squad" => Config.Replace("/ac/", "/other/"),
            "tag-not-digest" => Config.Replace("@" + PublishedSquadFactory.Digest, ":latest"),
            "extra-path" => Config + "/extra",
            "nested-repository" => Config.Replace("/published/", "/published/nested/"),
            _ => Config,
        };
        var refs = error == "duplicate" ? new[] { Config, Config } : new[] { config };
        _rawManifests[PinnedSet] = WorkloadWithConfigurations(refs);

        // Act
        var action = LoadAsync;

        // Assert
        await RejectAsync(action);
        _calls
            .Should()
            .NotContain(call => call.Arguments[0] == "pull" || call.Arguments[0] == "image");
    }

    private static string WorkloadWithConfigurations(IEnumerable<string> refs) =>
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
                            kind = "workload",
                            kits = refs.Select(value => new { @ref = value }),
                        }
                    ),
                },
            }
        );

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public async Task HostPlatformResolutionIsBoundedAtFourNestedIndexes(int depth)
    {
        // Arrange
        var repository = "rwest.jfrog.io/release/agentbox-rwest-mti-ca/ac@";
        var digest = PublishedSquadFactory.Digest;
        for (var index = 0; index < depth; index++)
        {
            var next = "sha256:" + new string((char)('2' + index), 64);
            _rawManifests[repository + digest] = PlatformIndex(next);
            digest = next;
        }

        // Act
        var action = LoadAsync;

        // Assert
        if (depth == 4)
            (await action()).Reference.Should().Be(repository + digest);
        else
        {
            await RejectAsync(action);
            _calls
                .Should()
                .NotContain(call => call.Arguments[0] == "pull" || call.Arguments[0] == "image");
        }
    }
}
