using System.ComponentModel;
using System.Diagnostics;
using Abox.Infrastructure;

namespace Abox.Tests;

public sealed class ProcessRunnerTests
{
    [Theory]
    [InlineData("win32")]
    [InlineData("aggregate")]
    [InlineData("exited")]
    public async Task AKillFailureDoesNotReplaceCancellation(string failure)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var killAttempted = false;
        var runner = new ProcessRunner(
            null,
            process =>
            {
                process.Kill(entireProcessTree: true);
                killAttempted = true;
                throw failure switch
                {
                    "win32" => new Win32Exception("A child cannot be killed."),
                    "aggregate" => new AggregateException("A child exited during the tree walk."),
                    _ => new InvalidOperationException("The process exited."),
                };
            }
        );
        var request = LongRunningRequest();

        var action = () => runner.RunAsync(request, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        killAttempted.Should().BeTrue();
    }

    [Fact]
    public async Task CancellationDoesNotWaitForeverWhenTheChildSurvivesTheKill()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Process? child = null;
        var runner = new ProcessRunner(null, process => child = Process.GetProcessById(process.Id));
        try
        {
            var action = () =>
                runner
                    .RunAsync(LongRunningRequest(), cancellation.Token)
                    .WaitAsync(TimeSpan.FromSeconds(10));

            var exception = await action.Should().ThrowAsync<OperationCanceledException>();

            exception.Which.CancellationToken.Should().Be(cancellation.Token);
            child.Should().NotBeNull();
            child!.HasExited.Should().BeFalse();
        }
        finally
        {
            if (child is not null)
            {
                using (child)
                {
                    child.Kill(entireProcessTree: true);
                    child.WaitForExit(5000);
                }
            }
        }
    }

    private static ProcessRequest LongRunningRequest() =>
        new(
            OperatingSystem.IsWindows() ? "powershell" : "sh",
            OperatingSystem.IsWindows()
                ? ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 60"]
                : ["-c", "sleep 60"]
        );

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 23)]
    [InlineData(true, 0)]
    [InlineData(true, 23)]
    public async Task VerboseShowsBothCompleteStreamsWithoutChangingTheResult(
        bool verbose,
        int exitCode
    )
    {
        using var root = new TempDirectory();
        var stdout = string.Concat(
            Enumerable.Repeat("stdout line: " + new string('x', 1024) + "\n", 100)
        );
        var stderr = string.Concat(
            Enumerable.Repeat("stderr line: " + new string('y', 1024) + "\n", 100)
        );
        var stdoutFile = root.File("stdout.txt", stdout);
        var stderrFile = root.File("stderr.txt", stderr);
        var request = new ProcessRequest(
            OperatingSystem.IsWindows() ? "cmd" : "sh",
            OperatingSystem.IsWindows()
                ? ["/c", $"type stdout.txt & type stderr.txt >&2 & exit /b {exitCode}"]
                :
                [
                    "-c",
                    "cat \"$1\"; cat \"$2\" >&2; exit \"$3\"",
                    "sh",
                    stdoutFile,
                    stderrFile,
                    exitCode.ToString(),
                ],
            WorkingDirectory: root.Path
        );
        var console = new TestConsole();
        var output = new ProcessOutput(console) { Verbose = verbose };

        var result = await new ProcessRunner(output).RunAsync(request, CancellationToken.None);

        var expectedOutput = stdout.Replace("\n", Environment.NewLine);
        var expectedError = stderr.Replace("\n", Environment.NewLine);
        result.ExitCode.Should().Be(exitCode);
        result.StandardOutput.Should().Be(expectedOutput);
        result.StandardError.Should().Be(expectedError);
        console.Output.Should().BeEmpty("tool diagnostics must not pollute stdout");
        if (verbose)
        {
            console.Errors.Should().Contain(expectedOutput).And.Contain(expectedError);
            console.Errors[0].Should().Contain($"exit code: {exitCode}");
        }
        else
        {
            console.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task TheStandardInputOfTheChildIsWritten()
    {
        var request = new ProcessRequest(
            OperatingSystem.IsWindows() ? "cmd" : "sh",
            OperatingSystem.IsWindows() ? ["/c", "sort"] : ["-c", "cat"],
            StandardInput: "the value"
        );

        var result = await new ProcessRunner().RunAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.StandardOutput.Should().Contain("the value");
    }

    [Fact]
    public async Task LargeOutputIsDrainedWhileTheStandardInputIsBeingWritten()
    {
        using var root = new TempDirectory();
        var stdout = string.Concat(Enumerable.Repeat(new string('x', 1024) + "\n", 1024));
        root.File("stdout.txt", stdout);
        var stdin = string.Concat(Enumerable.Repeat("stdin line\n", 100_000));
        var request = new ProcessRequest(
            OperatingSystem.IsWindows() ? "powershell" : "sh",
            OperatingSystem.IsWindows()
                ?
                [
                    "-NoProfile",
                    "-NonInteractive",
                    "-Command",
                    "$PID | Set-Content pid.tmp; Move-Item pid.tmp pid.txt; [Console]::Out.Write([IO.File]::ReadAllText('stdout.txt')); [Console]::Out.Flush(); [Console]::Out.Write([Console]::In.ReadToEnd())",
                ]
                : ["-c", "echo $$ > pid.tmp; mv pid.tmp pid.txt; cat stdout.txt; cat"],
            WorkingDirectory: root.Path,
            StandardInput: stdin
        );
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var result = await new ProcessRunner().RunAsync(request, cancellation.Token);

            result.Succeeded.Should().BeTrue();
            result.StandardOutput.Should().Be((stdout + stdin).Replace("\n", Environment.NewLine));
            result.StandardError.Should().BeEmpty();
        }
        finally
        {
            KillChildIfRunning(root.Path);
        }
    }

    [Fact]
    public async Task CancellingABlockedStandardInputWriteStopsTheChild()
    {
        using var root = new TempDirectory();
        using var cancellation = new CancellationTokenSource();
        var request = new ProcessRequest(
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
            WorkingDirectory: root.Path,
            StandardInput: new string('x', 4 * 1024 * 1024)
        );
        var running = new ProcessRunner().RunAsync(request, cancellation.Token);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var pidFile = Path.Combine(root.Path, "pid.txt");
            while (!File.Exists(pidFile))
            {
                await Task.Delay(50, timeout.Token);
            }
            var pid = int.Parse((await File.ReadAllTextAsync(pidFile, timeout.Token)).Trim());
            using var child = Process.GetProcessById(pid);
            cancellation.Cancel();

            var action = () => running.WaitAsync(timeout.Token);
            await action.Should().ThrowAsync<OperationCanceledException>();
            child.HasExited.Should().BeTrue();
        }
        finally
        {
            cancellation.Cancel();
            KillChildIfRunning(root.Path);
        }
    }

    private static void KillChildIfRunning(string directory)
    {
        var pidFile = Path.Combine(directory, "pid.txt");
        if (!File.Exists(pidFile))
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile).Trim()));
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
        catch (ArgumentException)
        {
            // The process has already exited.
        }
        catch (InvalidOperationException)
        {
            // The process exited before it could be killed.
        }
    }

    [Fact]
    public async Task TheArgumentsArePassedOneByOne()
    {
        var request = new ProcessRequest(
            OperatingSystem.IsWindows() ? "cmd" : "sh",
            OperatingSystem.IsWindows() ? ["/c", "echo", "a b"] : ["-c", "echo \"$1\"", "sh", "a b"]
        );

        var result = await new ProcessRunner().RunAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.StandardOutput.Should().Contain("a b");
    }

    [Fact]
    public async Task AProgramThatIsNotThereIsAnError()
    {
        var request = new ProcessRequest("abox-does-not-exist", []);

        var action = () => new ProcessRunner().RunAsync(request, CancellationToken.None);

        var exception = await action.Should().ThrowAsync<AboxException>();

        exception.Which.Message.Should().Contain("abox-does-not-exist");
    }
}
