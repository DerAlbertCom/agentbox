using Abox.Configuration;
using Abox.Features.Prune;
using Abox.Features.Run;
using Abox.Features.Secrets;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.Launchers;
using Abox.LocalState;
using Abox.Naming;
using Abox.Sandboxes;
using Abox.Sbx;
using Abox.UserSecrets;
using NSubstitute;

namespace Abox.Tests;

public sealed class RunServiceTests
{
    private readonly RunServiceFixture _fixture = new();
    private TempDirectory _root => _fixture.Root;
    private TestConsole _console => _fixture.Console;
    private ISbxClient _sbx => _fixture.Sbx;
    private RunService _service => _fixture.Service;
    private BuildStore _store => _fixture.Store;
    private SandboxRecordStore _records => _fixture.Records;
    private SandboxCreator _create => _fixture.Create;

    [Fact]
    public async Task TheShellRunsInTheWorkspaceOfTheSandbox()
    {
        // Arrange
        var name = SandboxName.For(_root.Path);
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.Now));
        _sbx.GetWorkspaceDirectoryAsync(name, Arg.Any<CancellationToken>())
            .Returns("/home/agent/workspace");

        // Act
        var exitCode = await _service.RunAsync(
            new RunRequest("shell", _root.Path, []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        await _sbx.Received(1)
            .ExecAsync(
                Arg.Is<SbxExecRequest>(request =>
                    request.Command.SequenceEqual(new[] { "bash", "-l" })
                    && request.WorkingDirectory == "/home/agent/workspace"
                    && request.AllocateTty
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task TheShellRunsInTheFolderWhenTheRuntimeDoesNotSayWhereTheWorkspaceIs()
    {
        // Arrange
        var name = SandboxName.For(_root.Path);
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.Now));
        _sbx.GetWorkspaceDirectoryAsync(name, Arg.Any<CancellationToken>()).Returns((string?)null);

        // Act
        await _service.RunAsync(new RunRequest("shell", _root.Path, []), CancellationToken.None);

        // Assert
        await _sbx.Received(1)
            .ExecAsync(
                Arg.Is<SbxExecRequest>(request => request.WorkingDirectory == _root.Path),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task TheShellRunsTheCommandThatIsGiven()
    {
        // Arrange
        var name = SandboxName.For(_root.Path);
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.Now));

        // Act
        await _service.RunAsync(
            new RunRequest("shell", _root.Path, ["ls", "-la"]),
            CancellationToken.None
        );

        // Assert
        await _sbx.Received(1)
            .ExecAsync(
                Arg.Is<SbxExecRequest>(request =>
                    request.Command.SequenceEqual(new[] { "ls", "-la" })
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task WithoutASandboxTheCreateCommandIsPrinted()
    {
        // Arrange: the fixture has no existing sandbox.
        // Act
        var exitCode = await _service.RunAsync(
            new RunRequest("shell", _root.Path, []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(1);
        _console
            .AllText.Should()
            .Contain("abox create")
            .And.Contain("--squad <name>")
            .And.Contain("--team sample")
            .And.Contain("replace <name> placeholders");
        await _sbx.DidNotReceive()
            .ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheCreateHintUsesTheSandboxRecord()
    {
        // Arrange
        _store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "release",
                Reference = ImageName.OfTeam("sample", "release"),
            }
        );
        _records.Save(
            SandboxName.For(_root.Path),
            new SandboxRecord
            {
                Team = "sample",
                Squad = "ac",
                Tag = "release",
            }
        );

        // Act
        var exitCode = await _service.RunAsync(
            new RunRequest("shell", _root.Path, []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(1);
        _console.AllText.Should().Contain("--team sample --squad ac --tag release --local");
    }

    [Fact]
    public async Task ARecordWithoutAKitReferenceDoesNotProveTheSandboxWasLocal()
    {
        // Arrange
        _records.Save(
            SandboxName.For(_root.Path),
            new SandboxRecord { Squad = "ac", Tag = "release" }
        );
        var config = new AboxConfig();
        var service = new RunService(
            new LauncherRegistry([new ShellLauncher(_sbx)]),
            config,
            new SquadResolver(config),
            _store,
            _records,
            _create,
            _sbx,
            SandboxFactory.Resolver(_sbx),
            Substitute.For<IHostPrograms>(),
            _console
        );

        // Act
        var exitCode = await service.RunAsync(
            new RunRequest("shell", _root.Path, []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(1);
        _console
            .AllText.Should()
            .Contain("--team <name> --squad ac --tag release")
            .And.Contain("replace <name> placeholders")
            .And.NotContain("--local")
            .And.NotContain("--root");
    }

    [Fact]
    public async Task TheCreateHintCanUseTheDefaultTeamsOnlySquad()
    {
        // Arrange
        _root.File("teams/sample/ac.abox", "workload: sample\n");

        // Act
        var exitCode = await _service.RunAsync(
            new RunRequest("shell", _root.Path, []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(1);
        _console
            .AllText.Should()
            .Contain("--team sample --squad ac --tag latest")
            .And.NotContain("<name>");
    }

    [Fact]
    public async Task AnOldRecordCanRecoverTheTeamFromItsBuildMetadata()
    {
        // Arrange
        _records.Save(
            SandboxName.For(_root.Path),
            new SandboxRecord { Squad = "ac", Tag = "release" }
        );
        _root.File(
            "state/builds/ac/release/build.json",
            System.Text.Json.JsonSerializer.Serialize(
                new BuildManifest
                {
                    Squad = "ac",
                    Tag = "release",
                    RootDirectory = _root.Path,
                    Reference = ImageName.OfTeam("other-team", "release"),
                }
            )
        );

        // Act
        var exitCode = await _service.RunAsync(
            new RunRequest("shell", _root.Path, []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(1);
        _console.AllText.Should().Contain("--team other-team --squad ac --tag release --local");
        _console.AllText.Should().Contain($"--root \"{_root.Path}\"");
    }

    [Fact]
    public async Task MissingLauncherProgramsPrintThePublishedSquadInTheCreateHint()
    {
        // Arrange
        var name = SandboxName.For(_root.Path);
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.Now));
        _sbx.HasProgramAsync(name, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _records.Save(
            name,
            new SandboxRecord
            {
                Team = "other-team",
                Squad = "ac",
                Tag = "release",
                KitReference = "published-set",
            }
        );

        // Act
        var exitCode = await _service.RunAsync(
            new RunRequest("herdr-app", _root.Path, []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(1);
        _console
            .AllText.Should()
            .Contain("--team other-team --squad ac --tag release")
            .And.NotContain("--local");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownTeamAndSquadNamesAreClearlyMarkedInTheCreateHint(bool emptyRecord)
    {
        // Arrange
        if (emptyRecord)
        {
            _records.Save(SandboxName.For(_root.Path), new SandboxRecord());
        }

        var service = new RunService(
            new LauncherRegistry([new ShellLauncher(_sbx)]),
            new AboxConfig(),
            new SquadResolver(new AboxConfig()),
            _store,
            _records,
            _create,
            _sbx,
            SandboxFactory.Resolver(_sbx),
            Substitute.For<IHostPrograms>(),
            _console
        );

        // Act
        var exitCode = await service.RunAsync(
            new RunRequest("shell", _root.Path, []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(1);
        _console
            .AllText.Should()
            .Contain("--team <name> --squad <name>")
            .And.Contain("replace <name> placeholders");
    }

    [Fact]
    public async Task ALauncherThatIsNotKnownIsAnError()
    {
        // Arrange: the fixture registers only shell and herdr-app launchers.
        // Act
        var action = () =>
            _service.RunAsync(new RunRequest("nope", _root.Path, []), CancellationToken.None);

        // Assert
        var exception = await action.Should().ThrowAsync<AboxException>();

        exception.Which.Message.Should().Contain("no launcher 'nope'");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ANewerBuildIsOfferedAndReplacesTheSandbox(bool separateSources)
    {
        // Arrange
        using var sources = new TempDirectory();
        var sourceRoot = separateSources ? sources : _root;
        var name = SandboxName.For(_root.Path);
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.Now));
        _sbx.RemoveSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, string.Empty));
        _sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, string.Empty));
        _sbx.ListMcpServersAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<string>)[]);
        sourceRoot.File("teams/sample/ac.abox", "workload: sample\n");
        _records.Save(
            name,
            new SandboxRecord
            {
                Team = "sample",
                Squad = "ac",
                Tag = "latest",
                BuildCreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            }
        );
        _store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                RootDirectory = sourceRoot.Path,
                CreatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
                WorkloadKitDirectory = "/kits/workload",
                Reference = ImageName.OfTeam("sample", "latest"),
                LocalWorkload = LocalWorkloadFactory.Artifact(_root),
            }
        );

        // Act
        var exitCode = await _service.RunAsync(
            new RunRequest("shell", _root.Path, []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        _console.AllText.Should().Contain("newer build");
        await _sbx.Received(1).RemoveSandboxAsync(name, Arg.Any<CancellationToken>());
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
    public async Task APulledSandboxIsNotReplacedByANewerLocalBuild()
    {
        // Arrange
        var name = SandboxName.For(_root.Path);
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.Now));
        _records.Save(
            name,
            new SandboxRecord
            {
                Team = "sample",
                Squad = "ac",
                Tag = "latest",
                BuildCreatedAt = DateTimeOffset.MinValue,
                KitReference =
                    "rwest.jfrog.io/team-docker-release/agentic-box-rwest-sample/ac:latest",
            }
        );
        _store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                CreatedAt = DateTimeOffset.Now,
                Reference = ImageName.OfTeam("sample", "latest"),
            }
        );

        // Act
        var exitCode = await _service.RunAsync(
            new RunRequest("shell", _root.Path, []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        _console.AllText.Should().NotContain("newer build");
        await _sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NewerMetadataOnlyPreparationDoesNotInterruptAnExistingLocalSandbox()
    {
        // Arrange
        var name = SandboxName.For(_root.Path);
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.Now));
        _records.Save(
            name,
            new SandboxRecord
            {
                Team = "sample",
                Squad = "ac",
                Tag = "latest",
                BuildCreatedAt = DateTimeOffset.MinValue,
            }
        );
        _store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                Reference = ImageName.OfTeam("sample", "latest"),
                CreatedAt = DateTimeOffset.Now,
                LocalWorkload = null,
            }
        );

        // Act
        var exitCode = await _service.RunAsync(
            new RunRequest("shell", _root.Path, []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        _console.AllText.Should().NotContain("newer build");
        await _sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sbx.Received(1).ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ANewerBuildIsNotOfferedWhenTheAnswerIsNo()
    {
        // Arrange
        var name = SandboxName.For(_root.Path);
        _console.Answer = false;
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.Now));
        _root.File("teams/sample/ac.abox", "workload: sample\n");
        _records.Save(
            name,
            new SandboxRecord
            {
                Team = "sample",
                Squad = "ac",
                Tag = "latest",
                BuildCreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            }
        );
        _store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                CreatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
                WorkloadKitDirectory = "/kits/workload",
                Reference = ImageName.OfTeam("sample", "latest"),
                LocalWorkload = LocalWorkloadFactory.Artifact(_root),
            }
        );

        // Act
        await _service.RunAsync(new RunRequest("shell", _root.Path, []), CancellationToken.None);

        // Assert
        await _sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
