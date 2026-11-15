using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed partial class UserSecretsPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OracleCopyUsesExplicitRuntimeManualPolicyWithoutKitFiles(bool copied)
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        using var fixture = new PolicyFixture(cli.Name);
        fixture.UseAddedRule();
        fixture.AddGlobalRules();
        cli.Configure("""{"userSecrets":["sample"]}""");
        const string json = """
            {"Oracle":"Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=synthetic-service)));User Id=synthetic-user;Password=synthetic-password",
             "Unmapped":"//localhost:5000/synthetic-service","Remote":"//remote.test:1521/synthetic-service"}
            """;
        string source;
        if (copied)
            source = cli.Store("sample", json);
        else
        {
            cli.Root.File(
                "folder/Api/Api.csproj",
                "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
            );
            source = cli.Root.File("folder/Api/appsettings.json", json);
        }
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(call => fixture.ReadAsync(call.Arg<CancellationToken>()));

        // Act
        var exitCode = await cli.Run("--verbose");

        // Assert
        exitCode.Should().Be(0, cli.Console.AllText);
        await cli
            .Sbx.Received(1)
            .WriteUserSecretsAsync(
                cli.Name,
                "sample",
                Arg.Is<string>(written =>
                    written.Contains("(HOST=host.docker.internal)(PORT=1521)")
                    && written.Contains("Password=synthetic-password")
                    && (
                        copied
                            ? written.Contains("//localhost:5000/synthetic-service")
                                && written.Contains("//remote.test:1521/synthetic-service")
                            : !written.Contains("\"Unmapped\"") && !written.Contains("\"Remote\"")
                    )
                ),
                Arg.Any<CancellationToken>()
            );
        File.ReadAllText(source).Should().Be(json);
        cli.Console.AllText.Should()
            .NotContain("synthetic-password")
            .And.NotContain("synthetic-service");
        fixture.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForceCannotBypassManualPolicyEffectiveDenyOrMalformedMetadata(bool malformed)
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        using var fixture = new PolicyFixture(cli.Name);
        fixture.UseAddedRule();
        if (malformed)
            fixture.Rule.Remove("sandbox_id");
        else
            fixture.Check["allowed"] = false;
        cli.Configure("""{"userSecrets":["sample"]}""");
        const string json = """{"Oracle":"//localhost:1521/synthetic-service"}""";
        cli.Store("sample", json);
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(call => fixture.ReadAsync(call.Arg<CancellationToken>()));

        // Act
        var exitCode = await cli.Run("--verbose", "--force");

        // Assert
        exitCode.Should().Be(malformed ? 1 : 0, cli.Console.AllText);
        if (malformed)
        {
            await cli.NoWrites();
            cli.Console.AllText.Should().Contain("sbx policy ls <sandbox> --type network --json");
        }
        else
            await cli
                .Sbx.Received(1)
                .WriteUserSecretsAsync(cli.Name, "sample", json, Arg.Any<CancellationToken>());
        cli.Console.AllText.Should()
            .NotContain("synthetic-service")
            .And.NotContain("Safety override");
    }
}
