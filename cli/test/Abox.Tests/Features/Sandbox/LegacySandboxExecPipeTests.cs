using Abox.Cli;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class LegacySandboxExecPipeTests
{
    [Theory]
    [InlineData("exec")]
    [InlineData("pipe")]
    public async Task ExplicitNamesRemainExactAndNeverDiscoverAnotherSandbox(string command)
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        cli.Provider.GetRequiredService<TrailingArguments>().Values = ["echo", "hello"];
        cli.Sbx.ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>()).Returns(23);

        // Act
        var exitCode = await cli.Invoke(command, LegacySandboxFixture.Name);

        // Assert
        exitCode.Should().Be(23);
        await cli
            .Sbx.Received(1)
            .ExecAsync(
                Arg.Is<SbxExecRequest>(request =>
                    request.Sandbox == LegacySandboxFixture.Name
                    && request.KeepStandardInputOpen == (command == "pipe")
                ),
                Arg.Any<CancellationToken>()
            );
        await cli
            .Sbx.DidNotReceive()
            .FindSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await cli.Sbx.DidNotReceive().ListSandboxesAsync(Arg.Any<CancellationToken>());
    }
}
