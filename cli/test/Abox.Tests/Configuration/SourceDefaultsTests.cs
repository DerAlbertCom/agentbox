using Abox.Cli;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class SourceDefaultsTests
{
    [Theory]
    [InlineData("build")]
    [InlineData("publish")]
    [InlineData("create")]
    [InlineData("secrets", "list", "--squad", "ac")]
    [InlineData("secrets", "set", "--squad", "ac")]
    [InlineData("secrets", "remove", "--squad", "ac")]
    public async Task MissingTeamsHaveTheSameDiagnosticWithoutRequiringExternalTools(
        params string[] arguments
    )
    {
        // Arrange
        using var root = new TempDirectory();
        var console = new TestConsole();
        var programs = Substitute.For<IHostPrograms>();
        var runner = Substitute.For<IProcessRunner>();
        var sbx = Substitute.For<ISbxClient>();
        var services = new ServiceCollection().AddAbox(console, new TrailingArguments());
        services.AddSingleton(AboxPaths.For(root.Path, root.Path));
        services.AddSingleton(new AboxConfig());
        services.AddSingleton(programs);
        services.AddSingleton(runner);
        services.AddSingleton(sbx);
        using var provider = services.BuildServiceProvider();

        // Act
        var exitCode = await provider.GetRequiredService<AboxRootCommand>().InvokeAsync(arguments);

        // Assert
        exitCode.Should().Be(1);
        console
            .Errors.Should()
            .Equal(
                "error: No team is given. Give --team <name> or set 'defaultTeam' in the user configuration."
            );
        programs.ReceivedCalls().Should().BeEmpty();
        runner.ReceivedCalls().Should().BeEmpty();
        sbx.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("create", "--help")]
    [InlineData("publish", "--help")]
    [InlineData("secrets", "set", "--help")]
    [InlineData("secrets", "remove", "--help")]
    public async Task SourceCommandHelpDoesNotLoadInvalidSettings(params string[] arguments)
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("config.yaml", "unknown-key: invalid\n");
        var console = new TestConsole();
        var services = new ServiceCollection().AddAbox(console, new TrailingArguments());
        services.AddSingleton(AboxPaths.For(root.Path, root.Path));
        using var provider = services.BuildServiceProvider();

        // Act
        var exitCode = await provider.GetRequiredService<AboxRootCommand>().InvokeAsync(arguments);

        // Assert
        exitCode.Should().Be(0);
        console.Errors.Should().BeEmpty();
    }
}
