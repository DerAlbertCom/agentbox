using System.Text;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

[CollectionDefinition("Console input encoding", DisableParallelization = true)]
public sealed class ConsoleInputEncodingCollection;

[Collection("Console input encoding")]
public sealed class SbxSecretInputTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 19)]
    [InlineData(true, 19)]
    public async Task HostSecretInputIsExactUtf8DespiteALegacyConsoleCodePage(
        bool custom,
        int exitCode
    )
    {
        // Arrange
        using var root = new TempDirectory();
        const string value = "x-ü€漢🔒-y";
        byte[] expected =
        [
            0x78,
            0x2D,
            0xC3,
            0xBC,
            0xE2,
            0x82,
            0xAC,
            0xE6,
            0xBC,
            0xA2,
            0xF0,
            0x9F,
            0x94,
            0x92,
            0x2D,
            0x79,
        ];
        var console = new TestConsole();
        var processRunner = new ProcessRunner(new ProcessOutput(console) { Verbose = true });
        var runner = Substitute.For<IProcessRunner>();
        ProcessRequest? emitted = null;
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                emitted = call.Arg<ProcessRequest>();
                return processRunner.RunAsync(
                    emitted with
                    {
                        FileName = OperatingSystem.IsWindows() ? "powershell" : "sh",
                        Arguments = OperatingSystem.IsWindows()
                            ?
                            [
                                "-NoProfile",
                                "-NonInteractive",
                                "-Command",
                                "$file = [IO.File]::Create('received.bin'); [Console]::OpenStandardInput().CopyTo($file); $file.Dispose(); [Console]::Out.WriteLine('stored'); [Console]::Error.WriteLine('warning'); exit "
                                    + exitCode,
                            ]
                            :
                            [
                                "-c",
                                $"cat > received.bin; printf 'stored\\n'; printf 'warning\\n' >&2; exit {exitCode}",
                            ],
                        WorkingDirectory = root.Path,
                    },
                    call.Arg<CancellationToken>()
                );
            });
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var previousEncoding = Console.InputEncoding;
        try
        {
            Console.InputEncoding = CodePagesEncodingProvider.Instance.GetEncoding(850)!;

            // Act
            var outcome = custom
                ? await client.SetCustomSecretAsync(
                    "example.test",
                    "SYNTHETIC_TOKEN",
                    value,
                    cancellation.Token
                )
                : await client.SetSecretAsync("synthetic", value, cancellation.Token);

            // Assert
            File.ReadAllBytes(Path.Combine(root.Path, "received.bin")).Should().Equal(expected);
            outcome.ExitCode.Should().Be(exitCode);
            outcome.Output.Should().Be($"stored{Environment.NewLine}warning{Environment.NewLine}");
            console.AllText.Should().Contain("stored").And.Contain("warning").And.NotContain(value);
            emitted.Should().NotBeNull();
            emitted!.FileName.Should().Be("sbx");
            emitted
                .Arguments.Should()
                .Equal(
                    custom
                        ?
                        [
                            "secret",
                            "set-custom",
                            "--host",
                            "example.test",
                            "--env",
                            "SYNTHETIC_TOKEN",
                            "--value-stdin",
                        ]
                        : ["secret", "set", "synthetic"]
                );
            emitted.Environment.Should().BeNull();
            emitted.ToString().Should().NotContain(value);
            await runner.Received(1).RunAsync(Arg.Any<ProcessRequest>(), cancellation.Token);
        }
        finally
        {
            Console.InputEncoding = previousEncoding;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostSecretSettersPreserveCancellation(bool custom)
    {
        // Arrange
        using var root = new TempDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), cancellation.Token)
            .Returns(Task.FromCanceled<ProcessResult>(cancellation.Token));
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));

        // Act
        var action = () =>
            custom
                ? client.SetCustomSecretAsync(
                    "example.test",
                    "SYNTHETIC_TOKEN",
                    "synthetic-ü€",
                    cancellation.Token
                )
                : client.SetSecretAsync("synthetic", "synthetic-ü€", cancellation.Token);

        // Assert
        var exception = await action.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cancellation.Token);
    }
}
