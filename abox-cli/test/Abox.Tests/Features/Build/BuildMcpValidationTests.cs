using Abox.Configuration;
using Abox.Features.Build;
using Abox.Health;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Naming;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class BuildMcpValidationTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly ISbxClient _sbx = Substitute.For<ISbxClient>();
    private readonly IProcessRunner _runner = LocalWorkloadFactory.Runner();
    private readonly TestConsole _console = new();
    private readonly AboxPaths _paths;
    private readonly BuildStore _store;
    private readonly BuildCommand _command;

    public BuildMcpValidationTests()
    {
        _paths = AboxPaths.For(_root.CreateDirectory("config"), Path.Combine(_root.Path, "state"));
        _store = new BuildStore(_paths);
        _root.File("teams/sample/ac.abox", "workload: sample\n");
        _root.File("teams/sample/sample.workload", "template: example.com/base:1\n");
        var programs = Substitute.For<IHostPrograms>();
        programs.Exists("sbx").Returns(true);
        programs.Exists("docker").Returns(true);
        _sbx.GetVersionAsync(Arg.Any<CancellationToken>()).Returns(new Version(0, 51, 0));
        _sbx.DiagnoseAsync(Arg.Any<CancellationToken>()).Returns(new DiagnoseReport([]));
        var config = new AboxConfig();
        var resolver = new SquadResolver(config);
        var build = TestServiceGraphs.Build(
            resolver,
            new PersonalConfigurations(_paths),
            _store,
            new LocalWorkloadImages(programs, new OciRegistry(_runner), _console),
            _sbx,
            _console
        );
        _command = new BuildCommand(
            new Lazy<AboxConfig>(() => config),
            new Preflight(_console, _sbx, programs),
            new Lazy<SquadResolver>(() => resolver),
            new Lazy<BuildService>(() => build),
            _console
        );
    }

    [Theory]
    [MemberData(nameof(UnsupportedDeclarations))]
    public async Task InvalidMcpDeclarationsFailBeforeChangingBuildsOrRuntime(
        string field,
        string value,
        string origin,
        bool metadataOnly
    )
    {
        // Arrange
        (await BuildAsync(metadataOnly))
            .Should()
            .Be(0, _console.AllText);
        var buildDirectory = _store.DirectoryFor(new SquadBuildIdentity("sample", "ac", "latest"));
        _root.File(Path.Combine(buildDirectory, "workload/scripts/keep.sh"), "keep this script");
        var before = FilesInState();
        var declaration = field switch
        {
            "name" => $"mcp:\n  - name: {value}\n    url: https://example.com/mcp\n",
            "url" => $"mcp:\n  - name: elastic\n    url: {value}\n",
            _ =>
                $"mcp:\n  - name: elastic\n    url: https://example.com/mcp\n    {field}: {value}\n",
        };
        var source = origin switch
        {
            "personal" => _root.File("config/personal.abox", declaration),
            "base" => _root.File("teams/sample/base.abox", "workload: sample\n" + declaration),
            _ => _root.File("teams/sample/ac.abox", "workload: sample\n" + declaration),
        };
        if (origin == "base")
            _root.File("teams/sample/ac.abox", "base: base\n");
        _runner.ClearReceivedCalls();
        _sbx.ClearReceivedCalls();

        // Act
        var exitCode = await BuildAsync(metadataOnly);

        // Assert
        exitCode.Should().Be(1);
        _console
            .Errors.Should()
            .ContainSingle()
            .Which.Should()
            .Contain(Path.GetFullPath(source))
            .And.Contain($"mcp.{field}")
            .And.Contain(field is "name" or "url" ? "not valid" : "not a known key");
        if (field is "command" or "args" or "headers")
            _console
                .Errors.Single()
                .Should()
                .Contain("not a known key")
                .And.Contain("Known keys: name, url");
        _console.AllText.Should().NotContain("password");
        FilesInState()
            .Should()
            .BeEquivalentTo(before, "a failed build must preserve previous output");
        _runner.ReceivedCalls().Should().BeEmpty();
        _sbx.ReceivedCalls()
            .Should()
            .OnlyContain(call =>
                call.GetMethodInfo().Name == nameof(ISbxClient.GetVersionAsync)
                || call.GetMethodInfo().Name == nameof(ISbxClient.DiagnoseAsync)
            );
    }

    public static IEnumerable<object[]> UnsupportedDeclarations()
    {
        foreach (
            var (field, value) in new[]
            {
                ("command", "node"),
                ("command", "\"\""),
                ("args", "[server.js]"),
                ("args", "[]"),
                ("headers", "{X-Example: configured}"),
                ("headers", "{}"),
                ("name", "'elastic,other'"),
                ("name", "'-elastic'"),
                ("name", "'élastic'"),
                ("name", "''"),
                ("url", "file:///local"),
                ("url", "https://user:password@example.com/mcp"),
                ("url", "not-a-url"),
                ("url", "''"),
            }
        )
        foreach (var origin in new[] { "squad", "base", "personal" })
        foreach (var metadataOnly in new[] { false, true })
            yield return [field, value, origin, metadataOnly];
    }

    [Theory]
    [InlineData("squad")]
    [InlineData("base")]
    [InlineData("personal")]
    public async Task DuplicateMcpNamesRemainAnActionableBuildError(string origin)
    {
        // Arrange
        var source = _root.File("teams/sample/ac.abox", "workload: sample\nmcp: [elastic]\n");
        var duplicate = "mcp:\n  - name: elastic\n    url: https://example.com/mcp\n";
        string other;
        if (origin == "personal")
        {
            other = _root.File("config/personal.abox", duplicate);
        }
        else if (origin == "base")
        {
            other = _root.File("teams/sample/base.abox", "workload: sample\n" + duplicate);
            _root.File("teams/sample/ac.abox", "base: base\nmcp: [elastic]\n");
        }
        else
        {
            other = source;
            _root.File(
                "teams/sample/ac.abox",
                "workload: sample\nmcp:\n  - elastic\n  - name: elastic\n    url: https://example.com/mcp\n"
            );
        }

        // Act
        var exitCode = await BuildAsync(metadataOnly: true);

        // Assert
        exitCode.Should().Be(1);
        _console
            .AllText.Should()
            .Contain("MCP server 'elastic'")
            .And.Contain("declared twice")
            .And.Contain(Path.GetFullPath(source))
            .And.Contain(Path.GetFullPath(other));
        Directory.Exists(_paths.StateDirectory).Should().BeFalse();
        _runner.ReceivedCalls().Should().BeEmpty();
        _sbx.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "missing")]
    [InlineData("parameters: {missing: 'file:///local'}\n", "mcp.url")]
    [InlineData("parameters: {missing: 'https://user:password@example.com/mcp'}\n", "mcp.url")]
    public async Task AnInvalidMcpUrlParameterStillFailsBeforeGeneration(
        string parameters,
        string error
    )
    {
        // Arrange
        var source = _root.File(
            "teams/sample/ac.abox",
            "workload: sample\nmcp:\n  - name: elastic\n    url: ${{ parameters.missing }}\n"
                + parameters
        );

        // Act
        var exitCode = await BuildAsync(metadataOnly: true);

        // Assert
        exitCode.Should().Be(1);
        _console
            .AllText.Should()
            .Contain(error)
            .And.Contain(Path.GetFullPath(source))
            .And.NotContain("password");
        Directory.Exists(_paths.StateDirectory).Should().BeFalse();
        _runner.ReceivedCalls().Should().BeEmpty();
        _sbx.ReceivedCalls().Should().BeEmpty();
    }

    private Task<int> BuildAsync(bool metadataOnly)
    {
        var arguments = new List<string> { "--team", "sample", "--root", _root.Path };
        if (metadataOnly)
            arguments.Add("--metadata-only");
        return _command.Parse(arguments.ToArray()).InvokeAsync();
    }

    private Dictionary<string, string> FilesInState() =>
        Directory
            .GetFiles(_paths.StateDirectory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllText);

    public void Dispose() => _root.Dispose();
}
