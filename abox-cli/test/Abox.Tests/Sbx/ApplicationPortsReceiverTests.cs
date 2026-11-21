using Abox.Infrastructure;
using Abox.Sbx;

namespace Abox.Tests;

public sealed class ApplicationPortsReceiverTests
{
    [Fact]
    public async Task EmbeddedReceiverPreservesOwnedStartupRecoveryAndBoundedTcpTransport()
    {
        // Arrange: only disposable HOME directories and their fixture-owned children.
        RequireSupportedReceiverFixture();
        using var root = new TempDirectory();
        using var source = new StreamReader(
            typeof(SbxClient).Assembly.GetManifestResourceStream("Abox.ApplicationPortsRelay")!
        );
        var receiver = root.File("embedded receiver.py", await source.ReadToEndAsync());
        var checks = SyntheticTestResources.File("Verification/ApplicationPortsReceiverChecks.py");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        // Act: execute the shipped bytes, not a separately imported module or helper.
        var result = await new ProcessRunner().RunAsync(
            new ProcessRequest(
                "/usr/bin/python3",
                ["-I", checks],
                WorkingDirectory: root.Path,
                Environment: new Dictionary<string, string>
                {
                    ["ABOX_APPLICATION_PORTS_RECEIVER"] = receiver,
                }
            ),
            timeout.Token
        );

        // Assert
        result.ExitCode.Should().Be(0, result.StandardError);
        result.StandardError.Should().Contain("OK");
    }

    private static void RequireSupportedReceiverFixture()
    {
        if (
            !OperatingSystem.IsLinux()
            || Environment.UserName == "root"
            || !File.Exists("/usr/bin/python3")
        )
            throw new InvalidOperationException(
                Xunit.v3.DynamicSkipToken.Value + "Requires Linux, non-root and Python 3.8+."
            );
    }
}
