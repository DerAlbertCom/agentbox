using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

public sealed class PortsCommandHttpsTests
{
    private const string Configured =
        "HTTPS default certificate and exact public sandbox system trust configured";

    [Fact]
    public async Task ConfiguresHttpsInOrderPasswordExportActivateVerify()
    {
        // Arrange
        using var scenario = new HttpsScenario();

        // Act
        var exit = await scenario.Fixture.Run();

        // Assert
        exit.Should().Be(0, scenario.Fixture.Console.AllText);
        scenario
            .Steps.Should()
            .Equal("prepare", "export", "id", "root trust", "activate", "status");
        scenario.Fixture.Console.AllText.Should().Contain(Configured);
    }

    [Theory]
    [InlineData("prepare", "export")]
    [InlineData("export", "id")]
    [InlineData("activate", "status")]
    [InlineData("status", null)]
    public async Task CancellationStopsBeforeTheNextHttpsStep(string cancelAt, string? notReached)
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        using var scenario = new HttpsScenario(onStep: step =>
        {
            if (step == cancelAt)
                cancellation.Cancel();
        });

        // Act
        var exit = await scenario.Fixture.Run(cancellation.Token);

        // Assert
        exit.Should().Be(130, scenario.Fixture.Console.AllText);
        scenario.Steps[^1].Should().Be(cancelAt);
        if (notReached is not null)
            scenario.Steps.Should().NotContain(notReached);
        scenario.Fixture.Console.AllText.Should().NotContain(Configured);
    }

    [Theory]
    [InlineData(42, "hc server restart --fresh")]
    [InlineData(43, "/proc process environments are not inspectable")]
    [InlineData(44, "NODE_EXTRA_CA_CERTS")]
    [InlineData(75, "Protected HTTPS runtime is busy")]
    [InlineData(17, "Cannot verify protected HTTPS launch configuration")]
    public async Task ReceiverStatusOutcomesFailWithTheirGuidance(int code, string guidance)
    {
        // Arrange
        using var scenario = new HttpsScenario(statusExitCode: code);

        // Act
        var exit = await scenario.Fixture.Run();

        // Assert
        exit.Should().Be(1);
        scenario
            .Fixture.Console.AllText.Should()
            .Contain(guidance)
            .And.NotContain("private child")
            .And.NotContain(Configured);
    }

    [Fact]
    public async Task BusyRuntimeStopsBeforeTheHostExport()
    {
        // Arrange
        using var scenario = new HttpsScenario(prepareExitCode: 75);

        // Act
        var exit = await scenario.Fixture.Run();

        // Assert
        exit.Should().Be(1);
        scenario.Fixture.Console.AllText.Should().Contain("Protected HTTPS runtime is busy");
        scenario.Steps.Should().Equal("prepare");
    }

    [Fact]
    public async Task BootstrapRefreshRunsOnlyTheReceiverRefresh()
    {
        // Arrange
        using var scenario = new HttpsScenario();

        // Act
        var exit = await scenario.Fixture.Run("--refresh-https-bootstrap");

        // Assert
        exit.Should().Be(0, scenario.Fixture.Console.AllText);
        scenario.Steps.Should().Equal("refresh-bootstrap");
        scenario
            .Fixture.Console.AllText.Should()
            .Contain(
                "HTTPS bootstrap refreshed from this CLI. Private activation retained; HTTPS/Node/agent readiness was not verified. Retry the original launch; use abox ports for full setup."
            );
    }

    [Fact]
    public async Task FailedBootstrapRefreshPointsToFullSetup()
    {
        // Arrange
        using var scenario = new HttpsScenario(refreshExitCode: 1);

        // Act
        var exit = await scenario.Fixture.Run("--refresh-https-bootstrap");

        // Assert
        exit.Should().Be(1);
        scenario
            .Fixture.Console.AllText.Should()
            .Contain("Cannot refresh the protected HTTPS bootstrap")
            .And.Contain("rerun abox ports for full setup")
            .And.NotContain("bootstrap refreshed");
    }

    /// <summary>A .NET web folder whose Sandbox answers through the real sbx adapter.</summary>
    private sealed class HttpsScenario : IDisposable
    {
        private readonly LifecycleCertificateCli certificate;
        private readonly Action<string> onStep;

        public HttpsScenario(
            Action<string>? onStep = null,
            int prepareExitCode = 0,
            int statusExitCode = 0,
            int refreshExitCode = 0
        )
        {
            this.onStep = onStep ?? (_ => { });
            var runner = Substitute.For<IProcessRunner>();
            var console = new TestConsole();
            certificate = new LifecycleCertificateCli(console) { Exported = () => Step("export") };
            Fixture = new PortsCommandFixture(runner: runner, exporter: certificate.Exporter);
            Fixture.Root.File("folder/Web.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");
            runner
                .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var arguments = call.Arg<ProcessRequest>().Arguments;
                    return arguments[0] switch
                    {
                        "version" => Result(0, """{"client":{"version":"0.51.0"}}"""),
                        "diagnose" => Result(0, """{"checks":[]}"""),
                        "ls" => Result(0, $$"""[{"name":"{{Fixture.Name}}","status":"running"}]"""),
                        "exec" when arguments.Contains("id") => Step("id", 0, "1000\n"),
                        "exec" when arguments.Contains("root") => Step("root trust", 0),
                        "exec" when arguments.Contains("prepare") => Step(
                            "prepare",
                            prepareExitCode,
                            new string('a', 64)
                        ),
                        "exec" when arguments.Contains("activate") => Step("activate", 0),
                        "exec" when arguments.Contains("status") => Step("status", statusExitCode),
                        "exec" when arguments.Contains("refresh-bootstrap") => Step(
                            "refresh-bootstrap",
                            refreshExitCode
                        ),
                        _ => throw new InvalidOperationException("Unexpected runtime operation."),
                    };
                });
        }

        public PortsCommandFixture Fixture { get; }
        public List<string> Steps { get; } = [];

        private void Step(string step)
        {
            Steps.Add(step);
            onStep(step);
        }

        private ProcessResult Step(string step, int exitCode, string output = "")
        {
            Step(step);
            return Result(exitCode, output);
        }

        private static ProcessResult Result(int exitCode, string output) =>
            new(exitCode, output, "private child diagnostics");

        public void Dispose()
        {
            Fixture.Dispose();
            certificate.Dispose();
        }
    }
}
