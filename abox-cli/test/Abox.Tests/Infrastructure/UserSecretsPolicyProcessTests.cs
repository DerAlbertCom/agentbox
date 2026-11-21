using Abox.Infrastructure;

namespace Abox.Tests;

public sealed class UserSecretsPolicyProcessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PolicyCaptureRetainsMetadataForParsingWithoutPrintingRawStreams(bool verbose)
    {
        // Arrange
        var console = new TestConsole();
        var output = new ProcessOutput(console) { Verbose = verbose };
        var request = new ProcessRequest(
            OperatingSystem.IsWindows() ? "powershell" : "sh",
            OperatingSystem.IsWindows()
                ?
                [
                    "-NoProfile",
                    "-NonInteractive",
                    "-Command",
                    "[Console]::Out.WriteLine('{\"rules\":[]}'); [Console]::Error.WriteLine('synthetic-password 123456789 true'); exit 9",
                ]
                :
                [
                    "-c",
                    "printf '%s\\n' '{\"rules\":[]}'; printf 'synthetic-password 123456789 true' >&2; exit 9",
                ],
            OutputPolicy: ProcessOutputPolicy.CapturedSensitive
        );

        // Act
        var result = await new ProcessRunner(output).RunAsync(request, CancellationToken.None);

        // Assert
        result.ExitCode.Should().Be(9);
        result.StandardOutput.Trim().Should().Be("""{"rules":[]}""");
        result.StandardError.Should().Contain("synthetic-password 123456789 true");
        console.AllText.Should().BeEmpty();
    }

    [Fact]
    public async Task PolicyCaptureCannotAttachConsoleOrLeakStartFailures()
    {
        // Arrange
        var runner = new ProcessRunner();

        // Act
        var attached = () =>
            runner.RunAsync(
                new ProcessRequest(
                    "synthetic-password-123456789",
                    [],
                    AttachConsole: true,
                    OutputPolicy: ProcessOutputPolicy.CapturedSensitive
                ),
                CancellationToken.None
            );
        var missing = () =>
            runner.RunAsync(
                new ProcessRequest(
                    "synthetic-password-123456789",
                    [],
                    OutputPolicy: ProcessOutputPolicy.CapturedSensitive
                ),
                CancellationToken.None
            );

        // Assert
        (await attached.Should().ThrowAsync<AboxException>())
            .Which.ToString()
            .Should()
            .NotContain("synthetic-password");
        (await missing.Should().ThrowAsync<AboxException>())
            .Which.ToString()
            .Should()
            .NotContain("synthetic-password");
    }
}
