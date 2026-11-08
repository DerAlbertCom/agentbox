using Abox.Infrastructure;

namespace Abox.Tests;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task TheStandardInputOfTheChildIsWritten()
    {
        var request = new ProcessRequest(
            OperatingSystem.IsWindows() ? "cmd" : "sh",
            OperatingSystem.IsWindows() ? ["/c", "sort"] : ["-c", "cat"],
            StandardInput: "the value"
        );

        var result = await new ProcessRunner().RunAsync(request, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("the value", result.StandardOutput);
    }

    [Fact]
    public async Task TheArgumentsArePassedOneByOne()
    {
        var request = new ProcessRequest(
            OperatingSystem.IsWindows() ? "cmd" : "sh",
            OperatingSystem.IsWindows() ? ["/c", "echo", "a b"] : ["-c", "echo \"$1\"", "sh", "a b"]
        );

        var result = await new ProcessRunner().RunAsync(request, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("a b", result.StandardOutput);
    }

    [Fact]
    public async Task AProgramThatIsNotThereIsAnError()
    {
        var request = new ProcessRequest("abox-does-not-exist", []);

        var exception = await Assert.ThrowsAsync<AboxException>(() =>
            new ProcessRunner().RunAsync(request, CancellationToken.None)
        );

        Assert.Contains("abox-does-not-exist", exception.Message);
    }
}
