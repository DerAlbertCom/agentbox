using System.CommandLine;
using Abox.Cli;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class PrunePreflightTests
{
    [Fact]
    public async Task RecoveredAuthenticationAllowsStopsAndRemovalOnlyAfterRediagnosis()
    {
        // Arrange
        using var cli = new PruneCli();

        // Act
        var exitCode = await cli.Root.InvokeAsync(["prune", "--full", "--sbx"]);

        // Assert
        exitCode.Should().Be(0);
        cli.Scenario.Console.Errors.Should().BeEmpty();
        cli.Scenario.Console.Output.Should().Contain("Removed sbx-kit-src:old.");
        cli.Scenario.SbxCalls.Should()
            .Equal(
                "GetVersionAsync",
                "DiagnoseAsync",
                "SignInAsync",
                "DiagnoseAsync",
                "ListSandboxesAsync",
                "StopSandboxAsync",
                "ListTemplatesAsync",
                "RemoveTemplateAsync"
            );
        await cli
            .Scenario.Sbx.Received(1)
            .StopSandboxAsync("synthetic-running", Arg.Any<CancellationToken>());
        await cli
            .Scenario.Sbx.Received(1)
            .RemoveTemplateAsync("sbx-kit-src:old", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeclinedOrFailedSignInPreventsAllPruneOperations()
    {
        // Arrange
        using var cli = new PruneCli();
        cli.Scenario.Sbx.SignInAsync(Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var exitCode = await cli.Root.InvokeAsync(["prune", "--full", "--sbx"]);

        // Assert
        exitCode.Should().Be(1);
        cli.Scenario.Console.Errors.Should()
            .Equal("Signing in failed. Run 'sbx login' and try again.");
        cli.Scenario.Console.AllText.Should().NotContain("credential-sentinel");
        cli.Scenario.SbxCalls.Should().Equal("GetVersionAsync", "DiagnoseAsync", "SignInAsync");
    }

    [Fact]
    public async Task NoninteractiveAuthenticationFailureCannotStopSandboxesOrRemoveImages()
    {
        // Arrange
        using var cli = new PruneCli(interactive: false);

        // Act
        var exitCode = await cli.Root.InvokeAsync(["prune", "--full", "--sbx"]);

        // Assert
        exitCode.Should().Be(1);
        cli.Scenario.Console.Output.Should().BeEmpty();
        cli.Scenario.Console.Errors.Should()
            .Equal("sbx is not signed in. Run 'sbx login' and try again.");
        cli.Scenario.SbxCalls.Should().Equal("GetVersionAsync", "DiagnoseAsync");
    }

    [Theory]
    [InlineData("Authentication", "Run 'sbx login' and try again.")]
    [InlineData("Daemon", "Start the Docker Sandboxes daemon and try again.")]
    public async Task ASecondUnhealthyDiagnosisPreventsAllPruneOperations(string check, string hint)
    {
        // Arrange
        using var cli = new PruneCli();
        cli.Scenario.Sbx.DiagnoseAsync(Arg.Any<CancellationToken>())
            .Returns(
                PreflightScenario.SignedOut(),
                new DiagnoseReport([new DiagnosticCheck(check, "fail", "not ready", hint)])
            );

        // Act
        var exitCode = await cli.Root.InvokeAsync(["prune", "--full", "--sbx"]);

        // Assert
        exitCode.Should().Be(1);
        cli.Scenario.Console.Errors.Should().Equal($"sbx check '{check}' failed: not ready", hint);
        cli.Scenario.Console.AllText.Should().NotContain("credential-sentinel");
        cli.Scenario.SbxCalls.Should()
            .Equal("GetVersionAsync", "DiagnoseAsync", "SignInAsync", "DiagnoseAsync");
    }

    [Fact]
    public async Task FailedRediagnosisIsAnErrorRatherThanPermissionToPrune()
    {
        // Arrange
        using var cli = new PruneCli();
        cli.Scenario.Sbx.DiagnoseAsync(Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromResult(PreflightScenario.SignedOut()),
                _ =>
                    throw new AboxException(
                        "Could not diagnose sbx. Check the daemon and try again."
                    )
            );

        // Act
        var exitCode = await cli.Root.InvokeAsync(["prune", "--full", "--sbx"]);

        // Assert
        exitCode.Should().Be(1);
        cli.Scenario.Console.Errors.Should()
            .Equal("error: Could not diagnose sbx. Check the daemon and try again.");
        cli.Scenario.Console.AllText.Should().NotContain("credential-sentinel");
        cli.Scenario.SbxCalls.Should()
            .Equal("GetVersionAsync", "DiagnoseAsync", "SignInAsync", "DiagnoseAsync");
    }

    [Fact]
    public async Task MissingSbxStopsBeforeVersionDiagnosisLoginOrPruning()
    {
        // Arrange
        using var cli = new PruneCli();
        cli.Scenario.Programs.Exists("sbx").Returns(false);

        // Act
        var exitCode = await cli.Root.InvokeAsync(["prune", "--full", "--sbx"]);

        // Assert
        exitCode.Should().Be(1);
        cli.Scenario.Console.AllText.Should()
            .Contain("not on PATH")
            .And.Contain("never installs anything itself");
        cli.Scenario.SbxCalls.Should().BeEmpty();
        cli.Scenario.Programs.Received(1).Exists("sbx");
    }

    [Fact]
    public async Task OldSbxStopsBeforeDiagnosisLoginOrPruning()
    {
        // Arrange
        using var cli = new PruneCli();
        cli.Scenario.Sbx.GetVersionAsync(Arg.Any<CancellationToken>())
            .Returns(new Version(0, 50, 0));

        // Act
        var exitCode = await cli.Root.InvokeAsync(["prune", "--full", "--sbx"]);

        // Assert
        exitCode.Should().Be(1);
        cli.Scenario.Console.AllText.Should()
            .Contain("too old")
            .And.Contain("Upgrade sbx and try again.");
        cli.Scenario.SbxCalls.Should().Equal("GetVersionAsync");
    }

    [Fact]
    public async Task CancellationDuringLoginReturns130WithoutRediagnosisOrPruning()
    {
        // Arrange
        using var cli = new PruneCli();
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        cli.Scenario.Sbx.SignInAsync(Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<bool>(call.Arg<CancellationToken>());
            });

        // Act
        var exitCode = await cli
            .Root.Parse(["prune", "--full", "--sbx"])
            .InvokeAsync(cancellationToken: token);

        // Assert
        exitCode.Should().Be(130);
        cli.Scenario.Console.Errors.Should().Equal("error: the command was cancelled.");
        cli.Scenario.SbxCalls.Should().Equal("GetVersionAsync", "DiagnoseAsync", "SignInAsync");
        // System.CommandLine links the invocation token; use the actual token seen at version checking.
        cli.Scenario.CommandToken.IsCancellationRequested.Should().BeTrue();
        await cli.Scenario.Sbx.Received(1).SignInAsync(cli.Scenario.CommandToken);
    }

    [Fact]
    public async Task CancellationDuringRediagnosisReturns130WithoutRetryOrPruning()
    {
        // Arrange
        using var cli = new PruneCli();
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        cli.Scenario.Sbx.DiagnoseAsync(Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromResult(PreflightScenario.SignedOut()),
                call =>
                {
                    cancellation.Cancel();
                    return Task.FromCanceled<DiagnoseReport>(call.Arg<CancellationToken>());
                }
            );

        // Act
        var exitCode = await cli
            .Root.Parse(["prune", "--full", "--sbx"])
            .InvokeAsync(cancellationToken: token);

        // Assert
        exitCode.Should().Be(130);
        cli.Scenario.Console.Errors.Should().Equal("error: the command was cancelled.");
        cli.Scenario.SbxCalls.Should()
            .Equal("GetVersionAsync", "DiagnoseAsync", "SignInAsync", "DiagnoseAsync");
        cli.Scenario.CommandToken.IsCancellationRequested.Should().BeTrue();
        await cli.Scenario.Sbx.Received(2).DiagnoseAsync(cli.Scenario.CommandToken);
    }

    private sealed class PruneCli : IDisposable
    {
        private readonly TempDirectory _directory = new();
        private readonly ServiceProvider _provider;

        public PruneCli(bool interactive = true)
        {
            Scenario = new PreflightScenario(interactive);
            SeedPrunableRuntime();
            var services = new ServiceCollection().AddAbox(
                Scenario.Console,
                new TrailingArguments()
            );
            services.AddSingleton(
                AboxPaths.For(_directory.Path, Path.Combine(_directory.Path, "state"))
            );
            services.AddSingleton(Scenario.Sbx);
            services.AddSingleton(Scenario.Programs);
            _provider = services.BuildServiceProvider();
        }

        // A bypass must reach real stop/removal targets, not pass on an empty inventory.
        private void SeedPrunableRuntime()
        {
            Scenario
                .Sbx.ListSandboxesAsync(Arg.Any<CancellationToken>())
                .Returns(new[] { new SandboxInfo("synthetic-running", "running", null) });
            Scenario
                .Sbx.StopSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new SbxOutcome(0, ""));
            Scenario
                .Sbx.ListTemplatesAsync(Arg.Any<CancellationToken>())
                .Returns(
                    new[]
                    {
                        new TemplateImage(
                            "sbx-kit-src",
                            "old",
                            "synthetic-id",
                            DateTimeOffset.Parse("2026-11-01T00:00:00Z")
                        ),
                    }
                );
            Scenario
                .Sbx.RemoveTemplateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new SbxOutcome(0, ""));
        }

        public PreflightScenario Scenario { get; }
        public AboxRootCommand Root => _provider.GetRequiredService<AboxRootCommand>();

        public void Dispose()
        {
            _provider.Dispose();
            _directory.Dispose();
        }
    }
}
