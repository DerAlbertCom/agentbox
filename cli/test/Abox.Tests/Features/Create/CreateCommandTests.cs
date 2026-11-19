using Abox.Configuration;
using Abox.Features.Create;
using Abox.Health;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Sandboxes;
using Abox.Sbx;
using Abox.UserSecrets;
using NSubstitute;

namespace Abox.Tests;

[Collection("Create command working directory")]
public sealed class CreateCommandTests
{
    private readonly TempDirectory _root = new();
    private readonly TestConsole _console = new();
    private readonly ISbxClient _sbx = Substitute.For<ISbxClient>();

    [Fact]
    public async Task RemoteCreateFromAnArbitraryDirectoryDoesNotNeedTeamsOrABuild()
    {
        // Arrange
        var command = Command("");
        Directory.Delete(Path.Combine(_root.Path, "teams"), recursive: true);
        Directory.Delete(Path.Combine(_root.Path, "config"), recursive: true);
        Directory.Delete(Path.Combine(_root.Path, "state"), recursive: true);

        // Act
        var exitCode = await InvokeAsync(command, ["--team", "mti-ca", "--squad", "ac"]);

        // Assert
        _console.AllText.Should().NotContain("There is no team 'mti-ca'");
        exitCode.Should().Be(0);
        await _sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request =>
                    request.KitSource.Contains("agentbox-rwest-mti-ca/ac")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task RemoteCreateWithoutSquadRequiresItRatherThanLookingForLocalTeams()
    {
        // Arrange
        var command = Command("");
        Directory.Delete(Path.Combine(_root.Path, "teams"), recursive: true);

        // Act
        var exitCode = await InvokeAsync(command, ["--team", "mti-ca"]);

        // Assert
        exitCode.Should().Be(1);
        _console.AllText.Should().Contain("requires --squad").And.NotContain("There is no team");
        _sbx.ReceivedCalls()
            .Should()
            .BeEmpty("missing selection must fail before preflight as before");
        await _sbx.DidNotReceive()
            .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoteCreateRejectsRootBeforeAnySbxCalls()
    {
        // Arrange
        var command = Command("");
        _sbx.ClearReceivedCalls();

        // Act
        var exitCode = await InvokeAsync(
            command,
            ["--team", "sample", "--squad", "ac", "--root", _root.Path]
        );

        // Assert
        exitCode.Should().Be(1);
        _console.AllText.Should().Contain("--root requires --local");
        _sbx.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task LocalCreateStillRequiresTheLocalTeamSources()
    {
        // Arrange
        var command = Command("");
        Directory.Delete(Path.Combine(_root.Path, "teams"), recursive: true);

        // Act
        var exitCode = await InvokeAsync(command, ["--team", "mti-ca", "--squad", "ac", "--local"]);

        // Assert
        exitCode.Should().Be(1);
        _console.AllText.Should().Contain("There is no team 'mti-ca'");
        await _sbx.DidNotReceive()
            .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "mtiae-docker-release")]
    [InlineData("pullRepository: team-docker-release", "team-docker-release")]
    public async Task CreationPullsTheConfiguredRepositoryAndSelectedTag(
        string yaml,
        string repository
    )
    {
        // Arrange
        var command = Command(yaml);

        // Act
        var exitCode = await InvokeAsync(command, ["--squad", "ac", "--tag", "release"]);

        // Assert
        exitCode.Should().Be(0);
        await _sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request =>
                    request.KitSource
                        == $"rwest.jfrog.io/{repository}/agentbox-rwest-sample/ac@{PublishedSquadFactory.Digest}"
                    && request.PullPolicy == PullPolicy.Always
                ),
                Arg.Any<CancellationToken>()
            );
        _console.AllText.Should().NotContain("this version of abox uses");
    }

    [Fact]
    public async Task LocalCreationUsesTheLocalKitsInsteadOfThePullRepository()
    {
        // Arrange
        var command = Command("pullRepository: team-docker-release");

        // Act
        var exitCode = await InvokeAsync(command, ["--local", "--tag", "release"]);

        // Assert
        exitCode.Should().Be(0);
        await _sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request =>
                    request.KitSource == Path.Combine(_root.Path, "runtime", "workload")
                    && request.PullPolicy == PullPolicy.Never
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task LocalCreateAcceptsSquadNamesInAnotherCase()
    {
        // Arrange
        var command = Command("");

        // Act
        var exitCode = await InvokeAsync(command, ["--local", "--squad", "AC", "--tag", "release"]);

        // Assert
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task LocalCreateFindsTeamsNextToItsLocation()
    {
        // Arrange
        var command = Command("");
        using var project = new TempDirectory();
        project.File("teams/sample/ac.abox", "workload: sample\n");
        Directory.Delete(Path.Combine(_root.Path, "teams"), recursive: true);

        // Act
        var exitCode = await InvokeAsync(
            command,
            ["--local", "--location", project.Path, "--tag", "release"]
        );

        // Assert
        exitCode.Should().Be(0);
        await _sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request => request.Location == project.Path),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task LocalCreateCanResolveTeamsFromAnExplicitRootOutsideTheLocation()
    {
        // Arrange
        var command = Command("");
        using var workspace = new TempDirectory();

        // Act
        var exitCode = await InvokeAsync(
            command,
            [
                "--local",
                "--squad",
                "ac",
                "--tag",
                "release",
                "--location",
                workspace.Path,
                "--root",
                _root.Path,
            ]
        );

        // Assert
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task PullingUsesTheDefaultTagFromSettings()
    {
        // Arrange
        var command = Command("defaultTag: release\npullRepository: team-docker-release\n");

        // Act
        var exitCode = await InvokeAsync(command, ["--squad", "ac"]);

        // Assert
        exitCode.Should().Be(0);
        await _sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request =>
                    request.KitSource
                    == $"rwest.jfrog.io/team-docker-release/agentbox-rwest-sample/ac@{PublishedSquadFactory.Digest}"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task TheSandboxLocationCanBeOutsideTheTeamSourceProject()
    {
        // Arrange
        using var workspace = new TempDirectory();
        var command = Command("");

        // Act
        var exitCode = await InvokeAsync(
            command,
            ["--squad", "ac", "--tag", "release", "--location", workspace.Path]
        );

        // Assert
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
                LocalWorkload = LocalWorkloadFactory.Artifact(_root),
            }
        );
        var programs = ReadyRuntime();
        return new CreateCommand(
            new Lazy<AboxConfig>(() => config),
            new Preflight(_console, _sbx, programs),
            new Lazy<SquadResolver>(() => new SquadResolver(config)),
            new Lazy<SandboxCreator>(() =>
                TestServiceGraphs.Creator(
                    new SandboxRecordStore(paths),
                    paths,
                    _sbx,
                    _console,
                    new LocalSquadSource(store, LocalWorkloadFactory.Images(), _console),
                    new RemoteSquadSource(config, _sbx, PublishedSquadFactory.Source()),
                    new UserSecretsCopy(config, new UserSecretsHost(null, null), _sbx)
                )
            ),
            _console
        );
    }

    private IHostPrograms ReadyRuntime()
    {
        var programs = Substitute.For<IHostPrograms>();
        programs.Exists("sbx").Returns(true);
        _sbx.GetVersionAsync(Arg.Any<CancellationToken>()).Returns(new Version(0, 51, 0));
        _sbx.DiagnoseAsync(Arg.Any<CancellationToken>()).Returns(new DiagnoseReport([]));
        _sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, string.Empty));
        return programs;
    }
}
