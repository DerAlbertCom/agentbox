using Abox.Infrastructure;

namespace Abox.Tests;

public sealed partial class UserSecretsSafetyTests
{
    [Fact]
    public async Task SensitiveAttachedConsoleIsRejectedBeforeTheChildStarts()
    {
        // Arrange
        using var root = new TempDirectory();
        var console = new TestConsole();
        var runner = new ProcessRunner(new ProcessOutput(console) { Verbose = true });
        var request = new ProcessRequest(
            OperatingSystem.IsWindows() ? "cmd" : "sh",
            OperatingSystem.IsWindows()
                ? ["/c", "echo started > started.txt"]
                : ["-c", "touch started.txt"],
            WorkingDirectory: root.Path,
            AttachConsole: true,
            OutputPolicy: ProcessOutputPolicy.Sensitive
        );

        // Act
        var action = () => runner.RunAsync(request, CancellationToken.None);

        // Assert
        await action.Should().ThrowAsync<AboxException>().WithMessage("*suppressed*");
        File.Exists(Path.Combine(root.Path, "started.txt")).Should().BeFalse();
        console.AllText.Should().BeEmpty();
    }

    [Fact]
    public async Task SensitiveStartFailureDoesNotExposeRawExceptionDetailsEvenInVerboseMode()
    {
        // Arrange
        using var root = new TempDirectory();
        var console = new TestConsole();
        var runner = new ProcessRunner(new ProcessOutput(console) { Verbose = true });
        var request = new ProcessRequest(
            Path.Combine(root.Path, "synthetic-private-missing-executable"),
            ["123456789 true"],
            StandardInput: "synthetic-password",
            OutputPolicy: ProcessOutputPolicy.Sensitive
        );

        // Act
        var action = () => runner.RunAsync(request, CancellationToken.None);

        // Assert
        var error = await action.Should().ThrowAsync<AboxException>();
        error
            .Which.ToString()
            .Should()
            .Contain("suppressed")
            .And.NotContain("synthetic-private")
            .And.NotContain(root.Path)
            .And.NotContain("synthetic-password")
            .And.NotContain("123456789 true");
        console.AllText.Should().BeEmpty();
    }
}
