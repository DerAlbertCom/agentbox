using Abox.Configuration;
using Abox.Features.Build;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Naming;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class BuildServiceTests
{
    private readonly BuildServiceFixture _fixture = new();
    private TempDirectory _root => _fixture.Root;
    private ISbxClient _sbx => _fixture.Sbx;
    private IProcessRunner _runner => _fixture.Runner;
    private TestConsole _console => _fixture.Console;
    private BuildStore _store => _fixture.Store;
    private SquadResolver _resolver => _fixture.Resolver;
    private BuildService _service => _fixture.Service;

    [Fact]
    public async Task EachBuildKeepsTheRepositoryOfItsInheritedWorkload()
    {
        // Arrange
        _root.File("teams/sample/ac.abox", "base: cape\n");
        _root.File("teams/other/cape.abox", "workload: shared\n");
        _root.File("kits/shared.workload", "publishRepository: shared-images-local\n");
        _root.File("teams/sample/bd.abox", "workload: sample\n");
        _root.File("teams/sample/sample.workload", "publishRepository: sample-images-local\n");

        // Act
        await _service.BuildAsync(
            Request() with
            {
                IncludePersonalConfigurations = false,
            },
            CancellationToken.None
        );

        // Assert
        _store
            .Load(new SquadBuildIdentity("sample", "ac", "latest"))!
            .PublishRepository.Should()
            .Be("shared-images-local");
        _store
            .Load(new SquadBuildIdentity("sample", "bd", "latest"))!
            .PublishRepository.Should()
            .Be("sample-images-local");
    }

    [Fact]
    public async Task ChangingTheWorkloadRepositoryRequiresRebuildingBeforePublication()
    {
        // Arrange
        _root.File("teams/sample/sample.workload", "publishRepository: original-images-local\n");
        await _service.BuildAsync(
            Request() with
            {
                IncludePersonalConfigurations = false,
            },
            CancellationToken.None
        );
        _root.File("teams/sample/sample.workload", "publishRepository: other-images-local\n");

        // Act
        var check = () =>
            BuildSources.EnsureUnchanged(
                _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!
            );

        // Assert
        check.Should().Throw<AboxException>().WithMessage("*sources changed*--no-personal*");
    }

    [Fact]
    public async Task PublicationBuildsExplicitlyExcludePersonalConfiguration()
    {
        // Arrange: the fixture supplies sources with personal configuration.
        // Act
        await _service.BuildAsync(
            Request() with
            {
                IncludePersonalConfigurations = false,
            },
            CancellationToken.None
        );

        // Assert
        var manifest = _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        manifest.IncludesPersonalConfigurations.Should().BeFalse();
        manifest.Kits.Should().BeEmpty();
        manifest.ConfigFiles.Should().NotContain(file => file.Contains("personal.abox"));
        manifest.SourceHashes.Should().NotBeEmpty();
        BuildSources.EnsureUnchanged(manifest);
    }

    [Fact]
    public async Task TheCertificateAssetsAreFingerprintedAndIncludedInPublicationSnapshots()
    {
        // Arrange: the fixture supplies a publishable workload and bundled assets.
        // Act
        await _service.BuildAsync(
            Request() with
            {
                IncludePersonalConfigurations = false,
            },
            CancellationToken.None
        );
        // Assert
        var manifest = _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        var snapshot = BuildSources.Snapshot(manifest, _root.CreateDirectory("snapshot"));
        var asset = Path.Combine("certificates", "rwe_inspection_certificate_chain.pem");
        File.ReadAllBytes(Path.Combine(snapshot[manifest.WorkloadKitDirectory], asset))
            .Should()
            .Equal(File.ReadAllBytes(Path.Combine(manifest.WorkloadKitDirectory, asset)));

        // Arrange
        File.AppendAllText(Path.Combine(manifest.WorkloadKitDirectory, asset), "\n# changed\n");
        // Act
        var check = () => BuildSources.EnsureUnchanged(manifest);
        // Assert
        check.Should().Throw<AboxException>().WithMessage("*sources changed*");
    }

    [Fact]
    public async Task LocalBuildsStillIncludePersonalConfigurationByDefaultAndRecordIt()
    {
        // Arrange: use the fixture's personal configuration and default build request.
        // Act
        await _service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        var manifest = _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        manifest.IncludesPersonalConfigurations.Should().BeTrue();
        manifest.Kits.Should().ContainSingle().Which.Name.Should().Be("private");
        manifest.SourceHashes.Should().BeEmpty();
    }

    [Theory(SkipExceptions = [typeof(LinkUnavailableException)])]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnlyPublicationBuildsEnforceLinkFreeInputs(bool includePersonalConfigurations)
    {
        // Arrange
        _root.File("teams/sample/ac.abox", "workload: sample\nkits: [private]\n");
        File.Delete(Path.Combine(_root.Path, "config", "personal.abox"));
        var target = _root.CreateDirectory("shared");
        _root.File("shared/payload.txt", "linked kit payload");
        var link = Path.Combine(_root.Path, "kits", "private", "linked");
        try
        {
            var result = await DirectoryLinks.CreateAsync(link, target);
            if (result is not null && !result.Succeeded)
            {
                throw new LinkUnavailableException(
                    $"Directory junctions are not available: {result.StandardError}"
                );
            }
        }
        catch (Exception exception)
            when (exception
                    is UnauthorizedAccessException
                        or PlatformNotSupportedException
                        or IOException
            )
        {
            throw new LinkUnavailableException(
                $"Symbolic links are not available on this host: {exception.Message}"
            );
        }

        var request = Request() with
        {
            IncludePersonalConfigurations = includePersonalConfigurations,
        };
        if (includePersonalConfigurations)
        {
            // Act
            var exitCode = await _service.BuildAsync(request, CancellationToken.None);

            // Assert
            exitCode.Should().Be(0);
            _store
                .Load(new SquadBuildIdentity("sample", "ac", "latest"))!
                .SourceHashes.Should()
                .BeEmpty();
            await _sbx.Received(1)
                .InspectKitAsync(
                    Path.Combine(_root.Path, "kits", "private"),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<IReadOnlyDictionary<string, string>>()
                );
        }
        else
        {
            // Act
            var action = () => _service.BuildAsync(request, CancellationToken.None);

            // Assert
            (await action.Should().ThrowAsync<AboxException>()).WithMessage(
                "*symbolic link or reparse point*"
            );
            _store.Load(new SquadBuildIdentity("sample", "ac", "latest")).Should().BeNull();
        }
    }

    [Fact]
    public async Task SourcesChangedDuringVerificationAreNotSavedAsAVerifiedBuild()
    {
        // Arrange
        _root.File("teams/sample/ac.abox", "workload: sample\nkits: [private]\n");
        _sbx.InspectKitAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<IReadOnlyDictionary<string, string>>()
            )
            .Returns(call =>
            {
                File.AppendAllText(
                    Path.Combine(
                        _store.DirectoryFor(new SquadBuildIdentity("sample", "ac", "latest")),
                        "workload",
                        "workload.yaml"
                    ),
                    "description: changed during verification\n"
                );
                return new SbxOutcome(0, "");
            });

        // Act
        var action = () =>
            _service.BuildAsync(
                Request() with
                {
                    IncludePersonalConfigurations = false,
                },
                CancellationToken.None
            );

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*sources changed*");
        _store.Load(new SquadBuildIdentity("sample", "ac", "latest")).Should().BeNull();
    }

    private BuildRequest Request() =>
        new([_resolver.ResolveTeam(_root.Path, "sample")], _root.Path, "latest");

    [Fact]
    public async Task NoVerifyStillPrebuildsTheWorkloadAndRecordsItsContentIdentity()
    {
        // Arrange: use the fixture's local workload and fake image builder.
        // Act
        await _service.BuildAsync(Request() with { Verify = false }, CancellationToken.None);

        // Assert
        var manifest = _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        manifest.LocalWorkload.Should().NotBeNull();
        manifest.LocalWorkload!.ImageId.Should().Be(LocalWorkloadFactory.ImageId);
        manifest.LocalWorkload.RuntimeKitDirectory.Should().NotBe(manifest.WorkloadKitDirectory);
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("--load")),
                Arg.Any<CancellationToken>()
            );
        await _sbx.DidNotReceive()
            .InspectKitAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<IReadOnlyDictionary<string, string>>()
            );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MetadataOnlyExplicitlyInvalidatesSelectionButKeepsPreviousLocalArtifacts(
        bool changeInputs
    )
    {
        // Arrange
        await _service.BuildAsync(Request(), CancellationToken.None);
        var image = _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!.LocalWorkload!;
        var recipePath = Path.Combine(image.RuntimeKitDirectory, "workload.dockerfile");
        var previousRecipe = File.ReadAllText(recipePath);
        if (changeInputs)
        {
            _root.File("teams/sample/sample.workload", "template: example.com/other:2\n");
        }
        _runner.ClearReceivedCalls();
        _sbx.ClearReceivedCalls();

        // Act
        var exitCode = await _service.BuildAsync(
            Request() with
            {
                MetadataOnly = true,
            },
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        _store
            .Load(new SquadBuildIdentity("sample", "ac", "latest"))!
            .LocalWorkload.Should()
            .BeNull();
        _store
            .Load(new SquadBuildIdentity("sample", "ac", "latest"))!
            .Template.Should()
            .Be(changeInputs ? "example.com/other:2" : "example.com/base:1");
        File.ReadAllText(recipePath).Should().Be(previousRecipe);
        File.Exists(Path.Combine(image.RuntimeKitDirectory, "workload.yaml")).Should().BeTrue();
        _console.AllText.Should().Contain("invalidated").And.Contain("not deleted");
        _runner.ReceivedCalls().Should().BeEmpty();
        _sbx.ReceivedCalls().Should().BeEmpty();
    }

    private sealed class LinkUnavailableException(string message) : Exception(message);
}
