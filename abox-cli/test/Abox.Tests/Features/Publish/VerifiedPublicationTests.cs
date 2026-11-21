using Abox.Configuration;
using Abox.Features.Publish;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Naming;
using NSubstitute;

namespace Abox.Tests;

public sealed class VerifiedPublicationTests
{
    private const string FirstPlatform =
        "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string SecondPlatform =
        "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    private const string TwoPlatforms = """
        {"schemaVersion":2,"manifests":[
          {"digest":"sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","platform":{"os":"linux","architecture":"amd64"}},
          {"digest":"sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc","platform":{"os":"windows","architecture":"arm64"}}
        ]}
        """;

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"containerimage.digest\":null}")]
    [InlineData("{\"containerimage.digest\":42}")]
    [InlineData("{\"containerimage.digest\":\"sha256:bad\"}")]
    public async Task InvalidPushMetadataStopsBeforeInspectionOrPromotionAndRemovesSnapshots(
        string? metadata
    )
    {
        // Arrange
        using var fixture = new PublicationFixture();
        fixture.SaveBuild("ac");
        fixture
            .Runner.RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("--push")),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var args = call.Arg<ProcessRequest>().Arguments.ToList();
                if (metadata is not null)
                    File.WriteAllText(args[args.IndexOf("--metadata-file") + 1], metadata);
                return new ProcessResult(0, "", "");
            });

        // Act
        var action = () => fixture.PublishAsync("ac");

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*pushed digest*not promoted*"
        );
        fixture.Calls.Should().HaveCount(2); // Buildx availability, then the first push only.
        Directory.Exists(fixture.Calls.Last().Arguments.Last()).Should().BeFalse();
        fixture.Console.AllText.Should().NotContain("Published squad");
    }

    [Fact]
    public async Task FailedConfirmationDoesNotReportSuccessOrAttemptRollbackOfAPromotedTag()
    {
        // Arrange
        using var fixture = new PublicationFixture();
        fixture.SaveBuild("ac");
        fixture
            .Runner.RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.Contains("--raw")
                    && request.Arguments.Last().EndsWith(":latest")
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ProcessResult(0, "{}", ""));

        // Act
        var action = () => fixture.PublishAsync("ac");

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*valid v3 workload descriptor*"
        );
        fixture.Calls.Count(call => call.Arguments.Contains("create")).Should().Be(1);
        fixture.Calls.Last().Arguments.Last().Should().EndWith("/ac:latest");
        fixture.Console.AllText.Should().NotContain("Published squad");
    }

    [Fact]
    public async Task VerifiedImmutableCandidateIsPromotedBeforeTheRequestedTagIsConfirmed()
    {
        // Arrange
        using var fixture = new PublicationFixture();
        fixture.SaveBuild("ac");

        // Act
        await fixture.PublishAsync("ac");

        // Assert
        var promotion = fixture.Calls.FindIndex(call => call.Arguments.Contains("create"));
        var candidate = fixture.Calls[promotion - 1].Arguments.Last();
        candidate.Should().MatchRegex(@"/ac@sha256:[a-f0-9]{64}$");
        fixture
            .Calls.Skip(promotion - 1)
            .Select(call => call.Arguments.Last())
            .Should()
            .Equal(
                candidate,
                candidate,
                "rwest.jfrog.io/test-images-local/agentic-box-rwest-sample/ac:latest"
            );
        fixture.Calls[promotion - 1].Arguments.Should().Contain("--raw");
        fixture.Calls[promotion + 1].Arguments.Should().Contain("--raw");
    }

    [Fact]
    public async Task LaterSquadUsesItsEntrySnapshotEvenWhenItsSourceChangesAtTheFirstRegistryWrite()
    {
        // Arrange
        using var fixture = new PublicationFixture();
        fixture.SaveBuild("ac");
        var laterSource = fixture.SaveBuild("bd");
        var original = File.ReadAllText(laterSource);
        var publishedWorkloads = new List<string>();
        fixture
            .Runner.RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.Contains("--push")
                    && request.Arguments.Any(arg => arg.Contains("/components/workload:"))
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var request = call.Arg<ProcessRequest>();
                publishedWorkloads.Add(
                    File.ReadAllText(Path.Combine(request.Arguments.Last(), "workload.yaml"))
                );
                File.AppendAllText(laterSource, "description: changed after publication started\n");
                return PublishTestProcess.Run(request);
            });

        // Act
        await fixture.PublishAsync("ac", "bd");

        // Assert
        publishedWorkloads.Should().Equal(original, original);
        File.ReadAllText(laterSource).Should().NotBe(original);
        fixture
            .Calls.Where(call => call.Arguments.Contains("--push"))
            .Should()
            .OnlyContain(call => !Directory.Exists(call.Arguments.Last()));
    }

    [Fact]
    public async Task EveryRunnablePlatformMustRetainItsDescriptorIncludingNonHostPlatforms()
    {
        // Arrange
        using var fixture = new PublicationFixture();
        fixture.SaveBuild("ac");
        fixture.WorkloadInspection(reference =>
            reference.EndsWith(FirstPlatform) ? PublishTestProcess.Manifest("workload")
            : reference.EndsWith(SecondPlatform) ? "{\"schemaVersion\":2}"
            : TwoPlatforms
        );

        // Act
        var action = () => fixture.PublishAsync("ac");

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*valid v3 workload descriptor*"
        );
        fixture.Calls.Should().Contain(call => call.Arguments.Last().EndsWith(SecondPlatform));
        fixture.Calls.Should().NotContain(call => call.Arguments.Contains("create"));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"manifests\":[]}", "*No runnable platform manifests*", 1)]
    [InlineData(
        "{\"schemaVersion\":2,\"manifests\":[{\"annotations\":{\"vnd.docker.reference.type\":\"attestation-manifest\"},\"platform\":{\"os\":\"unknown\",\"architecture\":\"unknown\"}}]}",
        "*No runnable platform manifests*",
        1
    )]
    [InlineData(
        "{\"schemaVersion\":2,\"manifests\":[{\"digest\":\"sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"}]}",
        "*too many nested image indexes*",
        5
    )]
    public async Task IndexesWithoutBoundedRunnableLeavesStopBeforePromotion(
        string index,
        string error,
        int inspections
    )
    {
        // Arrange
        using var fixture = new PublicationFixture();
        fixture.SaveBuild("ac");
        fixture.WorkloadInspection(_ => index);

        // Act
        var action = () => fixture.PublishAsync("ac");

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(error);
        fixture.Calls.Count(call => call.Arguments.Contains("--raw")).Should().Be(inspections);
        fixture.Calls.Should().NotContain(call => call.Arguments.Contains("create"));
    }

    [Fact]
    public async Task SuppliedRegistryArgumentsAreValidatedOnEveryPlatformBeforeTheFirstWrite()
    {
        // Arrange
        using var fixture = new PublicationFixture();
        fixture.SaveBuild("ac");
        fixture.AddRegistryKit("ac");
        fixture
            .Runner.RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("--raw")),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => new ProcessResult(
                0,
                call.Arg<ProcessRequest>().Arguments.Last().EndsWith(FirstPlatform)
                        ? PublishTestProcess.Manifest(
                            "mixin",
                            new { message = new { env = "MESSAGE" } }
                        )
                    : call.Arg<ProcessRequest>().Arguments.Last().EndsWith(SecondPlatform)
                        ? PublishTestProcess.Manifest(
                            "mixin",
                            new { message = new { buildSecret = "MESSAGE" } }
                        )
                    : TwoPlatforms,
                ""
            ));

        // Act
        var action = () => fixture.PublishAsync("ac");

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*Registry kit argument 'message'*build-phase*"
        );
        fixture.Calls.Should().NotContain(call => call.Arguments.Contains("--push"));
    }

    [Fact]
    public async Task LaterSquadFailureDoesNotRollbackOrHideTheEarlierSuccessfulPromotion()
    {
        // Arrange
        using var fixture = new PublicationFixture();
        fixture.SaveBuild("ac");
        fixture.SaveBuild("bd");
        fixture
            .Runner.RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.Contains("--push")
                    && request.Arguments.Any(arg => arg.Contains("/bd/components/"))
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ProcessResult(1, "", "second squad push failed"));

        // Act
        var action = () => fixture.PublishAsync("ac", "bd");

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*second squad push failed*"
        );
        fixture
            .Calls.Where(call => call.Arguments.Contains("create"))
            .Should()
            .ContainSingle()
            .Which.Arguments.Should()
            .Contain("rwest.jfrog.io/test-images-local/agentic-box-rwest-sample/ac:latest");
        fixture
            .Console.AllText.Should()
            .Contain("Published squad 'ac'")
            .And.NotContain("Published squad 'bd'");
    }

    private sealed class PublicationFixture : IDisposable
    {
        private readonly TempDirectory _root = new();
        private readonly BuildStore _store;
        private readonly PublishService _service;
        public IProcessRunner Runner { get; } = Substitute.For<IProcessRunner>();
        public TestConsole Console { get; } = new();
        public List<ProcessRequest> Calls { get; } = [];

        public PublicationFixture()
        {
            _store = new BuildStore(AboxPaths.For(_root.Path, Path.Combine(_root.Path, "state")));
            var programs = Substitute.For<IHostPrograms>();
            programs.Exists("docker").Returns(true);
            _service = new PublishService(_store, programs, new OciRegistry(Runner), Console);
            Runner
                .When(runner =>
                    runner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
                )
                .Do(call => Calls.Add(call.Arg<ProcessRequest>()));
            Runner
                .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
                .Returns(call => PublishTestProcess.Run(call.Arg<ProcessRequest>()));
        }

        public string SaveBuild(string squad)
        {
            var workload = Path.Combine(_root.Path, $"{squad}/workload.yaml");
            var build = new BuildManifest
            {
                Squad = squad,
                Tag = "latest",
                Reference = "agentic-box-rwest-sample:latest",
                PublishRepository = "test-images-local",
                WorkloadKitDirectory = Path.GetDirectoryName(workload)!,
                IncludesPersonalConfigurations = false,
            };
            return PublicationBuilds.SaveWorkload(
                _store,
                build,
                workload,
                "# syntax=docker/sandbox-kit:3\nschemaVersion: \"3\"\nkind: workload\n"
            );
        }

        public Task<int> PublishAsync(params string[] squads) =>
            _service.PublishAsync(
                new PublishRequest(
                    [new SquadResolver.ResolvedTeam("sample", _root.Path, squads)],
                    "latest"
                ),
                CancellationToken.None
            );

        public void AddRegistryKit(string squad)
        {
            var build = _store.Load(new SquadBuildIdentity("sample", squad, "latest"))!;
            build.Kits.Add(
                new ManifestKit
                {
                    Name = "remote",
                    Reference = "example.com/remote:1",
                    Arguments = new() { ["message"] = "hello" },
                }
            );
            PublicationBuilds.SaveVerified(_store, build);
        }

        public void WorkloadInspection(Func<string, string> response) =>
            Runner
                .RunAsync(
                    Arg.Is<ProcessRequest>(request =>
                        request.Arguments.Contains("--raw")
                        && request.Arguments.Last().Contains("/components/workload")
                    ),
                    Arg.Any<CancellationToken>()
                )
                .Returns(call => new ProcessResult(
                    0,
                    response(call.Arg<ProcessRequest>().Arguments.Last()),
                    ""
                ));

        public void Dispose() => _root.Dispose();
    }
}
