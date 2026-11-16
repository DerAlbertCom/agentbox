using System.Diagnostics;
using Abox.Infrastructure;

namespace Abox.Tests;

public sealed class DesktopProcessTests
{
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
