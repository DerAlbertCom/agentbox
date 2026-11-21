using System.Security.Cryptography;
using System.Text;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class UserSecretsTransportTests
{
    [Theory]
    [InlineData("export APPDATA=\n")]
    [InlineData("export APPDATA=relative\n")]
    public async Task InvalidEffectiveAppDataCannotFallBackToHome(string environment)
    {
        // Arrange
        using var sandbox = new ShellSandbox(true) { BeforeReceiver = environment };

        // Act
        var action = () =>
            sandbox.Client.WriteUserSecretsAsync(
                "sandbox",
                "synthetic",
                "{}",
                CancellationToken.None
            );

        // Assert
        await action.Should().ThrowAsync<AboxException>();
        Directory.Exists(sandbox.SecretsRoot).Should().BeFalse();
    }

    [Fact]
    public async Task ALaterInvalidDestinationIsDetectedBeforeAnyDirectoriesAreCreated()
    {
        // Arrange
        using var sandbox = new ShellSandbox(true);
        Directory.CreateDirectory(sandbox.StorePath);

        // Act
        var action = () =>
            sandbox.Client.ValidateUserSecretsDestinationAsync(
                "sandbox",
                ["first", "synthetic"],
                CancellationToken.None
            );

        // Assert
        await action.Should().ThrowAsync<AboxException>();
        Directory.Exists(Path.Combine(sandbox.SecretsRoot, "first")).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReceiverResolvesEffectiveEnvironmentAndReplacesTheWholeStore(bool appData)
    {
        // Arrange
        using var sandbox = new ShellSandbox(appData);

        // Act
        await sandbox.Client.ValidateUserSecretsDestinationAsync(
            "sandbox",
            ["synthetic"],
            CancellationToken.None
        );
        // Assert
        Directory
            .Exists(sandbox.SecretsRoot)
            .Should()
            .BeFalse("destination preflight must not create anything");
        // Arrange
        const string original = "{\"Password\":\"Grüße 🔒\",\"Removed\":\"old\"}";

        // Act
        await sandbox.Client.WriteUserSecretsAsync(
            "sandbox",
            "synthetic",
            original,
            CancellationToken.None
        );
        // Assert
        File.ReadAllBytes(sandbox.StorePath).Should().Equal(Encoding.UTF8.GetBytes(original));

        // Act
        await sandbox.Client.WriteUserSecretsAsync(
            "sandbox",
            "synthetic",
            "{}",
            CancellationToken.None
        );

        // Assert
        File.ReadAllText(sandbox.StorePath).Should().Be("{}");
        Directory.GetFiles(Path.GetDirectoryName(sandbox.StorePath)!).Should().ContainSingle();
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(sandbox.StorePath)
                .Should()
                .Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.GetUnixFileMode(Path.GetDirectoryName(sandbox.StorePath)!)
                .Should()
                .Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Theory]
    [InlineData("short")]
    [InlineData("corrupt")]
    public async Task ShortOrCorruptInputCannotCommitEvenWhenStdinCloses(string damage)
    {
        // Arrange
        using var sandbox = new ShellSandbox(true);
        await sandbox.Client.WriteUserSecretsAsync(
            "sandbox",
            "synthetic",
            "{\"Previous\":true}",
            CancellationToken.None
        );
        sandbox.Frame = frame => damage == "short" ? frame[..^3] : frame[..^1] + "x";

        // Act
        var action = () =>
            sandbox.Client.WriteUserSecretsAsync(
                "sandbox",
                "synthetic",
                "{\"Replacement\":123456789}",
                CancellationToken.None
            );

        // Assert
        await action.Should().ThrowAsync<AboxException>();
        File.ReadAllText(sandbox.StorePath).Should().Be("{\"Previous\":true}");
        Directory.GetFiles(Path.GetDirectoryName(sandbox.StorePath)!).Should().ContainSingle();
    }

    [Fact]
    public async Task CancellationWhileInputIsBlockedDoesNotReplaceThePreviousFile()
    {
        // Arrange
        using var sandbox = new ShellSandbox(true);
        await sandbox.Client.WriteUserSecretsAsync(
            "sandbox",
            "synthetic",
            "{}",
            CancellationToken.None
        );
        sandbox.BeforeReceiver = "sleep 30\n";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        // Act
        var action = () =>
            sandbox.Client.WriteUserSecretsAsync(
                "sandbox",
                "synthetic",
                "{\"Synthetic\":\"" + new string('x', 8 * 1024 * 1024) + "\"}",
                cancellation.Token
            );

        // Assert
        await action.Should().ThrowAsync<OperationCanceledException>();
        File.ReadAllText(sandbox.StorePath).Should().Be("{}");
    }

    [Fact]
    public async Task NonregularDestinationIsRejectedWithoutReplacingIt()
    {
        // Arrange
        using var sandbox = new ShellSandbox(true);
        Directory.CreateDirectory(sandbox.StorePath);

        // Act
        var action = () =>
            sandbox.Client.WriteUserSecretsAsync(
                "sandbox",
                "synthetic",
                "{}",
                CancellationToken.None
            );

        // Assert
        await action.Should().ThrowAsync<AboxException>();
        Directory.Exists(sandbox.StorePath).Should().BeTrue();
    }

    [Theory]
    [InlineData("exit")]
    [InlineData("exception")]
    public async Task RawFailuresIncludingScalarFragmentsNeverEscapeTheSbxAdapter(string failure)
    {
        // Arrange
        using var root = new TempDirectory();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProcessResult>>(_ =>
                failure == "exit"
                    ? Task.FromResult(new ProcessResult(9, "123456789 true", "synthetic-password"))
                    : Task.FromException<ProcessResult>(
                        new AboxException("synthetic-password 123456789 true")
                    )
            );
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));

        // Act
        var action = () =>
            client.WriteUserSecretsAsync("sandbox", "synthetic", "{}", CancellationToken.None);

        // Assert
        var error = await action.Should().ThrowAsync<AboxException>();
        error
            .Which.ToString()
            .Should()
            .NotContain("synthetic-password")
            .And.NotContain("123456789 true");
    }

    [Fact]
    public async Task PayloadAndFrameUseOnlySensitiveNoninteractiveStdinWithUtf8ByteLength()
    {
        // Arrange
        using var root = new TempDirectory();
        const string payload = "{\"Password\":\"Grüße 🔒\"}";
        ProcessRequest? captured = null;
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<ProcessRequest>();
                return new ProcessResult(0, "must not escape", "must not escape");
            });
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));

        // Act
        await client.WriteUserSecretsAsync("sandbox", "synthetic", payload, CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.FileName.Should().Be("sbx");
        captured.Arguments.Take(2).Should().Equal("exec", "-i");
        captured.Arguments.Should().NotContain("-t").And.NotContain("-u");
        string.Join(" ", captured.Arguments)
            .Should()
            .NotContain("Grüße")
            .And.NotContain("Password");
        captured.AttachConsole.Should().BeFalse();
        captured.OutputPolicy.Should().Be(ProcessOutputPolicy.Sensitive);
        captured
            .StandardInput.Should()
            .Be(
                $"{Encoding.UTF8.GetByteCount(payload)}\n{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))}\n{payload}"
            );
        captured.ToString().Should().NotContain("Grüße");
    }
}
