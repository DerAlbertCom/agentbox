using System.Text.Json;
using NSubstitute;

namespace Abox.Tests;

public sealed class UserSecretsEndpointCommandTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedAndGeneratedEndpointsShareSamePortAdaptationWithoutChangingOtherText(
        bool generated
    )
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        const string source = """
            {
              "Url":"HTTP://localhost:8004/synthetic-private?q=localhost:8004#fragment",
              "Sql":"Server = 'tcp:127.0.0.1,8004'; Password = 'synthetic;password'",
              "Postgres":"Host=[::1];Port=8004;Password=synthetic-password",
              "Oracle":"Data Source=//localhost:8004/synthetic-service;Password=synthetic-password",
              "Descriptor":"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=::1)(PORT=8004))(CONNECT_DATA=(SID=synthetic-service)))",
              "Database_Config:Host":"localhost",
              "Database_Config:Port":"08004"
            }
            """;
        cli.Root.File(
            "folder/Api/Api.csproj",
            "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
        );
        var path = generated
            ? cli.Root.File("folder/Api/appsettings.json", source)
            : cli.Store("sample", source);
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });

        // Act
        var exit = await cli.Run("--verbose");

        // Assert
        exit.Should().Be(0, cli.Console.AllText);
        var write = cli
            .Sbx.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == "WriteUserSecretsAsync");
        using var written = JsonDocument.Parse((string)write.GetArguments()[2]!);
        var values = written.RootElement;
        values
            .GetProperty("Url")
            .GetString()
            .Should()
            .Be("HTTP://host.docker.internal:8004/synthetic-private?q=localhost:8004#fragment");
        values
            .GetProperty("Sql")
            .GetString()
            .Should()
            .Be("Server = 'tcp:host.docker.internal,8004'; Password = 'synthetic;password'");
        values
            .GetProperty("Postgres")
            .GetString()
            .Should()
            .Be("Host=host.docker.internal;Port=8004;Password=synthetic-password");
        values
            .GetProperty("Oracle")
            .GetString()
            .Should()
            .Be(
                "Data Source=//host.docker.internal:8004/synthetic-service;Password=synthetic-password"
            );
        values
            .GetProperty("Descriptor")
            .GetString()
            .Should()
            .Be(
                "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=host.docker.internal)(PORT=8004))(CONNECT_DATA=(SID=synthetic-service)))"
            );
        values.GetProperty("Database_Config:Host").GetString().Should().Be("host.docker.internal");
        if (generated)
            values.TryGetProperty("Database_Config:Port", out _).Should().BeFalse();
        else
            values.GetProperty("Database_Config:Port").GetString().Should().Be("08004");
        (await File.ReadAllTextAsync(path)).Should().Be(source);
        cli.Console.AllText.Should()
            .Contain("6 keys")
            .And.NotContain("synthetic-private")
            .And.NotContain("synthetic-service")
            .And.NotContain("synthetic-password")
            .And.NotContain("synthetic;password");
    }
}
