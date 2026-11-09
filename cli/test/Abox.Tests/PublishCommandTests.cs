using Abox.Build;
using Abox.Configuration;
using Abox.Features.Publish;
using Abox.Infrastructure;
using Abox.Kits;
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
        SaveBuild("sample", "ac", "release");
        var command = Command(new AboxConfig { DefaultTeam = "sample", DefaultTag = "release" });

        var exitCode = await command.Parse(["--root", _root.Path]).InvokeAsync();

        exitCode.Should().Be(0);
        _console.AllText.Should().Contain("agentbox-rwest-sample/ac:release");
    }

    [Fact]
    public async Task TeamsCanBeRepeatedAndBuilderAndTagArePassedThrough()
    {
        SaveBuild("one", "ac", "test");
        SaveBuild("two", "bd", "test");

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

        exitCode.Should().Be(0);
        _console
            .AllText.Should()
            .Contain("agentbox-rwest-one/ac:test")
            .And.Contain("agentbox-rwest-two/bd:test");
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
        _root.File("teams/sample/cape.abox", "workload: shared\n");
        _root.File("teams/sample/ac.abox", "base: cape\n");
        _root.File("kits/shared.workload", "publishRepository: shared-images-local\n");
        var config = new AboxConfig { DefaultTeam = "sample" };
        var resolver = new SquadResolver(config);
        var build = new Abox.Features.Build.BuildService(
            resolver,
            new WorkloadKitGenerator(),
            new StateMixinGenerator(),
            new PersonalConfigurations(AboxPaths.For(_root.Path, _root.Path)),
            _store,
            LocalWorkloadTest.Images(),
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

        var exitCode = await Command(config).Parse(["--root", _root.Path]).InvokeAsync();

        exitCode.Should().Be(0);
        _console
            .AllText.Should()
            .Contain("rwest.jfrog.io/shared-images-local/agentbox-rwest-sample/ac:latest");
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
        var exitCode = await Command(new AboxConfig()).Parse([]).InvokeAsync();

        exitCode.Should().Be(1);
        _console.AllText.Should().Contain("No team is given");
        await _runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishingDoesNotRequireSbx()
    {
        SaveBuild("sample", "ac", "latest");
        _programs.Exists("sbx").Returns(false);

        var exitCode = await Command(new AboxConfig { DefaultTeam = "sample" })
            .Parse(["--root", _root.Path])
            .InvokeAsync();

        exitCode.Should().Be(0);
        await _runner.Received().RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailedPublishingReturnsOneWithTheDiagnostic()
    {
        SaveBuild("sample", "ac", "latest");
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(1, string.Empty, "buildx is unavailable"));

        var exitCode = await Command(new AboxConfig { DefaultTeam = "sample" })
            .Parse(["--root", _root.Path])
            .InvokeAsync();

        exitCode.Should().Be(1);
        _console.AllText.Should().Contain("buildx is unavailable");
    }

    private PublishCommand Command(AboxConfig config) =>
        new(
            config,
            new SquadResolver(config),
            new PublishService(_store, _programs, _runner, _console),
            _console
        );

    [Fact]
    public async Task EverySelectedTeamIsValidatedBeforeTheFirstPush()
    {
        SaveBuild("one", "ac", "latest");
        _root.File("teams/two/bd.abox", "workload: sample\n");

        var exitCode = await Command(new AboxConfig())
            .Parse(["--team", "one", "--team", "two", "--root", _root.Path])
            .InvokeAsync();

        exitCode.Should().Be(1);
        _console.AllText.Should().Contain("abox build --team two");
        await _runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    private void SaveBuild(string team, string squad, string tag)
    {
        _root.File($"teams/{team}/{squad}.abox", "workload: sample\n");
        var workload = Path.Combine(_store.DirectoryFor(squad, tag), "workload");
        Directory.CreateDirectory(workload);
        File.WriteAllText(
            Path.Combine(workload, "workload.yaml"),
            "schemaVersion: \"3\"\nkind: workload\n"
        );
        var manifest = new BuildManifest
        {
            Squad = squad,
            Tag = tag,
            Reference = $"agentbox-rwest-{team}:{tag}",
            PublishRepository = "mtiae-developer-container-images-local",
            WorkloadKitDirectory = workload,
            IncludesPersonalConfigurations = false,
        };
        manifest.SourceHashes = BuildSources.Capture(manifest);
        _store.Save(manifest);
    }
}
