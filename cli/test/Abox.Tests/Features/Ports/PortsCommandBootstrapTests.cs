using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

public sealed class PortsCommandBootstrapTests
{
    [Fact]
    public async Task BootstrapOnlyRefreshRejectsAnOffsetWithoutAnyRuntimeOperation()
    {
        // Arrange
        using var fixture = new PortsCommandFixture();

        // Act
        var exit = await fixture.Run("--refresh-https-bootstrap", "--offset", "10000");

        // Assert
        exit.Should().Be(1);
        fixture
            .Console.AllText.Should()
            .Contain("cannot be combined")
            .And.Contain("changes no ports");
        fixture.Sbx.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ExplicitBootstrapRefreshUsesEmbeddedReceiverWithoutDiscoveryExportOrRootTrust()
    {
        // Arrange
        var runner = Substitute.For<IProcessRunner>();
        using var fixture = new PortsCommandFixture(runner: runner);
        fixture.Root.File("folder/abox.json", "invalid unrelated folder configuration");
        fixture.Root.File("folder/Web.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\">");
        var calls = new List<ProcessRequest>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessRequest>();
                calls.Add(request);
                return new ProcessResult(
                    0,
                    request.Arguments[0] switch
                    {
                        "version" => """{"client":{"version":"0.51.0"}}""",
                        "diagnose" => """{"checks":[]}""",
                        "ls" => $$"""[{"name":"{{fixture.Name}}","status":"running"}]""",
                        "exec" when request.Arguments.Contains("refresh-bootstrap") => "",
                        _ => throw new InvalidOperationException("Unexpected runtime operation."),
                    },
                    "private child diagnostics"
                );
            });

        // Act
        var exit = await fixture.Run("--refresh-https-bootstrap");

        // Assert
        exit.Should().Be(0);
        fixture.Console.AllText.Should().Contain("bootstrap refreshed").And.Contain("not verified");
        fixture
            .Console.AllText.Should()
            .NotContain("private child")
            .And.NotContain("ports published");
        var refresh = calls.Single(call => call.Arguments[0] == "exec");
        refresh.Arguments.Should().ContainInOrder(fixture.Name, "python3", "-I", "-c");
        refresh.Arguments.Should().NotContain("root");
        refresh.StandardInput.Should().BeEmpty();
        refresh.AttachConsole.Should().BeFalse();
        refresh.OutputPolicy.Should().Be(ProcessOutputPolicy.Sensitive);
        calls.Should().OnlyContain(call => call.FileName == "sbx");
    }
}
