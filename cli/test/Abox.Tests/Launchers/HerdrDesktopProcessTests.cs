using System.Diagnostics;
using Abox.Infrastructure;
using Abox.Launchers;
using NSubstitute;

namespace Abox.Tests;

public sealed class HerdrDesktopProcessTests
{
    [Fact]
    public async Task ConfigurationDiagnosticsReleaseTheNewDesktopBeforeOpeningASpace()
    {
        // Arrange
        using var root = new TempDirectory();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var fixture = new HerdrProtocolFixture();
        fixture.Statuses.Enqueue(
            new(2, "", "herdr-app: no Herdr window at /synthetic/socket: refused")
        );
        fixture.Statuses.Enqueue(
            new(0, "{\"errors\":[\"layout: synthetic-secret\\u001b[31m\"]}", "")
        );
        Process? child = null;
        UseDesktopProcess(
            fixture,
            root.Path,
            process =>
            {
                child = process;
                fixture.Calls.Should().NotContain(r => r.Arguments.Contains("space.new"));
            }
        );
        try
        {
            // Act
            var result = await fixture.Launcher.RunAsync(fixture.Session, deadline.Token);

            // Assert
            result.Should().Be(0);
            child.Should().NotBeNull();
            child!
                .HasExited.Should()
                .BeFalse("configuration diagnostics do not revoke desktop ownership release");
            fixture.Calls[^1].Arguments.Should().Contain("space.new");
            var console = fixture.Console;
            console
                .Errors.Should()
                .ContainSingle()
                .Which.Should()
                .Contain("configuration diagnostics");
            console.AllText.Should().NotContain("synthetic-secret").And.NotContain("\u001b");
        }
        finally
        {
            await StopChildAsync(child);
        }
    }

    [Theory]
    [InlineData("{\"errors\":[\"synthetic-secret\",null]}")]
    [InlineData("{\"errors\":[\"synthetic-secret\"],\"error\":\"control failure\"}")]
    public async Task MalformedStatusStillCleansUpOnlyTheNewDesktop(string status)
    {
        // Arrange
        using var root = new TempDirectory();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var fixture = new HerdrProtocolFixture();
        fixture.Statuses.Enqueue(
            new(2, "", "herdr-app: no Herdr window at /synthetic/socket: refused")
        );
        fixture.Statuses.Enqueue(new(0, status, ""));
        Process? child = null;
        UseDesktopProcess(fixture, root.Path, process => child = process);
        try
        {
            // Act
            var action = () => fixture.Launcher.RunAsync(fixture.Session, deadline.Token);

            // Assert
            (await action.Should().ThrowAsync<AboxException>())
                .Which.Message.Should()
                .Contain("herdr-app status")
                .And.NotContain("synthetic-secret");
            child.Should().NotBeNull();
            child!.HasExited.Should().BeTrue();
            fixture
                .Calls.Should()
                .NotContain(r => r.Arguments.Contains("space.new") || r.Arguments.Contains("add"));
            fixture.Console.AllText.Should().BeEmpty();
        }
        finally
        {
            await StopChildAsync(child);
        }
    }

    [Fact]
    public async Task DesktopFailureIsReportedWithoutWaitingForReadinessTimeout()
    {
        // Arrange
        IProcessRunner runner = new ProcessRunner();
        var request = new ProcessRequest(
            OperatingSystem.IsWindows() ? "cmd" : "sh",
            OperatingSystem.IsWindows() ? ["/c", "exit /b 17"] : ["-c", "exit 17"],
            AttachConsole: true
        );
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        var result = await runner.RunUntilReadyAsync(
            request,
            token => Task.Delay(Timeout.Infinite, token),
            deadline.Token
        );

        // Assert
        result.ExitCode.Should().Be(17);
        deadline.IsCancellationRequested.Should().BeFalse();
    }

    [Theory]
    [InlineData(ProcessOutputPolicy.Sensitive, true, null)]
    [InlineData(ProcessOutputPolicy.CapturedSensitive, true, null)]
    [InlineData(ProcessOutputPolicy.Normal, false, null)]
    [InlineData(ProcessOutputPolicy.Normal, true, "secret")]
    public async Task DesktopStartupCannotBypassStreamSafety(
        ProcessOutputPolicy policy,
        bool attach,
        string? input
    )
    {
        // Arrange
        IProcessRunner runner = new ProcessRunner();
        var request = new ProcessRequest(
            "must-not-be-started",
            [],
            AttachConsole: attach,
            StandardInput: input,
            OutputPolicy: policy
        );

        // Act
        var action = () =>
            runner.RunUntilReadyAsync(request, _ => Task.CompletedTask, CancellationToken.None);

        // Assert
        await action.Should().ThrowAsync<AboxException>().WithMessage("*inherited streams*");
    }

    [Fact]
    public async Task ConfirmedDesktopReturnsWhileItsEventLoopIsStillRunning()
    {
        // Arrange
        using var root = new TempDirectory();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        IProcessRunner runner = new ProcessRunner();
        Process? child = null;
        try
        {
            // Act
            var result = await runner.RunUntilReadyAsync(
                Desktop(root.Path),
                async token => child = await FindChildAsync(root.Path, token),
                deadline.Token
            );

            // Assert
            result.Succeeded.Should().BeTrue();
            child.Should().NotBeNull();
            child!.HasExited.Should().BeFalse();
        }
        finally
        {
            await StopChildAsync(child);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledReadinessDoesNotLeaveTheNewDesktopRunning(bool cancel)
    {
        // Arrange
        using var root = new TempDirectory();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        IProcessRunner runner = new ProcessRunner();
        Process? child = null;
        try
        {
            // Act
            var action = () =>
                runner.RunUntilReadyAsync(
                    Desktop(root.Path),
                    async token =>
                    {
                        child = await FindChildAsync(root.Path, token);
                        if (cancel)
                        {
                            deadline.Cancel();
                            token.ThrowIfCancellationRequested();
                        }
                        throw new AboxException("Invalid status");
                    },
                    deadline.Token
                );

            // Assert
            if (cancel)
                await action.Should().ThrowAsync<OperationCanceledException>();
            else
                await action.Should().ThrowAsync<AboxException>().WithMessage("Invalid status");
            child.Should().NotBeNull();
            child!.HasExited.Should().BeTrue();
        }
        finally
        {
            await StopChildAsync(child);
        }
    }

    private static void UseDesktopProcess(
        HerdrProtocolFixture fixture,
        string directory,
        Action<Process> observeChild
    )
    {
        IProcessRunner processes = new ProcessRunner();
        fixture
            .Runner.RunUntilReadyAsync(
                Arg.Any<ProcessRequest>(),
                Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                processes.RunUntilReadyAsync(
                    Desktop(directory),
                    async token =>
                    {
                        observeChild(await FindChildAsync(directory, token));
                        await call.Arg<Func<CancellationToken, Task>>()(token);
                    },
                    call.Arg<CancellationToken>()
                )
            );
    }

    private static async Task StopChildAsync(Process? child)
    {
        if (child is null)
            return;
        using (child)
        {
            if (!child.HasExited)
                child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static ProcessRequest Desktop(string directory) =>
        new(
            OperatingSystem.IsWindows() ? "powershell" : "sh",
            OperatingSystem.IsWindows()
                ?
                [
                    "-NoProfile",
                    "-NonInteractive",
                    "-Command",
                    "$PID | Set-Content pid.tmp; Move-Item pid.tmp pid.txt; Start-Sleep -Seconds 60",
                ]
                : ["-c", "echo $$ > pid.tmp; mv pid.tmp pid.txt; sleep 60"],
            AttachConsole: true,
            WorkingDirectory: directory
        );

    private static async Task<Process> FindChildAsync(string directory, CancellationToken token)
    {
        var path = Path.Combine(directory, "pid.txt");
        while (!File.Exists(path))
            await Task.Delay(20, token);
        return Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(path, token)));
    }
}
