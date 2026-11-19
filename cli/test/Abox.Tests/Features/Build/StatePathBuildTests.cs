using System.Text.Json;
using Abox.Configuration;
using Abox.Features.Build;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class StatePathBuildTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly ISbxClient _sbx = Substitute.For<ISbxClient>();
    private readonly IProcessRunner _runner = LocalWorkloadFactory.Runner();
    private readonly BuildStore _store;
    private readonly BuildService _build;
    private readonly SquadResolver _resolver = new(new AboxConfig());

    public StatePathBuildTests()
    {
        var paths = AboxPaths.For(
            _root.CreateDirectory("config"),
            Path.Combine(_root.Path, "state")
        );
        _store = new BuildStore(paths);
        _root.File("teams/sample/sample.workload", "template: example.com/base:1\n");
        _build = TestServiceGraphs.Build(
            _resolver,
            new PersonalConfigurations(paths),
            _store,
            LocalWorkloadFactory.Images(_runner),
            _sbx,
            new TestConsole()
        );
    }

    [Theory]
    [InlineData("~/../../outside")]
    [InlineData("/var/../outside")]
    [InlineData("~/.config/./sessions")]
    [InlineData("~other/cache")]
    [InlineData("~\\outside")]
    [InlineData("/var\\..\\outside")]
    [InlineData("C:/outside")]
    [InlineData("C:\\outside")]
    [InlineData("//server/share")]
    [InlineData("\\\\server\\share")]
    [InlineData("/C:/outside")]
    [InlineData("/var/cache:stream")]
    [InlineData("/var/cache.")]
    [InlineData("/var/cache /sessions")]
    [InlineData("/var/CON/sessions")]
    [InlineData("/var/nul.txt")]
    [InlineData("/var/LPT1")]
    [InlineData("/var/COM¹.txt")]
    [InlineData("/var/CONIN$")]
    [InlineData("/var/ca?he")]
    [InlineData("/var/ca*he")]
    [InlineData("/var/ca|he")]
    [InlineData("/var/ca<he")]
    [InlineData("/var/ca>he")]
    [InlineData("/var/ca\"he")]
    [InlineData("/var/ca\nhe")]
    [InlineData("/var/ca\u0000he")]
    [InlineData("/")]
    [InlineData("")]
    [InlineData("relative/cache")]
    public async Task UnsafeStateFailsBeforeBuildOutputOrToolRequests(string declaration)
    {
        // Arrange
        _root.File(
            "teams/sample/ac.abox",
            "workload: sample\nstate:\n  - " + JsonSerializer.Serialize(declaration) + "\n"
        );
        var sentinel = _root.File("outside/sentinel.txt", "keep existing data");

        // Act
        var action = () => BuildAsync();

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*state*");
        File.ReadAllText(sentinel).Should().Be("keep existing data");
        Directory.GetFileSystemEntries(Path.GetDirectoryName(sentinel)!).Should().ContainSingle();
        Directory.Exists(Path.Combine(_root.Path, "state")).Should().BeFalse();
        _runner.ReceivedCalls().Should().BeEmpty();
        _sbx.ReceivedCalls().Should().BeEmpty();
    }

    private Task<int> BuildAsync() =>
        _build.BuildAsync(
            new BuildRequest(
                [_resolver.ResolveTeam(_root.Path, "sample")],
                _root.Path,
                "latest",
                MetadataOnly: true
            ),
            CancellationToken.None
        );

    [Theory]
    [InlineData("~/.cache", "/home/agent/.cache")]
    [InlineData("~/.cache", "~////.cache/")]
    [InlineData("/var/cache", "/var////cache/sessions")]
    [InlineData("~/.cache/sessions", "~/.cache")]
    [InlineData("~/.cache", "~/.CACHE/sessions")]
    [InlineData("~/.cache", "/.cache")]
    [InlineData("~/.cache", "/.cache/sessions")]
    [InlineData("~", "~/.cache")]
    [InlineData("~/caf\u00e9", "~/cafe\u0301/sessions")]
    public async Task EquivalentOrOverlappingStateCannotEvadeBuildValidation(
        string first,
        string second
    )
    {
        // Arrange
        _root.File(
            "teams/sample/ac.abox",
            $"workload: sample\nstate: {JsonSerializer.Serialize(new[] { first, second })}\n"
        );

        // Act
        var action = () => BuildAsync();

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*state*");
        Directory.Exists(Path.Combine(_root.Path, "state")).Should().BeFalse();
        _runner.ReceivedCalls().Should().BeEmpty();
        _sbx.ReceivedCalls().Should().BeEmpty();
    }

    public void Dispose() => _root.Dispose();
}
