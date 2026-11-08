using Abox.Build;
using Abox.Configuration;
using Abox.Features.Create;
using Abox.Features.Prune;
using Abox.Features.Run;
using Abox.Features.Secrets;
using Abox.Infrastructure;
using Abox.Launchers;
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
    private readonly CreateService _create;

    public RunServiceTests()
    {
        var launchers = new LauncherRegistry([new ShellLauncher()]);
        var hostPrograms = Substitute.For<IHostPrograms>();
        hostPrograms.Exists(Arg.Any<string>()).Returns(true);
        _paths = AboxPaths.For(
            Path.Combine(_root.Path, "config"),
            Path.Combine(_root.Path, "state")
        );
        _store = new BuildStore(_paths);
        _records = new SandboxRecordStore(_paths);
        _create = new CreateService(_store, _records, _paths, _sbx, _console);
        _service = new RunService(
            launchers,
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
        _console.AllText.Should().Contain("abox create");
        await _sbx.DidNotReceive()
            .ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ALauncherThatIsNotKnownIsAnError()
    {
        var action = () =>
            _service.RunAsync(new RunRequest("nope", _root.Path, []), CancellationToken.None);

        var exception = await action.Should().ThrowAsync<AboxException>();

        exception.Which.Message.Should().Contain("no launcher 'nope'");
    }

    [Fact]
    public async Task ANewerBuildIsOfferedAndReplacesTheSandbox()
    {
        var name = SandboxName.For(_root.Path);
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.Now));
        _sbx.RemoveSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, string.Empty));
        _sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, string.Empty));
        _sbx.ListMcpServersAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<string>)[]);
        _records.Save(
            name,
            new SandboxRecord
            {
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
            }
        );

        var exitCode = await _service.RunAsync(
            new RunRequest("shell", _root.Path, []),
            CancellationToken.None
        );

        exitCode.Should().Be(0);
        _console.AllText.Should().Contain("newer build");
        await _sbx.Received(1).RemoveSandboxAsync(name, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ANewerBuildIsNotOfferedWhenTheAnswerIsNo()
    {
        var name = SandboxName.For(_root.Path);
        _console.Answer = false;
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.Now));
        _records.Save(
            name,
            new SandboxRecord
            {
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
            }
        );

        await _service.RunAsync(new RunRequest("shell", _root.Path, []), CancellationToken.None);

        await _sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
