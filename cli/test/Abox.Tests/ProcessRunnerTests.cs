using Abox.Infrastructure;

namespace Abox.Tests;

public sealed class ProcessRunnerTests
{
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
