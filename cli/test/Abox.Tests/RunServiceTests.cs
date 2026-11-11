using Abox.Configuration;
using Abox.Features.Prune;
using Abox.Features.Run;
using Abox.Features.Secrets;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.Launchers;
using Abox.LocalState;
using Abox.Naming;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class RunServiceTests
{
    private readonly TempDirectory _root = new();
    private readonly TestConsole _console = new();
    private readonly ISbxClient _sbx = Substitute.For<ISbxClient>();
    private readonly RunService _service;
    private readonly AboxPaths _paths;
    private readonly BuildStore _store;
    private readonly SandboxRecordStore _records;
    private readonly SandboxCreator _create;
    private readonly AboxConfig _config = new() { DefaultTeam = "sample" };

    public RunServiceTests()
    {
        var launchers = new LauncherRegistry([
            new ShellLauncher(),
            new HerdrAppLauncher(Substitute.For<IProcessRunner>()),
        ]);
        var hostPrograms = Substitute.For<IHostPrograms>();
        hostPrograms.Exists(Arg.Any<string>()).Returns(true);
        _paths = AboxPaths.For(
            Path.Combine(_root.Path, "config"),
            Path.Combine(_root.Path, "state")
        );
        _store = new BuildStore(_paths);
        _records = new SandboxRecordStore(_paths);
        _create = new SandboxCreator(
            _records,
            _paths,
            _sbx,
            _console,
            new LocalSquadSource(_store, LocalWorkloadTest.Images(), _console),
            new RemoteSquadSource(_config, _sbx, PublishedSquadTest.Source())
        );
        _service = new RunService(
            launchers,
            _config,
            new SquadResolver(_config),
            _store,
            _records,
            _create,
            _sbx,
            hostPrograms,
            _console
        );

        _sbx.ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>()).Returns(0);
        _sbx.HasProgramAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    [Fact]
    public async Task TheShellRunsInTheWorkspaceOfTheSandbox()
    {
        var name = SandboxName.For(_root.Path);
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.Now));
        _sbx.GetWorkspaceDirectoryAsync(name, Arg.Any<CancellationToken>())
            .Returns("/home/agent/workspace");

        var exitCode = await _service.RunAsync(
            new RunRequest("shell", _root.Path, []),
            CancellationToken.None
        );

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
        var name = SandboxName.For(_root.Path);
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.Now));
        _sbx.GetWorkspaceDirectoryAsync(name, Arg.Any<CancellationToken>()).Returns((string?)null);

        await _service.RunAsync(new RunRequest("shell", _root.Path, []), CancellationToken.None);

        await _sbx.Received(1)
            .ExecAsync(
                Arg.Is<SbxExecRequest>(request => request.WorkingDirectory == _root.Path),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task TheShellRunsTheCommandThatIsGiven()
    {
        var name = SandboxName.For(_root.Path);
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.Now));

        await _service.RunAsync(
            new RunRequest("shell", _root.Path, ["ls", "-la"]),
            CancellationToken.None
        );

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
        var exitCode = await _service.RunAsync(
            new RunRequest("shell", _root.Path, []),
            CancellationToken.None
        );

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
        _records.Save(
            SandboxName.For(_root.Path),
            new SandboxRecord
            {
                Team = "sample",
                Squad = "ac",
                Tag = "release",
            }
        );

        (await _service.RunAsync(new RunRequest("shell", _root.Path, []), CancellationToken.None))
            .Should()
            .Be(1);

        _console.AllText.Should().Contain("--team sample --squad ac --tag release --local");
    }

    [Fact]
    public async Task TheCreateHintCanUseTheDefaultTeamsOnlySquad()
    {
        _root.File("teams/sample/ac.abox", "workload: sample\n");

        (await _service.RunAsync(new RunRequest("shell", _root.Path, []), CancellationToken.None))
            .Should()
            .Be(1);

        _console
            .AllText.Should()
            .Contain("--team sample --squad ac --tag latest")
            .And.NotContain("<name>");
    }

    [Fact]
    public async Task AnOldRecordCanRecoverTheTeamFromItsBuildMetadata()
    {
        _records.Save(
            SandboxName.For(_root.Path),
            new SandboxRecord { Squad = "ac", Tag = "release" }
        );
        _store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "release",
                RootDirectory = _root.Path,
                Reference = ImageName.OfTeam("other-team", "release"),
            }
        );

        (await _service.RunAsync(new RunRequest("shell", _root.Path, []), CancellationToken.None))
            .Should()
            .Be(1);

        _console.AllText.Should().Contain("--team other-team --squad ac --tag release --local");
        _console.AllText.Should().Contain($"--root \"{_root.Path}\"");
    }

    [Fact]
    public async Task MissingLauncherProgramsPrintThePublishedSquadInTheCreateHint()
    {
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

        (
            await _service.RunAsync(
                new RunRequest("herdr-app", _root.Path, []),
                CancellationToken.None
            )
        )
            .Should()
            .Be(1);

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
        if (emptyRecord)
        {
            _records.Save(SandboxName.For(_root.Path), new SandboxRecord());
        }

        var service = new RunService(
            new LauncherRegistry([new ShellLauncher()]),
            new AboxConfig(),
            new SquadResolver(new AboxConfig()),
            _store,
            _records,
            _create,
            _sbx,
            Substitute.For<IHostPrograms>(),
            _console
        );

        (await service.RunAsync(new RunRequest("shell", _root.Path, []), CancellationToken.None))
            .Should()
            .Be(1);

        _console
            .AllText.Should()
            .Contain("--team <name> --squad <name>")
            .And.Contain("replace <name> placeholders");
    }

    [Fact]
    public async Task ALauncherThatIsNotKnownIsAnError()
    {
        var action = () =>
            _service.RunAsync(new RunRequest("nope", _root.Path, []), CancellationToken.None);

        var exception = await action.Should().ThrowAsync<AboxException>();

        exception.Which.Message.Should().Contain("no launcher 'nope'");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ANewerBuildIsOfferedAndReplacesTheSandbox(bool separateSources)
    {
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
                LocalWorkload = LocalWorkloadTest.Artifact(_root),
            }
        );

        var exitCode = await _service.RunAsync(
            new RunRequest("shell", _root.Path, []),
            CancellationToken.None
        );

        exitCode.Should().Be(0);
        _console.AllText.Should().Contain("newer build");
        await _sbx.Received(1).RemoveSandboxAsync(name, Arg.Any<CancellationToken>());
        await _sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request =>
                    request.KitSource == Path.Combine(_root.Path, "runtime", "workload")
                    && request.PullPolicy == "never"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task APulledSandboxIsNotReplacedByANewerLocalBuild()
    {
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
                KitReference = "rwest.jfrog.io/team-docker-release/agentbox-rwest-sample/ac:latest",
            }
        );
        _store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                CreatedAt = DateTimeOffset.Now,
            }
        );

        var exitCode = await _service.RunAsync(
            new RunRequest("shell", _root.Path, []),
            CancellationToken.None
        );

        exitCode.Should().Be(0);
        _console.AllText.Should().NotContain("newer build");
        await _sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NewerMetadataOnlyPreparationDoesNotInterruptAnExistingLocalSandbox()
    {
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

        var exitCode = await _service.RunAsync(
            new RunRequest("shell", _root.Path, []),
            CancellationToken.None
        );

        exitCode.Should().Be(0);
        _console.AllText.Should().NotContain("newer build");
        await _sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sbx.Received(1).ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ANewerBuildIsNotOfferedWhenTheAnswerIsNo()
    {
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
                LocalWorkload = LocalWorkloadTest.Artifact(_root),
            }
        );

        await _service.RunAsync(new RunRequest("shell", _root.Path, []), CancellationToken.None);

        await _sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
