using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class SbxClientTests
{
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();
    private readonly SbxClient _client;

    public SbxClientTests()
    {
        _client = new SbxClient(_runner);
    }

    [Fact]
    public async Task TheSandboxesAreReadFromTheJsonOutput()
    {
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ProcessResult(
                    0,
                    """
                    [
                      {
                        "name": "agentbox-abox-12345",
                        "id": "1749681b-5622-4313-9d80-bf971233fcea",
                        "agent": "shell",
                        "status": "running",
                        "last_used_at": "2026-11-08T15:03:06.304065176Z",
                        "cpus": 4,
                        "memory": "8192m",
                        "memory_mib": 8192,
                        "created_at": "2026-11-08T14:27:10Z",
                        "detached": true
                      },
                      {
                        "name": "stopped-one",
                        "status": "stopped"
                      }
                    ]
                    """,
                    string.Empty
                )
            );

        var sandboxes = await _client.ListSandboxesAsync(CancellationToken.None);

        sandboxes.Should().HaveCount(2);
        sandboxes[0].Name.Should().Be("agentbox-abox-12345");
        sandboxes[0].IsRunning.Should().BeTrue();
        sandboxes[0].CreatedAt.Should().Be(DateTimeOffset.Parse("2026-11-08T14:27:10Z"));
        sandboxes[1].IsRunning.Should().BeFalse();
        sandboxes[1].CreatedAt.Should().BeNull();
    }

    [Fact]
    public async Task TheDiagnoseReportIsReadFromTheJsonOutput()
    {
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ProcessResult(
                    0,
                    """
                    {
                      "version": "1.0",
                      "checks": [
                        {
                          "name": "Daemon",
                          "status": "fail",
                          "message": "not healthy",
                          "detail": "no answer",
                          "hint": "start the daemon"
                        }
                      ]
                    }
                    """,
                    string.Empty
                )
            );

        var report = await _client.DiagnoseAsync(CancellationToken.None);

        var check = report.Checks.Should().ContainSingle().Which;
        check.Name.Should().Be("Daemon");
        check.Failed.Should().BeTrue();
        check.Hint.Should().Be("start the daemon");
    }

    [Fact]
    public async Task TheMcpServersAreReadFromTheJsonOutput()
    {
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ProcessResult(
                    0,
                    """
                    [
                      {
                        "name": "elastic",
                        "transport": "remote http",
                        "status": "ready",
                        "type": "remote",
                        "gateway": { "name": "LOCAL", "local": true }
                      }
                    ]
                    """,
                    string.Empty
                )
            );

        var servers = await _client.ListMcpServersAsync(CancellationToken.None);

        servers.Should().Equal("elastic");
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.SequenceEqual(new[] { "mcp", "ls", "--json" })
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task TheSecretsAreReadFromTheJsonOutput()
    {
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ProcessResult(
                    0,
                    """
                    {
                      "secrets": [{ "name": "github" }],
                      "custom_secrets": [
                        { "targets": ["api.example.com"], "env": "EXAMPLE_TOKEN" }
                      ],
                      "shadowed_services": [],
                      "env_only_count": 0
                    }
                    """,
                    string.Empty
                )
            );

        var secrets = await _client.ListSecretsAsync(CancellationToken.None);

        secrets.Services.Should().Equal("github");
        var custom = secrets.Custom.Should().ContainSingle().Which;
        custom.Targets.Should().Equal("api.example.com");
        custom.EnvironmentVariable.Should().Be("EXAMPLE_TOKEN");
    }

    [Fact]
    public async Task OutputThatIsNotJsonIsAnErrorThatNamesTheCommand()
    {
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "not json", string.Empty));

        var action = () => _client.ListSandboxesAsync(CancellationToken.None);

        var exception = await action.Should().ThrowAsync<AboxException>();

        exception.Which.Message.Should().Contain("sbx ls");
    }

    [Fact]
    public async Task EmptyOutputIsAnErrorThatNamesTheCommand()
    {
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, string.Empty, string.Empty));

        var action = () => _client.DiagnoseAsync(CancellationToken.None);

        var exception = await action.Should().ThrowAsync<AboxException>();

        exception.Which.Message.Should().Contain("sbx diagnose");
    }

    [Fact]
    public async Task AFailedCommandIsAnErrorWithWhatSbxWrote()
    {
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(1, string.Empty, "boom"));

        var action = () => _client.ListSandboxesAsync(CancellationToken.None);

        var exception = await action.Should().ThrowAsync<AboxException>();

        exception.Which.Message.Should().Contain("boom");
    }
}
