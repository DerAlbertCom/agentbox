using Abox.Build;
using Abox.Configuration;
using Abox.Features.Create;
using Abox.Health;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

[Collection("Create command working directory")]
public sealed class CreateCommandTests
{
    private readonly TempDirectory _root = new();
    private readonly TestConsole _console = new();
    private readonly ISbxClient _sbx = Substitute.For<ISbxClient>();

    [Theory]
    [InlineData("", "mtiae-docker-release")]
    [InlineData("pullRepository: team-docker-release", "team-docker-release")]
    public async Task CreationPullsTheConfiguredRepositoryAndSelectedTag(
        string yaml,
        string repository
    )
    {
        var command = Command(yaml);

        var exitCode = await InvokeAsync(command, ["--tag", "release"]);

        exitCode.Should().Be(0);
        await _sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request =>
                    request.KitSource
                        == $"rwest.jfrog.io/{repository}/agentbox-rwest-sample/ac:release"
                    && request.PullPolicy == "always"
                ),
                Arg.Any<CancellationToken>()
            );
        _console.AllText.Should().NotContain("this version of abox uses");
    }

    [Fact]
    public async Task LocalCreationUsesTheLocalKitsInsteadOfThePullRepository()
    {
        var command = Command("pullRepository: team-docker-release");

        var exitCode = await InvokeAsync(command, ["--local", "--tag", "release"]);

        exitCode.Should().Be(0);
        await _sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request =>
                    request.KitSource == "/kits/workload" && request.PullPolicy == "never"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PullingUsesTheDefaultTagFromSettings()
    {
        var command = Command("defaultTag: release\npullRepository: team-docker-release\n");

        var exitCode = await InvokeAsync(command, []);

        exitCode.Should().Be(0);
        await _sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request =>
                    request.KitSource
                    == "rwest.jfrog.io/team-docker-release/agentbox-rwest-sample/ac:release"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task TheSandboxLocationCanBeOutsideTheTeamSourceProject()
    {
        using var workspace = new TempDirectory();
        var command = Command("");

        var exitCode = await InvokeAsync(
            command,
            ["--tag", "release", "--location", workspace.Path]
        );

        exitCode.Should().Be(0);
        await _sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request => request.Location == workspace.Path),
                Arg.Any<CancellationToken>()
            );
    }

    private async Task<int> InvokeAsync(CreateCommand command, string[] arguments)
    {
        var originalDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_root.Path);
            return await command.Parse(arguments).InvokeAsync();
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }

    private CreateCommand Command(string yaml)
    {
        _root.File("teams/sample/ac.abox", "workload: sample\n");
        _root.File("config/config.yaml", yaml);
        var paths = AboxPaths.For(
            Path.Combine(_root.Path, "config"),
            Path.Combine(_root.Path, "state")
        );
        var loaded = AboxConfigLoader.Load(paths);
        var config = new AboxConfig
        {
            DefaultTeam = "sample",
            DefaultLocation = _root.Path,
            DefaultTag = loaded.DefaultTag,
            PullRepository = loaded.PullRepository,
        };
        var store = new BuildStore(paths);
        store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "release",
                Reference = "agentbox-rwest-sample:release",
                WorkloadKitDirectory = "/kits/workload",
            }
        );
        var programs = Substitute.For<IHostPrograms>();
        programs.Exists("sbx").Returns(true);
        _sbx.GetVersionAsync(Arg.Any<CancellationToken>()).Returns(new Version(0, 51, 0));
        _sbx.DiagnoseAsync(Arg.Any<CancellationToken>()).Returns(new DiagnoseReport([]));
        _sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, string.Empty));
        return new CreateCommand(
            config,
            new Preflight(_console, _sbx, programs),
            new SquadResolver(config),
            new CreateService(store, new SandboxRecordStore(paths), paths, config, _sbx, _console),
            _console
        );
    }
}

[CollectionDefinition("Create command working directory", DisableParallelization = true)]
public sealed class CreateCommandWorkingDirectoryCollection;
