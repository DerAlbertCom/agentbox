using Abox.Infrastructure;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class AppPortDiscoveryTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task OversizedProjectIsIgnoredOnlyWhenReadBytesEstablishItIsUnrelated(
        bool explicitAspNet,
        int expectedExit
    )
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        AddLaunchPorts(fixture);
        fixture.Root.File(
            "folder/App.csproj",
            (explicitAspNet ? "<Project Sdk=\"Microsoft.NET.Sdk.Web\">" : "<Project>")
                + new string(' ', 1024 * 1024)
        );

        // Act
        var exit = await fixture.Invoke("ports");

        // Assert
        exit.Should().Be(expectedExit, fixture.Console.AllText);
        if (explicitAspNet)
        {
            fixture.Console.AllText.Should().Contain("1 MiB").And.Contain("App.csproj");
            await NoApplicationSetup(fixture);
        }
        else
            fixture.Console.AllText.Should().Contain("| 8203 | 18203");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    public async Task JsonFileSizeBoundaryIsMeasuredInBytesBeforeAnyPublishing(
        int excessBytes,
        int expectedExit
    )
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        AddLaunchPorts(fixture);
        fixture.Root.File(
            "folder/package.json",
            "{}" + new string(' ', 1024 * 1024 - 2 + excessBytes)
        );

        // Act
        var exit = await fixture.Invoke("ports");

        // Assert
        exit.Should().Be(expectedExit, fixture.Console.AllText);
        if (excessBytes == 0)
            fixture.Console.AllText.Should().Contain("| 8203 | 18203");
        else
        {
            fixture.Console.AllText.Should().Contain("1 MiB").And.Contain("package.json");
            await NoApplicationSetup(fixture);
        }
    }

    [Fact]
    public async Task MixedFormatsRetainOrdinalSourceOrderAndDeclarationOrderWithoutRunningTools()
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        var runner = Substitute.For<IProcessRunner>();
        fixture.Services.AddSingleton(runner);
        fixture.Root.File(
            "folder/z/package.json",
            """{"scripts":{"start":"ng serve --port 4300"}}"""
        );
        fixture.Root.File(
            "folder/a/Properties/launchSettings.json",
            """{"profiles":{"second":{"applicationUrl":"http://localhost:8204;http://localhost:8203"},"first":{"applicationUrl":"http://localhost:8204"}}}"""
        );
        fixture.Root.File(
            "folder/b/angular.json",
            """{"projects":{"ui":{"architect":{"serve":{"options":{"port":4200}}}}}}"""
        );

        // Act
        var exit = await fixture.Invoke("ports");

        // Assert
        exit.Should().Be(0, fixture.Console.AllText);
        fixture
            .Console.Output.Where(line => line.StartsWith('"'))
            .Select(line => line.Replace(@"\\", "/"))
            .Should()
            .Equal(
                "\"a/Properties/launchSettings.json\" (\"second\") | 8204 | 18204",
                "\"a/Properties/launchSettings.json\" (\"second\") | 8203 | 18203",
                "\"a/Properties/launchSettings.json\" (\"first\") | 8204 | 18204",
                "\"b/angular.json\" (\"ui.architect.serve.options\") | 4200 | 14200",
                "\"z/package.json\" (\"scripts.start\") | 4300 | 14300"
            );
        await runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk.Web\"><broken>")]
    [InlineData(
        "<!DOCTYPE Project [<!ENTITY x SYSTEM 'file:///never-read'>]><Project Sdk=\"Microsoft.NET.Sdk.Web\" />"
    )]
    [InlineData(
        "<Project><ItemGroup><FrameworkReference Include=\"Microsoft.AspNetCore.App\" /><broken>"
    )]
    public async Task MalformedExplicitAspNetProjectFailsClosed(string project)
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        AddLaunchPorts(fixture);
        fixture.Root.File("folder/Web.csproj", project);

        // Act
        var exit = await fixture.Invoke("ports");

        // Assert
        exit.Should().Be(1, fixture.Console.AllText);
        fixture
            .Console.AllText.Should()
            .Contain("Web.csproj")
            .And.Contain("readable ordinary project XML")
            .And.NotContain("never-read")
            .And.NotContain("Application ports published");
        await NoApplicationSetup(fixture);
    }

    [Theory]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk\"><broken>")]
    [InlineData(
        "<!DOCTYPE Project [<!ENTITY x SYSTEM 'file:///never-read'>]><Project Sdk=\"Microsoft.NET.Sdk\" />"
    )]
    public async Task UnsupportedUnrelatedProjectXmlDoesNotBlockValidPortDiscovery(string unrelated)
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        fixture.Root.File("folder/Unrelated.csproj", unrelated);
        AddLaunchPorts(fixture);

        // Act
        var exit = await fixture.Invoke("ports");

        // Assert
        exit.Should().Be(0, fixture.Console.AllText);
        fixture.Console.AllText.Should().Contain("| 8203 | 18203");
        await fixture
            .Sbx.Received(1)
            .EnsureApplicationPortsAsync(
                fixture.Name,
                Arg.Is<IReadOnlyList<ApplicationPortMapping>>(ports =>
                    ports.Count == 1 && ports[0] == new ApplicationPortMapping(8203, 18203)
                ),
                Arg.Any<CancellationToken>()
            );
        await fixture
            .Sbx.DidNotReceive()
            .GetDevelopmentCertificatePasswordAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    private static void AddLaunchPorts(UserSecretsCliHarness fixture) =>
        fixture.Root.File(
            "folder/Properties/launchSettings.json",
            """{"profiles":{"app":{"applicationUrl":"http://localhost:8203"}}}"""
        );

    private static async Task NoApplicationSetup(UserSecretsCliHarness fixture)
    {
        await fixture
            .Sbx.DidNotReceive()
            .EnsureApplicationPortsAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<ApplicationPortMapping>>(),
                Arg.Any<CancellationToken>()
            );
        await fixture
            .Sbx.DidNotReceive()
            .GetDevelopmentCertificatePasswordAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }
}
