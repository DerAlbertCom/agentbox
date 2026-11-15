using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed partial class UserSecretsLifecycleTests
{
    [Theory]
    [InlineData("published-create")]
    [InlineData("local-create")]
    [InlineData("published")]
    [InlineData("local")]
    [InlineData("run")]
    public async Task EveryCreationAndReplacementPathGeneratesCurrentProjectOverridesWithoutAHostFile(
        string entry
    )
    {
        // Arrange
        using var fixture = new Fixture();
        if (!entry.EndsWith("-create", StringComparison.Ordinal))
            fixture.Existing();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample"]}""");
        fixture.Root.File(
            "folder/Api.csproj",
            "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
        );
        fixture.Root.File(
            "folder/appsettings.json",
            """{"Telemetry":"http://localhost:4317/base","WorkerApi":"http://localhost:5000/api"}"""
        );
        fixture.Root.File(
            "folder/appsettings.development.json",
            """{"Telemetry":"http://[::1]:4317/current"}"""
        );
        fixture
            .Sbx.GetUserSecretsHostPortsAsync(fixture.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 4317 });

        // Act
        var exit = entry.EndsWith("-create", StringComparison.Ordinal)
            ? await fixture.Create(entry == "local-create")
            : await fixture.Replace(entry);

        // Assert
        exit.Should().Be(0, fixture.Console.AllText);
        await fixture
            .Sbx.Received(1)
            .WriteUserSecretsAsync(
                fixture.Name,
                "sample",
                """{"Telemetry":"http://host.docker.internal:4317/current"}""",
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("published")]
    [InlineData("local")]
    [InlineData("run")]
    public async Task MalformedProjectConfigurationPreventsDestructiveReplacement(string entry)
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.Existing();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        fixture.Root.File(
            "folder/Api.csproj",
            "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
        );
        fixture.Root.File("folder/appsettings.json", "{\"Url\":\"synthetic-private\", broken}");

        // Act
        var exit = await fixture.Replace(entry);

        // Assert
        exit.Should().Be(1);
        await fixture.NoReplacementOrCopy();
        fixture
            .Console.AllText.Should()
            .Contain("Api.csproj")
            .And.Contain("appsettings.json")
            .And.NotContain("synthetic-private");
    }
}
