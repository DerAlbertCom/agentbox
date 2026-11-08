using Abox.Sbx;

namespace Abox.Tests;

public sealed class SbxClientTests
{
    private readonly FakeProcessRunner _runner = new();
    private readonly SbxClient _client;

    public SbxClientTests()
    {
        _client = new SbxClient(_runner);
    }

    [Fact]
    public async Task TheSandboxesAreReadFromTheJsonOutput()
    {
        _runner.AnsweredWith(
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
            """
        );

        var sandboxes = await _client.ListSandboxesAsync(CancellationToken.None);

        Assert.Equal(2, sandboxes.Count);
        Assert.Equal("agentbox-abox-12345", sandboxes[0].Name);
        Assert.True(sandboxes[0].IsRunning);
        Assert.Equal(DateTimeOffset.Parse("2026-11-08T14:27:10Z"), sandboxes[0].CreatedAt);
        Assert.False(sandboxes[1].IsRunning);
        Assert.Null(sandboxes[1].CreatedAt);
    }

    [Fact]
    public async Task TheDiagnoseReportIsReadFromTheJsonOutput()
    {
        _runner.AnsweredWith(
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
            """
        );

        var report = await _client.DiagnoseAsync(CancellationToken.None);

        var check = Assert.Single(report.Checks);
        Assert.Equal("Daemon", check.Name);
        Assert.True(check.Failed);
        Assert.Equal("start the daemon", check.Hint);
    }

    [Fact]
    public async Task TheMcpServersAreReadFromTheJsonOutput()
    {
        _runner.AnsweredWith(
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
            """
        );

        var servers = await _client.ListMcpServersAsync(CancellationToken.None);

        Assert.Equal(["elastic"], servers);
        Assert.Equal(["mcp", "ls", "--json"], Assert.Single(_runner.Requests).Arguments);
    }

    [Fact]
    public async Task TheSecretsAreReadFromTheJsonOutput()
    {
        _runner.AnsweredWith(
            """
            {
              "secrets": [{ "name": "github" }],
              "custom_secrets": [
                { "targets": ["api.example.com"], "env": "EXAMPLE_TOKEN" }
              ],
              "shadowed_services": [],
              "env_only_count": 0
            }
            """
        );

        var secrets = await _client.ListSecretsAsync(CancellationToken.None);

        Assert.Equal(["github"], secrets.Services);
        var custom = Assert.Single(secrets.Custom);
        Assert.Equal(["api.example.com"], custom.Targets);
        Assert.Equal("EXAMPLE_TOKEN", custom.EnvironmentVariable);
    }

    [Fact]
    public async Task OutputThatIsNotJsonIsAnErrorThatNamesTheCommand()
    {
        _runner.AnsweredWith("not json");

        var exception = await Assert.ThrowsAsync<AboxException>(() =>
            _client.ListSandboxesAsync(CancellationToken.None)
        );

        Assert.Contains("sbx ls", exception.Message);
    }

    [Fact]
    public async Task EmptyOutputIsAnErrorThatNamesTheCommand()
    {
        _runner.AnsweredWith("");

        var exception = await Assert.ThrowsAsync<AboxException>(() =>
            _client.DiagnoseAsync(CancellationToken.None)
        );

        Assert.Contains("sbx diagnose", exception.Message);
    }

    [Fact]
    public async Task AFailedCommandIsAnErrorWithWhatSbxWrote()
    {
        _runner.Results.Enqueue(new Abox.Infrastructure.ProcessResult(1, "", "boom"));

        var exception = await Assert.ThrowsAsync<AboxException>(() =>
            _client.ListSandboxesAsync(CancellationToken.None)
        );

        Assert.Contains("boom", exception.Message);
    }
}
