using Abox.Configuration;
using Abox.Features.Publish;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Naming;
using NSubstitute;

namespace Abox.Tests;

public sealed class PublishCommandTests
{
    private readonly TempDirectory _root = new();
    private readonly TestConsole _console = new();
    private readonly IHostPrograms _programs = Substitute.For<IHostPrograms>();
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();
    private readonly BuildStore _store;

    public PublishCommandTests()
    {
        _store = new BuildStore(AboxPaths.For(_root.Path, Path.Combine(_root.Path, "state")));
        _programs.Exists(Arg.Any<string>()).Returns(true);
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => PublishTestProcess.Run(call.Arg<ProcessRequest>()));
    }

    [Fact]
    public async Task TeamAndTagDefaultToTheUserConfiguration()
    {
        // Arrange
        SaveBuild("sample", "ac", "release");
        var command = Command(new AboxConfig { DefaultTeam = "sample", DefaultTag = "release" });

        // Act
        var exitCode = await command.Parse(["--root", _root.Path]).InvokeAsync();

        // Assert
        exitCode.Should().Be(0);
        _console.AllText.Should().Contain("agentic-box-rwest-sample/ac:release");
    }

    [Fact]
    public async Task TeamsCanBeRepeatedAndBuilderAndTagArePassedThrough()
    {
        // Arrange
        SaveBuild("one", "ac", "test");
        SaveBuild("two", "bd", "test");

        // Act
        var exitCode = await Command(new AboxConfig())
            .Parse([
                "--team",
                "one",
                "--team",
                "two",
                "--root",
                _root.Path,
                "--tag",
                "test",
                "--builder",
                "publisher",
            ])
            .InvokeAsync();

        // Assert
        exitCode.Should().Be(0);
        _console
            .AllText.Should()
            .Contain("agentic-box-rwest-one/ac:test")
            .And.Contain("agentic-box-rwest-two/bd:test");
        await _runner
            .Received(6)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.Contains("--builder")
                    && request.Arguments.Contains("publisher")
                    && request.Arguments.Contains("--push")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PublishingUsesTheRepositoryOfTheBuiltInheritedWorkload()
    {
        // Arrange
        _root.File("teams/sample/cape.abox", "workload: shared\n");
        _root.File("teams/sample/ac.abox", "base: cape\n");
        _root.File("kits/shared.workload", "publishRepository: shared-images-local\n");
        var config = new AboxConfig { DefaultTeam = "sample" };
        var resolver = new SquadResolver(config);
        var build = TestServiceGraphs.Build(
            resolver,
            new PersonalConfigurations(AboxPaths.For(_root.Path, _root.Path)),
            _store,
            LocalWorkloadFactory.Images(),
            Substitute.For<Abox.Sbx.ISbxClient>(),
            _console
        );
        await build.BuildAsync(
            new Abox.Features.Build.BuildRequest(
                [resolver.ResolveTeam(_root.Path, "sample")],
                _root.Path,
                "latest",
                Verify: false,
                IncludePersonalConfigurations: false
            ),
            CancellationToken.None
        );

        // Act
        var exitCode = await Command(config).Parse(["--root", _root.Path]).InvokeAsync();

        // Assert
        exitCode.Should().Be(0);
        _console
            .AllText.Should()
            .Contain("rwest.jfrog.io/shared-images-local/agentic-box-rwest-sample/ac:latest");
        await _runner
            .DidNotReceive()
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.Any(argument =>
                        argument.Contains("mtiae-developer-container-images-local")
                    )
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task NoTeamIsAnAboxErrorNotAnUnhandledException()
    {
        // Arrange: the fixture supplies a command with no configured default team.
        // Act
        var exitCode = await Command(new AboxConfig()).Parse([]).InvokeAsync();

        // Assert
        exitCode.Should().Be(1);
        _console.AllText.Should().Contain("No team is given");
        await _runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishingDoesNotRequireSbx()
    {
        // Arrange
        SaveBuild("sample", "ac", "latest");
        _programs.Exists("sbx").Returns(false);

        // Act
        var exitCode = await Command(new AboxConfig { DefaultTeam = "sample" })
            .Parse(["--root", _root.Path])
            .InvokeAsync();

        // Assert
        exitCode.Should().Be(0);
        await _runner.Received().RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailedPublishingReturnsOneWithTheDiagnostic()
    {
        // Arrange
        SaveBuild("sample", "ac", "latest");
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(1, string.Empty, "buildx is unavailable"));

        // Act
        var exitCode = await Command(new AboxConfig { DefaultTeam = "sample" })
            .Parse(["--root", _root.Path])
            .InvokeAsync();

        // Assert
        exitCode.Should().Be(1);
        _console.AllText.Should().Contain("buildx is unavailable");
    }

    private PublishCommand Command(AboxConfig config) =>
        new(
            new Lazy<AboxConfig>(() => config),
            new Lazy<SquadResolver>(() => new SquadResolver(config)),
            new PublishService(_store, _programs, new OciRegistry(_runner), _console),
            _console
        );

    [Fact]
    public async Task EverySelectedTeamIsValidatedBeforeTheFirstPush()
    {
        // Arrange
        SaveBuild("one", "ac", "latest");
        _root.File("teams/two/bd.abox", "workload: sample\n");

        // Act
        var exitCode = await Command(new AboxConfig())
            .Parse(["--team", "one", "--team", "two", "--root", _root.Path])
            .InvokeAsync();

        // Assert
        exitCode.Should().Be(1);
        _console.AllText.Should().Contain("abox build --team two");
        await _runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    private void SaveBuild(string team, string squad, string tag)
    {
        _root.File($"teams/{team}/{squad}.abox", "workload: sample\n");
        var workload = Path.Combine(
            _store.DirectoryFor(new SquadBuildIdentity(team, squad, tag)),
            "workload"
        );
        var manifest = new BuildManifest
        {
            Squad = squad,
            Tag = tag,
            Reference = $"agentic-box-rwest-{team}:{tag}",
            PublishRepository = "mtiae-developer-container-images-local",
            WorkloadKitDirectory = workload,
            IncludesPersonalConfigurations = false,
        };
        PublicationBuilds.SaveWorkload(
            _store,
            manifest,
            Path.Combine(workload, "workload.yaml"),
            "schemaVersion: \"3\"\nkind: workload\n"
        );
    }
}
