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
