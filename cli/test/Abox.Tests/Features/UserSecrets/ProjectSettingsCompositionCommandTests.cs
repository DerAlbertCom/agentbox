using Abox.Infrastructure;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class ProjectSettingsCompositionCommandTests
{
    [Fact]
    public async Task ProjectOnlySplitGenerationUsesMergedUnselectedMetadataWithoutSelectingOtherStores()
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":[{"id":"sample","keys":["database_config:host"]}]}""");
        ProjectOnlySplitSources(cli);
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });
        var processes = Substitute.For<IProcessRunner>();
        cli.Services.AddSingleton(processes);

        // Act
        var exit = await cli.Run("--verbose");

        // Assert
        exit.Should().Be(0, cli.Console.AllText);
        await cli
            .Sbx.Received(1)
            .WriteUserSecretsAsync(
                cli.Name,
                "sample",
                """{"DATABASE_CONFIG:HOST":"host.docker.internal"}""",
                Arg.Any<CancellationToken>()
            );
        cli.Sbx.ReceivedCalls()
            .Count(call => call.GetMethodInfo().Name == nameof(ISbxClient.WriteUserSecretsAsync))
            .Should()
            .Be(1);
        processes.ReceivedCalls().Should().BeEmpty();
        cli.Console.AllText.Should().NotContain("synthetic-private").And.NotContain("Other.csproj");
    }

    private static void ProjectOnlySplitSources(UserSecretsCliHarness cli)
    {
        cli.Root.File(
            "folder/Api/Api.csproj",
            """<Project><PropertyGroup><UserSecretsId> SAMPLE </UserSecretsId></PropertyGroup><Import Project="DoNotRead.props" /></Project>"""
        );
        cli.Root.File("folder/Api/DoNotRead.props", "not XML: imports are never evaluated");
        cli.Root.File(
            "folder/Api/appsettings.json",
            """{"Database_Config":{"Host":"remote.test","Port":5000},"Excluded":"http://localhost:8004/synthetic-private"}"""
        );
        cli.Root.File(
            "folder/Api/appsettings.development.json",
            """{"DATABASE_CONFIG:HOST":"localhost","database_config:PORT":"08004"}"""
        );
        cli.Root.File(
            "folder/Other/Other.csproj",
            "<Project><PropertyGroup><UserSecretsId>other</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File("folder/Other/appsettings.json", "not JSON: never selected");
    }

    [Theory]
    [InlineData("outside", 1)]
    [InlineData("folder/src", 0)]
    public async Task EscapingDirectoryLinkCannotSupplyTheMissingHostStoresProjectSource(
        string source,
        int expectedExit
    )
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        var link = await LinkProjectSource(cli, source);
        try
        {
            // Act
            var exit = await cli.Run("--verbose", "--force");

            // Assert
            exit.Should().Be(expectedExit, cli.Console.AllText);
            if (expectedExit == 0)
                await cli
                    .Sbx.Received(1)
                    .WriteUserSecretsAsync(cli.Name, "sample", "{}", Arg.Any<CancellationToken>());
            else
            {
                await cli.NoWrites();
                cli.Sbx.ReceivedCalls()
                    .Should()
                    .BeEmpty("association failed before runtime access");
                cli.Console.AllText.Should().Contain("sample").And.Contain("appsettings");
            }
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    private static async Task<string> LinkProjectSource(UserSecretsCliHarness cli, string source)
    {
        cli.Root.File(
            source + "/Api.csproj",
            "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File(source + "/appsettings.json", "{}");
        var link = Path.Combine(cli.Location, "alias");
        var result = await DirectoryLinks.CreateAsync(link, Path.Combine(cli.Root.Path, source));
        if (result is not null)
            result.ExitCode.Should().Be(0, result.StandardError);
        return link;
    }
}
