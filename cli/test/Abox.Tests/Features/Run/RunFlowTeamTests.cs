using System.Text.Json;
using Abox.Features.Run;
using Abox.LocalState;
using Abox.Naming;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class RunFlowTeamTests
{
    private readonly RunFlowFixture _fixture = new();
    private TempDirectory _root => _fixture.Root;
    private TestConsole _console => _fixture.Console;
    private ISbxClient _sbx => _fixture.Sbx;
    private RunFlow _service => _fixture.Service;
    private BuildStore _store => _fixture.Store;
    private SandboxRecordStore _records => _fixture.Records;

    [Theory]
    [InlineData("qualified")]
    [InlineData("legacy")]
    [InlineData("unknown-legacy")]
    [InlineData("corrupt-qualified")]
    public async Task UnrelatedOrUnprovenBuildsNeverOfferReplacementOrInterruptLaunch(
        string storage
    )
    {
        // Arrange
        var name = SandboxName.For(_root.Path);
        _sbx.ListSandboxesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.Now) });
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
        var build = new BuildManifest
        {
            Squad = "ac",
            Tag = "latest",
            Reference = "agentic-box-rwest-other:latest",
            CreatedAt = DateTimeOffset.Now,
            LocalWorkload = LocalWorkloadFactory.Artifact(_root),
        };
        if (storage == "qualified")
            _store.Save(build);
        else
        {
            if (storage == "unknown-legacy")
                build.Reference = null;
            _root.File(
                storage == "corrupt-qualified"
                    ? "state/builds/sample/ac/latest/build.json"
                    : "state/builds/ac/latest/build.json",
                JsonSerializer.Serialize(build)
            );
        }

        // Act
        var exitCode = await _service.RunAsync(
            new RunRequest("shell", _root.Path, []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        _console.AllText.Should().NotContain("newer build").And.NotContain("Replace the sandbox");
        if (storage != "qualified")
            _console.AllText.Should().Contain("abox build --team sample --tag latest");
        await _sbx.Received(1).ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>());
        await _sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sbx.DidNotReceive()
            .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacementUsesTheRecordedTeamsBuildEvenWhenAnotherTeamsSharedSquadIsNewer(
        bool legacy
    )
    {
        // Arrange
        using var otherSources = new TempDirectory();
        var otherImage = LocalWorkloadFactory.Artifact(otherSources);
        var name = SandboxName.For(_root.Path);
        _root.File("teams/sample/ac.abox", "workload: sample\n");
        _sbx.ListSandboxesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.Now) });
        _sbx.RemoveSandboxAsync(name, Arg.Any<CancellationToken>()).Returns(new SbxOutcome(0, ""));
        _sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, ""));
        _sbx.ListMcpServersAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<string>)[]);
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
        var selected = new BuildManifest
        {
            Squad = "ac",
            Tag = "latest",
            Reference = "agentic-box-rwest-sample:latest",
            RootDirectory = _root.Path,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            LocalWorkload = LocalWorkloadFactory.Artifact(_root),
        };
        if (legacy)
            _root.File("state/builds/ac/latest/build.json", JsonSerializer.Serialize(selected));
        else
            _store.Save(selected);
        _store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                Reference = "agentic-box-rwest-other:latest",
                RootDirectory = otherSources.Path,
                CreatedAt = DateTimeOffset.UtcNow,
                LocalWorkload = otherImage,
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
        await _sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request =>
                    request.KitSource == selected.LocalWorkload.RuntimeKitDirectory
                ),
                Arg.Any<CancellationToken>()
            );
        _records.Load(name)!.Team.Should().Be("sample");
        _records.Load(name)!.BuildCreatedAt.Should().Be(selected.CreatedAt);
        await _sbx.Received(1).ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateHintsNeverBorrowAnotherTeamsBuildOrSourceRoot(bool hasOwnedBuild)
    {
        // Arrange
        using var other = new TempDirectory();
        _records.Save(
            SandboxName.For(_root.Path),
            new SandboxRecord
            {
                Team = "sample",
                Squad = "ac",
                Tag = "latest",
            }
        );
        if (hasOwnedBuild)
            _store.Save(
                new BuildManifest
                {
                    Squad = "ac",
                    Tag = "latest",
                    Reference = "agentic-box-rwest-sample:latest",
                    RootDirectory = _root.Path,
                }
            );
        _store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                Reference = "agentic-box-rwest-other:latest",
                RootDirectory = other.Path,
            }
        );

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
            .And.NotContain(other.Path);
        if (hasOwnedBuild)
            _console.AllText.Should().Contain($"--local --root \"{_root.Path}\"");
        else
            _console.AllText.Should().NotContain("--local").And.NotContain("--root");
    }
}
