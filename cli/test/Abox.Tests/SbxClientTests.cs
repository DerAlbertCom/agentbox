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

    [Theory]
    [InlineData("rwest.jfrog.io/mtiae-docker-release/agentbox-rwest-sample/ac:latest", "always")]
    [InlineData("/kits/workload", "never")]
    public async Task CreationPassesTheExplicitPullPolicy(string source, string pullPolicy)
    {
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, string.Empty, string.Empty));

        var outcome = await _client.CreateAsync(
            new SbxCreateRequest(
                source,
                "/workspace",
                "sample",
                [],
                new Dictionary<string, string>(),
                [],
                [],
                pullPolicy
            ),
            CancellationToken.None
        );

        outcome.Succeeded.Should().BeTrue();
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.SequenceEqual(
                        new[]
                        {
                            "create",
                            source,
                            "/workspace",
                            "--name",
                            "sample",
                            "--pull",
                            pullPolicy,
                        }
                    )
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task TheVersionIsReadFromTheJsonOutput()
    {
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ProcessResult(
                    0,
                    """
                    {
                      "client": {
                        "version": "v0.51.0",
                        "revision": "7d55286de0c775148b985ed349f760f1eba06203"
                      },
                      "server": {
                        "state": "running",
                        "version": "v0.51.0",
                        "revision": "7d55286de0c775148b985ed349f760f1eba06203",
                        "api_version": "0.49.0"
                      }
                    }
                    """,
                    string.Empty
                )
            );

        var version = await _client.GetVersionAsync(CancellationToken.None);

        version.Should().Be(new Version(0, 51, 0));
    }

    [Fact]
    public async Task ThinLocalKitsUseTheHostBuilderAndRecordedDockerContext()
    {
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "", ""));
        await _client.CreateAsync(
            new SbxCreateRequest(
                "/runtime/workload",
                "/workspace",
                "sample",
                ["/kits/config"],
                new Dictionary<string, string> { ["mode"] = "team" },
                [],
                ["elastic"],
                HostDockerContext: "desktop-linux"
            ),
            CancellationToken.None
        );

        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Environment!["SBX_KIT_BUILDER"] == "host"
                    && request.Environment["BUILDX_BUILDER"] == "desktop-linux"
                    && request.Environment["DOCKER_CONTEXT"] == "desktop-linux"
                    && request.Arguments.Contains("mode=team")
                    && request.Arguments.Contains("/kits/config")
                    && request.Arguments.Contains("elastic")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AVersonlessAnswerIsAnError()
    {
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "{\"server\": {\"state\": \"stopped\"}}", string.Empty));

        var call = async () => await _client.GetVersionAsync(CancellationToken.None);

        (await call.Should().ThrowAsync<AboxException>()).WithMessage(
            "*Cannot read the sbx version from ''*"
        );
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
    public async Task TheTemplatesAreReadFromTheJsonOutput()
    {
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ProcessResult(
                    0,
                    """
                    [
                      {
                        "id": "527ef0280e47",
                        "repository": "docker.io/library/sandbox-kit-assembled",
                        "tag": "0307199587cd420e",
                        "flavor": "shell-docker",
                        "created_at": "2026-11-08T14:49:51Z",
                        "size": 1389240008
                      },
                      {
                        "id": "ecc48f9d23bd",
                        "repository": "docker.io/library/sbx-kit-src",
                        "tag": "pi-config-eacbe0b12b58",
                        "created_at": "2026-11-08T14:55:39Z",
                        "size": 6957
                      }
                    ]
                    """,
                    string.Empty
                )
            );

        var templates = await _client.ListTemplatesAsync(CancellationToken.None);

        templates.Should().HaveCount(2);
        templates[0]
            .Reference.Should()
            .Be("docker.io/library/sandbox-kit-assembled:0307199587cd420e");
        templates[0].Id.Should().Be("527ef0280e47");
        templates[0].CreatedAt.Should().Be(DateTimeOffset.Parse("2026-11-08T14:49:51Z"));
        templates[1].Repository.Should().Be("docker.io/library/sbx-kit-src");
        templates[1].CreatedAt.Should().NotBeNull();
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
