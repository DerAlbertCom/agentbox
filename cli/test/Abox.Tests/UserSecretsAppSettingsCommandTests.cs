using System.Text;
using System.Text.Json;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class UserSecretsAppSettingsCommandTests
{
    [Fact]
    public async Task LiteralUnselectedConsumersDoNotBlockGenerationDespiteImplicitWorkspaceImports()
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        cli.Root.File(
            "folder/Directory.Build.props",
            "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File(
            "folder/Api/Api.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File(
            "folder/Api/appsettings.json",
            """{"Endpoint":"http://localhost:8004/path"}"""
        );
        cli.Root.File(
            "folder/Worker/Worker.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><UserSecretsId>unselected</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File("folder/Worker/appsettings.json", "not JSON: definitely unselected consumer");
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });

        (await cli.Run("--verbose")).Should().Be(0, cli.Console.AllText);

        Written(cli, "sample")
            .Should()
            .Be("""{"Endpoint":"http://host.docker.internal:8004/path"}""");
        cli.Console.AllText.Should().NotContain("Warning:").And.NotContain("Worker.csproj");
    }

    [Theory]
    [InlineData("implicit-props", false)]
    [InlineData("implicit-props", true)]
    [InlineData("implicit-targets", false)]
    [InlineData("implicit-targets", true)]
    [InlineData("computed", false)]
    [InlineData("computed", true)]
    [InlineData("imported", false)]
    [InlineData("imported", true)]
    [InlineData("conditional", false)]
    [InlineData("conditional", true)]
    [InlineData("multiple", false)]
    [InlineData("multiple", true)]
    public async Task PotentialSelectedConsumersBlockGenerationButPermitAuthoritativeCopies(
        string association,
        bool authoritative
    )
    {
        foreach (var verbose in new[] { false, true })
        {
            using var cli = new UserSecretsCliHarness();
            cli.Configure("""{"userSecrets":["first","sample"]}""");
            cli.Store("first", "{}");
            cli.Store(
                "sample",
                authoritative
                    ? """{"Endpoint":"http://localhost:8004/authoritative"}"""
                    : """{"Copied":"synthetic-only"}"""
            );
            cli.Root.File(
                "folder/Api/Api.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
            );
            cli.Root.File(
                "folder/Api/appsettings.json",
                """{"Endpoint":"http://localhost:8004/generated?token=synthetic-token"}"""
            );
            cli.Root.File(
                "folder/Worker/Worker.csproj",
                association switch
                {
                    "implicit-props" or "implicit-targets" =>
                        "<Project Sdk=\"Microsoft.NET.Sdk\" />",
                    "computed" =>
                        "<Project><PropertyGroup><UserSecretsId>$(SyntheticPrivateId)</UserSecretsId></PropertyGroup></Project>",
                    "imported" =>
                        "<Project><Import Project=\"SyntheticPrivateId.props\" /></Project>",
                    "conditional" =>
                        "<Project><PropertyGroup Condition=\"'$(Configuration)' == 'Debug'\"><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>",
                    _ =>
                        "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId><UserSecretsId>other</UserSecretsId></PropertyGroup></Project>",
                }
            );
            cli.Root.File("folder/Worker/appsettings.json", "not JSON: never guess an association");
            if (association is "implicit-props" or "implicit-targets")
                cli.Root.File(
                    "folder/Directory.Build."
                        + (association == "implicit-props" ? "props" : "targets"),
                    "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup><Target Name=\"SyntheticPrivateId\"><Error Text=\"Do not run\" /></Target></Project>"
                );
            var originals = Directory
                .EnumerateFiles(cli.Location, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.ReadAllText);
            cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
                .Returns(new[] { 8004 });
            var processes = Substitute.For<IProcessRunner>();
            cli.Services.AddSingleton(processes);

            (await cli.Run(["--force", .. verbose ? new[] { "--verbose" } : []]))
                .Should()
                .Be(authoritative ? 0 : 1, cli.Console.AllText);

            cli.Console.AllText.Should()
                .Contain("Warning:")
                .And.Contain("Worker.csproj")
                .And.Contain("UserSecretsId")
                .And.NotContain("SyntheticPrivateId")
                .And.NotContain("synthetic-token")
                .And.NotContain("/generated")
                .And.NotContain("/authoritative");
            if (authoritative)
                Written(cli, "sample")
                    .Should()
                    .Be("""{"Endpoint":"http://host.docker.internal:8004/authoritative"}""");
            else
            {
                await cli.NoWrites();
                cli.Console.AllText.Should()
                    .Contain("sample")
                    .And.Contain("Endpoint")
                    .And.Contain("consumer")
                    .And.Contain("association");
            }
            foreach (var (path, original) in originals)
                File.ReadAllText(path).Should().Be(original);
            processes.ReceivedCalls().Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ImplicitImportNamesFollowExistingHostPathComparison()
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        cli.Store("sample", "{}");
        cli.Root.File("folder/directory.build.props", "not XML: import contents are never read");
        cli.Root.File("folder/Api.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");

        (await cli.Run("--verbose")).Should().Be(0, cli.Console.AllText);

        Written(cli, "sample").Should().Be("{}");
        cli.Console.AllText.Contains("Warning:").Should().Be(OperatingSystem.IsWindows());
    }

    [Fact]
    public async Task ImplicitImportDiagnosticsNeverSearchAboveTheSelectedWorkspace()
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        cli.Store("sample", "{}");
        cli.Root.File("Directory.Build.props", "not XML: outside the selected workspace");
        cli.Root.File("folder/src/Api.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");

        (await cli.Run("--verbose")).Should().Be(0, cli.Console.AllText);

        Written(cli, "sample").Should().Be("{}");
        cli.Console.AllText.Should().NotContain("Warning:").And.NotContain("Api.csproj");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownProjectConsumersCannotBypassSelectedHostOrAssociatedAppsettingsValidation(
        bool invalidAppsettings
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["first",{"id":"sample","keys":[]}]}""");
        cli.Store("first", "{}");
        cli.Store("sample", invalidAppsettings ? "{}" : "not JSON: SyntheticPrivateValue");
        cli.Root.File("folder/Templates/Broken.csproj", "not XML: unknown consumers");
        if (invalidAppsettings)
        {
            cli.Root.File(
                "folder/Api/Api.csproj",
                "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
            );
            cli.Root.File("folder/Api/appsettings.json", "not JSON: SyntheticPrivateValue");
        }

        (await cli.Run("--verbose", "--force")).Should().Be(1);

        await cli.NoWrites();
        cli.Console.AllText.Should()
            .Contain("sample")
            .And.Contain("JSON")
            .And.NotContain("SyntheticPrivateValue");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkedProjectFilesAreNeverReadAndDoNotPreventValidatedCopyOnlyStores(
        bool generate
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        cli.Store("sample", """{"Copied":"synthetic-only"}""");
        var target = cli.Root.File("Outside.csproj", "not XML: SyntheticPrivateValue");
        var link = Path.Combine(cli.Location, "Linked.csproj");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception exception)
            when (exception is UnauthorizedAccessException or PlatformNotSupportedException
                || (
                    OperatingSystem.IsWindows()
                    && exception is IOException
                    && (exception.HResult & 0xffff) == 1314
                )
            )
        {
            throw new InvalidOperationException(
                Xunit.v3.DynamicSkipToken.Value
                    + "File symlink tests require native link privileges on this host.",
                exception
            );
        }
        if (generate)
        {
            cli.Root.File(
                "folder/Api/Api.csproj",
                "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
            );
            cli.Root.File(
                "folder/Api/appsettings.json",
                """{"Endpoint":"http://localhost:8004/path"}"""
            );
            cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
                .Returns(new[] { 8004 });
        }

        (await cli.Run("--verbose")).Should().Be(generate ? 1 : 0, cli.Console.AllText);

        cli.Console.AllText.Should()
            .Contain("Linked.csproj")
            .And.Contain("ordinary file")
            .And.NotContain("SyntheticPrivateValue");
        if (generate)
            await cli.NoWrites();
        else
            Written(cli, "sample").Should().Be("""{"Copied":"synthetic-only"}""");
    }

    [Fact]
    public async Task GeneratedParentScalarsAndCopiedChildKeysAreDistinctInTheDotNetJsonProvider()
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        var host = cli.Store("sample", """{"Api":{"Key":"synthetic-only"}}""");
        cli.Root.File(
            "folder/Api.csproj",
            "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File("folder/appsettings.json", """{"Api":"http://localhost:5000"}""");
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 5000 });

        (await cli.Run()).Should().Be(0, cli.Console.AllText);

        var payload = Written(cli, "sample");
        payload
            .Should()
            .Be("""{"Api":{"Key":"synthetic-only"},"Api":"http://host.docker.internal:5000"}""");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        using var configuration = (ConfigurationRoot)
            new ConfigurationBuilder().AddJsonStream(stream).Build();
        configuration["Api"].Should().Be("http://host.docker.internal:5000");
        configuration["Api:Key"].Should().Be("synthetic-only");
        configuration.AsEnumerable().Should().HaveCount(2);
        File.ReadAllText(host).Should().Be("""{"Api":{"Key":"synthetic-only"}}""");
    }

    [Theory]
    [InlineData(
        "<PropertyGroup Condition=\"'$(Configuration)' == 'Debug'\"><UserSecretsId>unselected</UserSecretsId></PropertyGroup>"
    )]
    [InlineData(
        "<PropertyGroup><UserSecretsId>other</UserSecretsId><UserSecretsId>unselected</UserSecretsId></PropertyGroup>"
    )]
    [InlineData(
        "<PropertyGroup><UserSecretsId>../SyntheticPrivateId</UserSecretsId></PropertyGroup>"
    )]
    [InlineData("")]
    [InlineData("<Sdk Name=\"Microsoft.NET.Sdk\" />")]
    [InlineData(
        "<PropertyGroup><UserSecretsId>unselected</UserSecretsId></PropertyGroup><Import Project=\"SyntheticPrivateId.props\" />"
    )]
    public async Task CertainlyUnselectedProjectsDoNotWarnOrPreventGeneration(string declaration)
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        cli.Store("sample", "{}");
        cli.Root.File("folder/Other.csproj", "<Project>" + declaration + "</Project>");
        cli.Root.File("folder/appsettings.json", "not JSON: unrelated configuration");
        cli.Root.File(
            "folder/Api/Api.csproj",
            "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File(
            "folder/Api/appsettings.json",
            """{"Endpoint":"http://localhost:8004/path"}"""
        );
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });

        (await cli.Run("--verbose")).Should().Be(0, cli.Console.AllText);

        Written(cli, "sample")
            .Should()
            .Be("""{"Endpoint":"http://host.docker.internal:8004/path"}""");
        cli.Console.AllText.Should().NotContain("Warning:").And.NotContain("Other.csproj");
    }

    [Theory]
    [InlineData("malformed", false)]
    [InlineData("dtd", false)]
    [InlineData("locked", false)]
    [InlineData("malformed", true)]
    [InlineData("dtd", true)]
    [InlineData("locked", true)]
    public async Task IncompleteProjectDiscoveryPreservesCopyOnlyButNeverGuessesGenerationConsumers(
        string problem,
        bool generate
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["first","sample"]}""");
        cli.Store("first", "{}");
        cli.Store("sample", """{"Copied":"synthetic-only"}""");
        var broken = cli.Root.File(
            "folder/Templates/Broken.csproj",
            problem switch
            {
                "malformed" => "<Project><SyntheticPrivateId>unfinished",
                "dtd" => "<!DOCTYPE Project [<!ENTITY private 'SyntheticPrivateId'>]><Project />",
                _ => "<Project />",
            }
        );
        using var locked =
            problem == "locked"
                ? new FileStream(broken, FileMode.Open, FileAccess.Read, FileShare.None)
                : null;
        if (generate)
        {
            cli.Root.File(
                "folder/Api/Api.csproj",
                "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
            );
            cli.Root.File(
                "folder/Api/appsettings.json",
                """{"Endpoint":"http://localhost:8004/path"}"""
            );
        }
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });

        (await cli.Run("--verbose", "--force")).Should().Be(generate ? 1 : 0, cli.Console.AllText);

        cli.Console.AllText.Should().Contain("Broken.csproj").And.NotContain("SyntheticPrivateId");
        if (generate)
        {
            await cli.NoWrites();
            cli.Console.AllText.Should()
                .Contain("sample")
                .And.Contain("Endpoint")
                .And.Contain("consumer");
        }
        else
        {
            Written(cli, "sample").Should().Be("""{"Copied":"synthetic-only"}""");
            cli.Console.AllText.Should().Contain("Warning:").And.Contain("copy-only");
        }
    }

    [Theory]
    [InlineData("postgresql://localhost:5000,localhost:5001/db")]
    [InlineData("postgresql://localhost:5000,remote.test:5432/db")]
    [InlineData("postgresql://remote.test:5432,other.test:5433/db")]
    [InlineData("postgresql:///db?host=localhost")]
    [InlineData("postgresql:///db?host=localhost&port=5000")]
    [InlineData("postgresql:///db?host=remote.test&port=5432")]
    [InlineData("postgresql:///db?hostaddr=127.0.0.1")]
    [InlineData("postgresql:///db?hostaddr=127.0.0.1,192.0.2.1&port=5000,5432")]
    [InlineData("postgresql://remote.test:5000,other.test:5432/db?hostaddr=%3A%3A1,192.0.2.1")]
    [InlineData("postgresql:///db?hostaddr=%3A%3A1&port=5000&sslmode=require")]
    [InlineData("postgresql://remote.test:5432/db?hostaddr=192.0.2.1&sslmode=verify-full")]
    [InlineData("postgresql://remote.test:5432/db?hostaddr=&sslmode=verify-full")]
    [InlineData("postgresql://localhost:5432/db?hostaddr=192.0.2.1&sslmode=verify-full")]
    [InlineData("postgresql://localhost:5432/db?host=localhost&hostaddr=192.0.2.1&port=5432")]
    [InlineData(
        "postgresql://remote.test:5432/db?hostaddr=127.0.0.1&port=5000&options=-c%20search_path%3Dsynthetic_schema"
    )]
    [InlineData("a note about postgresql:///db?hostaddr=127.0.0.1&port=5432")]
    [InlineData("postgresql://remote.test:5432/db?host=localhost&port=5000")]
    [InlineData("postgresql://localhost:5432/db?host=localhost&port=5000")]
    [InlineData("postgresql://localhost:5432/db?host=remote.test&port=5432")]
    [InlineData("postgresql://localhost:5432/db?port=5000")]
    [InlineData("postgresql://localhost,other.test/db")]
    [InlineData("a note about postgresql://localhost:5432,localhost:5433/db")]
    [InlineData("postgresql:///db?password=localhost:5432&port=5432")]
    public async Task UnmappedOrUnresolvedPostgresUrlsNeverInferPortsOrScanArbitraryText(
        string endpoint
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        cli.Store("sample", JsonSerializer.Serialize(new { Copied = endpoint }));
        cli.Root.File(
            "folder/Api.csproj",
            "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File(
            "folder/appsettings.json",
            JsonSerializer.Serialize(new { Generated = endpoint })
        );
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 5432 });
        var originals = Directory
            .EnumerateFiles(cli.Root.Path, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);

        (await cli.Run("--verbose")).Should().Be(0, cli.Console.AllText);

        using var written = JsonDocument.Parse(Written(cli, "sample"));
        written.RootElement.EnumerateObject().Should().ContainSingle();
        written.RootElement.GetProperty("Copied").GetString().Should().Be(endpoint);
        cli.Console.AllText.Should().NotContain("unsupported");
        foreach (var (path, original) in originals)
            File.ReadAllBytes(path).Should().Equal(original);
    }

    [Theory]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Build.targets")]
    public async Task ImplicitWorkspaceImportsAreDiagnosedWithoutReadingOrEvaluatingTheirIds(
        string import
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        cli.Store("sample", """{"Copied":"synthetic-only"}""");
        cli.Root.File("folder/src/Api/Api.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");
        cli.Root.File(
            "folder/" + import,
            "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup><Target Name=\"SyntheticNeverRun\"><Error Text=\"SyntheticPrivateId\" /></Target></Project>"
        );
        cli.Root.File("folder/src/Api/appsettings.json", "not JSON: association is unresolved");
        var processes = Substitute.For<IProcessRunner>();
        cli.Services.AddSingleton(processes);

        (await cli.Run("--verbose")).Should().Be(0, cli.Console.AllText);

        Written(cli, "sample").Should().Be("""{"Copied":"synthetic-only"}""");
        cli.Console.AllText.Should()
            .Contain("Api.csproj")
            .And.Contain("UserSecretsId")
            .And.Contain("MSBuild")
            .And.NotContain("SyntheticPrivateId");
        processes.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnlyTheLowercaseDevelopmentFilenameCanSupplyProjectOnlyConfiguration(
        bool lowercase
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        cli.Root.File(
            "folder/Api.csproj",
            "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"><PropertyGroup><UserSecretsId>SAMPLE</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File(
            lowercase
                ? "folder/appsettings.development.json"
                : "folder/appsettings.Development.json",
            """{/* synthetic comment */ "Endpoints":[{"Url":"http://localhost:8004/path"}],} """
        );
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });

        (await cli.Run()).Should().Be(lowercase ? 0 : 1, cli.Console.AllText);

        if (lowercase)
            Written(cli, "sample")
                .Should()
                .Be("""{"Endpoints:0:Url":"http://host.docker.internal:8004/path"}""");
        else
            await cli.NoWrites();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenerationRetainsAllStoreDestinationAndPolicyValidationAndSuppressesRawFailures(
        bool destination
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["first","sample"]}""");
        cli.Store("first", "{}");
        cli.Root.File(
            "folder/Api.csproj",
            "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File("folder/appsettings.json", """{"Endpoint":"http://localhost:8004/path"}""");
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });
        if (destination)
            cli.Sbx.ValidateUserSecretsDestinationAsync(
                    cli.Name,
                    Arg.Is<IReadOnlyList<string>>(ids =>
                        ids.SequenceEqual(new[] { "first", "sample" })
                    ),
                    Arg.Any<CancellationToken>()
                )
                .Returns(Task.FromException(new AboxException("synthetic-private-destination")));
        else
            cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
                .Returns(
                    Task.FromException<IReadOnlyList<int>>(
                        new AboxException("synthetic-private-policy")
                    )
                );

        (await cli.Run("--verbose", "--force")).Should().Be(1);

        await cli.NoWrites();
        cli.Console.AllText.Should()
            .Contain("No stores were copied")
            .And.NotContain("synthetic-private");
    }

    [Fact]
    public async Task EmptySelectionDoesNotDiscoverProjectsAndUnmatchedStoresStillCopyOnly()
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":[]}""");
        cli.Root.File("folder/Bad.csproj", "not XML: an empty selection must not inspect projects");
        (await cli.Run()).Should().Be(0, cli.Console.AllText);
        await cli.NoWrites();
        cli.Sbx.ReceivedCalls()
            .Should()
            .NotContain(call =>
                call.GetMethodInfo().Name == nameof(ISbxClient.GetUserSecretsHostPortsAsync)
            );

        File.Delete(Path.Combine(cli.Location, "Bad.csproj"));
        cli.Configure("""{"userSecrets":["sample"]}""");
        cli.Store("sample", """{"Url":"http://localhost:8004/copied"}""");
        cli.Root.File("folder/appsettings.json", "not JSON: no associated project");
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });

        (await cli.Run()).Should().Be(0, cli.Console.AllText);
        Written(cli, "sample").Should().Be("""{"Url":"http://host.docker.internal:8004/copied"}""");
    }

    [Theory]
    [InlineData("{\"Url\":\"synthetic-private\", broken}")]
    [InlineData("{\"Nested\":{\"Key\":1},\"NESTED:KEY\":2}")]
    [InlineData("{\"Nodes\":[\"a\"],\"nodes:0\":\"b\"}")]
    [InlineData("[]")]
    public async Task EveryAppsettingsFileIsValidatedBeforeFilteringAndBeforeAnyStoreWrite(
        string json
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["first",{"id":"sample","keys":[]}]}""");
        cli.Store("first", "{}");
        cli.Store("sample", "{}");
        cli.Root.File(
            "folder/Api.csproj",
            "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File("folder/appsettings.json", json);

        (await cli.Run("--verbose", "--force")).Should().Be(1);

        await cli.NoWrites();
        cli.Console.AllText.Should()
            .Contain("sample")
            .And.Contain("Api.csproj")
            .And.Contain("appsettings.json")
            .And.Contain("effective .NET")
            .And.NotContain("synthetic-private");
    }

    [Fact]
    public async Task ExplicitCopyRebuildsTheDestinationAndDropsStaleGeneratedEntries()
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        cli.Store("sample", """{"Copied":"synthetic-only"}""");
        cli.Root.File(
            "folder/Api.csproj",
            "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File(
            "folder/appsettings.json",
            """{"Telemetry":"http://localhost:4317/old","LocalStack":"http://localhost:8006/old"}"""
        );
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 4317, 8006 });
        (await cli.Run()).Should().Be(0, cli.Console.AllText);
        Written(cli, "sample").Should().Contain("Telemetry").And.Contain("LocalStack");

        cli.Root.File(
            "folder/appsettings.json",
            """{"Telemetry":"http://localhost:5000/api","LocalStack":"http://localhost:8006/current"}"""
        );

        (await cli.Run()).Should().Be(0, cli.Console.AllText);
        Written(cli, "sample")
            .Should()
            .Be(
                """{"Copied":"synthetic-only","LocalStack":"http://host.docker.internal:8006/current"}"""
            );
    }

    [Fact]
    public async Task RemoteUnmappedAndArbitraryLocalhostValuesAreNeitherGeneratedNorTreatedAsAdaptationFailures()
    {
        using var cli = new UserSecretsCliHarness(
            new AboxConfig { UserSecretsDenyList = ["synthetic-denied"] }
        );
        cli.Configure("""{"userSecrets":[{"id":"sample","keys":["Copy:*","Generate:*" ]}]}""");
        cli.Root.File(
            "folder/Api.csproj",
            "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
        );
        var values = new Dictionary<string, string>
        {
            ["Remote"] = "http://remote.test:8004/synthetic-denied",
            ["WorkerApi"] = "http://localhost:5000/api",
            ["NoTranslation"] = "Host=localhost;Port=5432;Password=synthetic-denied",
            ["UnmappedUnsupported"] =
                "Host=localhost,remote.test;Port=5000;Password=synthetic-denied",
            ["Arbitrary"] = "a note about localhost:8004, not an endpoint",
            ["Credential"] = "Password=http://localhost:8004;Path=localhost,8004",
        };
        cli.Root.File(
            "folder/appsettings.json",
            JsonSerializer.Serialize(
                new { Generate = values, Excluded = "http://localhost:8004/synthetic-denied" }
            )
        );
        var copied = values.Where(pair => !pair.Value.Contains("synthetic-denied")).ToDictionary();
        cli.Store(
            "sample",
            JsonSerializer.Serialize(
                new { Copy = copied, Excluded = "http://localhost:8004/synthetic-denied" }
            )
        );
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });

        (await cli.Run("--force")).Should().Be(0, cli.Console.AllText);

        using var written = JsonDocument.Parse(Written(cli, "sample"));
        written.RootElement.EnumerateObject().Should().HaveCount(3);
        written
            .RootElement.GetProperty("Copy:WorkerApi")
            .GetString()
            .Should()
            .Be("http://localhost:5000/api");
        written
            .RootElement.EnumerateObject()
            .Should()
            .NotContain(property =>
                property.Name.StartsWith("Generate:") || property.Name == "Excluded"
            );
        cli.Console.AllText.Should().NotContain("Safety override");
    }

    [Fact]
    public async Task InternalDirectoryLinksAreDeduplicatedAndCyclesDoNotAddSharedConsumers()
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        var target = cli.Root.Directory_("folder/src/Api");
        cli.Root.File(
            "folder/src/Api/Api.csproj",
            "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File(
            "folder/src/Api/appsettings.json",
            """{"Url":"http://localhost:8004/path"}"""
        );
        var alias = Path.Combine(cli.Location, "alias");
        var cycle = Path.Combine(target, "cycle");
        await LinkDirectory(alias, target);
        await LinkDirectory(cycle, cli.Location);
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });
        try
        {
            (await cli.Run()).Should().Be(0, cli.Console.AllText);
            Written(cli, "sample")
                .Should()
                .Be("""{"Url":"http://host.docker.internal:8004/path"}""");
        }
        finally
        {
            Directory.Delete(cycle);
            Directory.Delete(alias);
        }
    }

    [Theory]
    [InlineData("https://localhost:8004/synthetic-private path?token=synthetic-token")]
    [InlineData("https://[::1]:8004/path%ZZ?token=synthetic-token")]
    [InlineData("https://localhost:8004/path%")]
    [InlineData("https://localhost:8004/\\windows")]
    [InlineData("https://localhost:8004\\windows")]
    [InlineData("https://localhost:8004 synthetic-private")]
    [InlineData("http://localhost/synthetic-private path")]
    [InlineData("http://localhost synthetic-private")]
    [InlineData("https://localhost:8004/path\u0000")]
    [InlineData("postgresql://localhost:5432,localhost:5433/db?password=synthetic-password")]
    [InlineData("postgresql://remote.test:5433,localhost:5432/db")]
    [InlineData("postgres://synthetic-private:synthetic-password@[::1]:5432,remote.test:5433/db")]
    [InlineData("postgresql:///db?host=localhost&port=5432&password=synthetic-password")]
    [InlineData("postgresql:///db?port=5432&host=%3A%3A1")]
    [InlineData("postgresql://localhost:5000/db?port=5432")]
    [InlineData("postgresql://remote.test:5432/db?host=localhost")]
    [InlineData(
        "postgresql://remote.test/db?hostaddr=127.0.0.1&port=5432&sslmode=require&password=synthetic-password"
    )]
    [InlineData(
        "postgresql://remote.test:5432/db?hostaddr=127.0.0.2&options=-c%20search_path%3Dsynthetic-private"
    )]
    [InlineData(
        "postgresql://remote.test/db?host=remote.test&hostaddr=%3A%3A1&port=5432&application_name=synthetic-private"
    )]
    [InlineData("postgresql:///db?hostaddr=0:0:0:0:0:0:0:1&port=5432&password=synthetic-password")]
    [InlineData(
        "postgres://synthetic-private:synthetic-password@remote.test:5432/db?hostaddr=::1&sslmode=verify-full"
    )]
    [InlineData("postgresql://remote.test:5000/db?hostaddr=127.0.0.1&port=5432")]
    [InlineData("postgresql:///db?hostaddr=192.0.2.1,%3A%3A1&port=5000,5432")]
    [InlineData("postgresql://localhost:5432/db?hostaddr=")]
    [InlineData("postgresql://remote.test:5432/db?host=localhost&hostaddr=")]
    [InlineData("Server=localhost,8004;Server=other,8004")]
    [InlineData("Server=localhost,8004;Host=other;Port=8004")]
    [InlineData("Server=localhost;Port=8004;Port=5432")]
    [InlineData("Server=localhost,8004;Password='unterminated")]
    [InlineData("Server=localhost,8004;Password={a;b}")]
    [InlineData("Host=localhost,remote.test;Port=8004;Password=synthetic-password")]
    [InlineData("Host=localhost:8004,remote.test:5432;Password=synthetic-password")]
    [InlineData("Host=localhost:8004;Port=5432;Password=synthetic-password")]
    [InlineData("Host=localhost;Port=8004,5432;Password=synthetic-password")]
    [InlineData("Host=localhost;Host=remote.test;Port=8004;Password=synthetic-password")]
    [InlineData("Host=localhost;Port=5432;Port=8004;Password=synthetic-password")]
    [InlineData("Host=localhost;Port=8004;Password='synthetic-password")]
    [InlineData("Host=localhost;Server=remote.test;Port=8004;Password=synthetic-password")]
    public async Task KnownMappedButUnsupportedEndpointsFailSafelyBeforeAnyStoreIsWritten(
        string endpoint
    )
    {
        foreach (var generated in new[] { false, true })
        foreach (var verbose in new[] { false, true })
        {
            using var cli = new UserSecretsCliHarness();
            cli.Configure("""{"userSecrets":["first","sample"]}""");
            cli.Store("first", """{"Valid":"http://localhost:8004/safe"}""");
            if (generated)
            {
                cli.Root.File(
                    "folder/Api.csproj",
                    "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
                );
                cli.Root.File(
                    "folder/appsettings.json",
                    JsonSerializer.Serialize(new { Endpoint = endpoint })
                );
            }
            else
                cli.Store("sample", JsonSerializer.Serialize(new { Endpoint = endpoint }));
            cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
                .Returns(new[] { 80, 8004, 5432 });
            var originals = Directory
                .EnumerateFiles(cli.Root.Path, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.ReadAllBytes);

            (await cli.Run(["--force", .. verbose ? new[] { "--verbose" } : []]))
                .Should()
                .Be(1, cli.Console.AllText);

            await cli.NoWrites();
            cli.Console.AllText.Should()
                .Contain("sample")
                .And.Contain("Endpoint")
                .And.Contain("mapped")
                .And.Contain("unsupported")
                .And.NotContain("synthetic-private")
                .And.NotContain("synthetic-token")
                .And.NotContain("synthetic-password")
                .And.NotContain("remote.test");
            if (generated)
                cli.Console.AllText.Should().Contain("Api.csproj");
            foreach (var (path, original) in originals)
                File.ReadAllBytes(path).Should().Equal(original);
        }
    }

    [Theory]
    [InlineData("http://localhost:8004/same", false)]
    [InlineData("http://remote.test:8004/other", false)]
    [InlineData("http://localhost:5000/api", false)]
    [InlineData("http://localhost:8004/same", true)]
    public async Task SharedStoresRejectAnyGenerationButPermitAuthoritativeCopyOnlySecrets(
        string second,
        bool authoritative
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["first","shared"]}""");
        cli.Store("first", "{}");
        if (authoritative)
            cli.Store("shared", """{"Endpoint":"http://localhost:8004/authoritative"}""");
        foreach (var project in new[] { "Api", "Worker" })
        {
            cli.Root.File(
                $"folder/{project}/{project}.csproj",
                "<Project><PropertyGroup><UserSecretsId>shared</UserSecretsId></PropertyGroup></Project>"
            );
            cli.Root.File(
                $"folder/{project}/appsettings.json",
                JsonSerializer.Serialize(
                    new { Endpoint = project == "Api" ? "http://localhost:8004/same" : second }
                )
            );
        }
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });

        (await cli.Run("--force")).Should().Be(authoritative ? 0 : 1, cli.Console.AllText);

        if (authoritative)
            Written(cli, "shared")
                .Should()
                .Be("""{"Endpoint":"http://host.docker.internal:8004/authoritative"}""");
        else
        {
            await cli.NoWrites();
            cli.Console.AllText.Should()
                .Contain("shared")
                .And.Contain("Endpoint")
                .And.Contain("multiple")
                .And.Contain("Api.csproj")
                .And.Contain("Worker.csproj")
                .And.Contain("temporary");
        }
        cli.Console.AllText.Should()
            .NotContain("/same")
            .And.NotContain("/other")
            .And.NotContain("/authoritative");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task GeneratedOriginalsCannotBypassTheDenyListAndForceAccounting(
        bool force,
        bool verbose
    )
    {
        using var cli = new UserSecretsCliHarness(
            new AboxConfig { UserSecretsDenyList = ["synthetic-private"] }
        );
        cli.Configure("""{"userSecrets":["first","sample"]}""");
        cli.Store("first", "{}");
        cli.Root.File(
            "folder/Api.csproj",
            "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File(
            "folder/appsettings.json",
            """{"Telemetry":"http://localhost:8004/synthetic-private?token=synthetic-token"}"""
        );
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });

        (
            await cli.Run([
                .. force ? new[] { "--force" } : [],
                .. verbose ? new[] { "--verbose" } : [],
            ])
        )
            .Should()
            .Be(force ? 0 : 1, cli.Console.AllText);

        if (force)
        {
            Written(cli, "sample").Should().Contain("host.docker.internal");
            cli.Console.AllText.Should().Contain("Safety override");
        }
        else
        {
            await cli.NoWrites();
            cli.Console.AllText.Should()
                .Contain("sample")
                .And.Contain("Api.csproj")
                .And.Contain("Telemetry")
                .And.Contain("deny list");
        }
        cli.Console.AllText.Should()
            .NotContain("synthetic-private")
            .And.NotContain("synthetic-token");
    }

    [Theory]
    [InlineData(
        "https://[::1]:8004/synthetic-private?q=localhost:8004#synthetic-fragment",
        "https://host.docker.internal:8004/synthetic-private?q=localhost:8004#synthetic-fragment"
    )]
    [InlineData("http://127.0.0.2:8004/path", "http://host.docker.internal:8004/path")]
    [InlineData("http://localhost/path", "http://host.docker.internal/path")]
    [InlineData(
        "Host=::1;Port=8004;Database=synthetic-db;Username=localhost;Password='synthetic;password';SSL Mode=Disable",
        "Host=host.docker.internal;Port=8004;Database=synthetic-db;Username=localhost;Password='synthetic;password';SSL Mode=Disable"
    )]
    [InlineData(
        "Host=[0:0:0:0:0:0:0:1];Port='08004';Database=synthetic-db",
        "Host=host.docker.internal;Port='08004';Database=synthetic-db"
    )]
    [InlineData(
        "Host=localhost;Database=synthetic-db",
        "Host=host.docker.internal;Database=synthetic-db"
    )]
    [InlineData(
        "Host=localhost:8004;Database=synthetic-db",
        "Host=host.docker.internal:8004;Database=synthetic-db"
    )]
    [InlineData(
        "Host='[::1]:08004';Database=synthetic-db",
        "Host='host.docker.internal:08004';Database=synthetic-db"
    )]
    [InlineData("http://localhost:0008004/path", "http://host.docker.internal:0008004/path")]
    [InlineData(
        "Host=' localhost ';Port=8004;Database=synthetic-db",
        "Host=' host.docker.internal ';Port=8004;Database=synthetic-db"
    )]
    public async Task UrlAndPostgresLoopbacksChangeOnlyTheHostInBothConfigurationSources(
        string original,
        string expected
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        cli.Root.File(
            "folder/Api.csproj",
            "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File(
            "folder/appsettings.json",
            JsonSerializer.Serialize(new { Generated = original })
        );
        cli.Store("sample", JsonSerializer.Serialize(new { Copied = original }));
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 80, 8004, 5432 });

        (await cli.Run()).Should().Be(0, cli.Console.AllText);

        using var written = JsonDocument.Parse(Written(cli, "sample"));
        written.RootElement.GetProperty("Generated").GetString().Should().Be(expected);
        written.RootElement.GetProperty("Copied").GetString().Should().Be(expected);
        cli.Console.AllText.Should()
            .NotContain("synthetic-private")
            .And.NotContain("synthetic-db")
            .And.NotContain("synthetic;password");
    }

    [Theory]
    [InlineData(
        "<PropertyGroup><UserSecretsId>$(SyntheticPrivateId)</UserSecretsId></PropertyGroup>"
    )]
    [InlineData(
        "<PropertyGroup Condition=\"'$(Configuration)' == 'Debug'\"><UserSecretsId>sample</UserSecretsId></PropertyGroup>"
    )]
    [InlineData(
        "<PropertyGroup><UserSecretsId>sample</UserSecretsId><UserSecretsId>other</UserSecretsId></PropertyGroup>"
    )]
    [InlineData("<Import Project=\"SyntheticPrivateId.props\" />")]
    public async Task UnsupportedAssociationsAreDiagnosedWithoutGuessingOrBlockingCopyOnlySecrets(
        string declaration
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        cli.Root.File("folder/Api.csproj", "<Project>" + declaration + "</Project>");
        cli.Root.File(
            "folder/appsettings.json",
            """{"Url":"http://localhost:8004/not-authorized-by-association"}"""
        );
        cli.Store("sample", """{"Selected":"synthetic-only"}""");
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });

        (await cli.Run("--verbose")).Should().Be(0, cli.Console.AllText);

        Written(cli, "sample").Should().Be("""{"Selected":"synthetic-only"}""");
        cli.Console.AllText.Should()
            .Contain("Api.csproj")
            .And.Contain("UserSecretsId")
            .And.Contain("MSBuild")
            .And.NotContain("SyntheticPrivateId")
            .And.NotContain("not-authorized-by-association");
    }

    [Fact]
    public async Task MissingHostAndProjectConfigurationFailsActionablyBeforeAnyDestinationWrite()
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["first","missing"]}""");
        cli.Store("first", "{}");
        cli.Root.File(
            "folder/Api.csproj",
            "<Project><PropertyGroup><UserSecretsId>missing</UserSecretsId></PropertyGroup></Project>"
        );

        (await cli.Run("--force")).Should().Be(1);

        await cli.NoWrites();
        cli.Console.AllText.Should()
            .Contain("missing")
            .And.Contain("appsettings")
            .And.Contain("UserSecretsId");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscoveryNeverReadsDirectoryLinksOutsideTheWorkspace(bool throughLinkedParent)
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        cli.Store("sample", "{}");
        var target = cli.Root.Directory_("outside");
        cli.Root.File("outside/Bad.csproj", "not XML: must not read outside workspace");
        var link = Path.Combine(cli.Location, "escape");
        await LinkDirectory(link, target);
        var indirect = Path.Combine(cli.Location, "indirect");
        if (throughLinkedParent)
        {
            cli.Root.File(
                "outside/nested/Bad.csproj",
                "not XML: must not follow intermediate linked parents"
            );
            await LinkDirectory(indirect, Path.Combine(link, "nested"));
        }
        try
        {
            (await cli.Run()).Should().Be(0, cli.Console.AllText);
            Written(cli, "sample").Should().Be("{}");
        }
        finally
        {
            if (throughLinkedParent)
                Directory.Delete(indirect);
            Directory.Delete(link);
        }
    }

    [Theory]
    [InlineData(".git/Bad.csproj")]
    [InlineData("src/bin/Bad.csproj")]
    [InlineData("src/obj/Bad.csproj")]
    [InlineData("src/nCrunchTemp_bad.csproj")]
    public async Task DiscoveryIsWorkspaceBoundedAndIgnoresBuildArtifactsAndUnselectedConfiguration(
        string excluded
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        cli.Root.File(
            "folder/src/Api/Api.csproj",
            "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File(
            "folder/src/Api/appsettings.json",
            """{"Url":"http://localhost:8004/selected"}"""
        );
        cli.Root.File("folder/" + excluded, "not XML: must not inspect artifacts");
        cli.Root.File("Outside.csproj", "not XML: must not search parents");
        cli.Root.File(
            "folder/Other/Other.csproj",
            "<Project><PropertyGroup><UserSecretsId>unselected</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File(
            "folder/Other/appsettings.json",
            "not JSON: unselected configuration must not be read"
        );
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });

        (await cli.Run()).Should().Be(0, cli.Console.AllText);

        Written(cli, "sample")
            .Should()
            .Contain("host.docker.internal")
            .And.NotContain("unselected");
    }

    [Theory]
    [InlineData("\"sample\"", 5)]
    [InlineData("{\"id\":\"sample\",\"keys\":[\"services:*\",\"Override\"]}", 4)]
    [InlineData("{\"id\":\"sample\",\"keys\":[\"Services:Url\"]}", 1)]
    [InlineData("{\"id\":\"sample\",\"keys\":[]}", 0)]
    public async Task DevelopmentAndSelectedSecretsWinCaseInsensitivelyWithinTheSameKeyFilters(
        string selection,
        int expectedCount
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("{\"userSecrets\":[" + selection + "]}");
        cli.Root.File(
            "folder/Api.csproj",
            "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File(
            "folder/appsettings.json",
            """{"Services":{"Url":"http://localhost:8004/base","Other":"http://localhost:8004/base-other","Null":"http://localhost:8004/base-null"},"Override":"http://localhost:8004/base-override","Generated":"http://localhost:8004/generated"}"""
        );
        cli.Root.File(
            "folder/appsettings.development.json",
            """{"services":{"URL":"http://localhost:8004/development","other":"http://localhost:8004/development-other"},"Override":"http://localhost:8004/development-override"}"""
        );
        var host = cli.Store(
            "sample",
            """{"SERVICES:Url":"http://127.0.0.1:8004/authoritative","Services:Null":null,"Override":"remote-authoritative"}"""
        );
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });

        (await cli.Run()).Should().Be(0, cli.Console.AllText);

        using var written = JsonDocument.Parse(Written(cli, "sample"));
        written.RootElement.EnumerateObject().Should().HaveCount(expectedCount);
        if (expectedCount > 0)
            written
                .RootElement.GetProperty("SERVICES:Url")
                .GetString()
                .Should()
                .Be("http://host.docker.internal:8004/authoritative");
        if (expectedCount > 1)
        {
            written
                .RootElement.GetProperty("Services:Null")
                .ValueKind.Should()
                .Be(JsonValueKind.Null);
            written
                .RootElement.GetProperty("Override")
                .GetString()
                .Should()
                .Be("remote-authoritative");
            written
                .RootElement.GetProperty("services:other")
                .GetString()
                .Should()
                .Be("http://host.docker.internal:8004/development-other");
        }
        cli.Console.AllText.Should().Contain($"{expectedCount} keys.");
        File.ReadAllText(host).Should().Contain("127.0.0.1").And.NotContain("host.docker.internal");
    }

    [Fact]
    public async Task SelectedProjectCanGenerateOverridesWithoutAHostStoreOrExecutingABuild()
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        var project = cli.Root.File(
            "folder/src/Api/Api.csproj",
            """<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup><Target Name="SyntheticNeverRun" BeforeTargets="Build"><Error Text="Do not execute builds" /></Target></Project>"""
        );
        var settings = cli.Root.File(
            "folder/src/Api/appsettings.json",
            """{"Telemetry":{"Endpoint":"http://localhost:4317/synthetic-private?token=synthetic-token"},"WorkerApi":"http://localhost:5000/api","Remote":"http://remote.test:4317"}"""
        );
        var originals = new[] { project, settings }.ToDictionary(path => path, File.ReadAllText);
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 4317 });
        var processes = Substitute.For<IProcessRunner>();
        cli.Services.AddSingleton(processes);

        (await cli.Run()).Should().Be(0, cli.Console.AllText);

        using var written = JsonDocument.Parse(Written(cli, "sample"));
        written.RootElement.EnumerateObject().Should().ContainSingle();
        written
            .RootElement.GetProperty("Telemetry:Endpoint")
            .GetString()
            .Should()
            .Be("http://host.docker.internal:4317/synthetic-private?token=synthetic-token");
        foreach (var (path, original) in originals)
            File.ReadAllText(path).Should().Be(original);
        processes
            .ReceivedCalls()
            .Should()
            .BeEmpty("configuration discovery must not execute MSBuild or any process");
        cli.Console.AllText.Should()
            .NotContain("synthetic-private")
            .And.NotContain("synthetic-token");
    }

    private static string Written(UserSecretsCliHarness cli, string id) =>
        (string)
            cli
                .Sbx.ReceivedCalls()
                .Last(call =>
                    call.GetMethodInfo().Name == nameof(ISbxClient.WriteUserSecretsAsync)
                    && (string)call.GetArguments()[1]! == id
                )
                .GetArguments()[2]!;

    private static async Task LinkDirectory(string link, string target)
    {
        link = Path.GetFullPath(link);
        target = Path.GetFullPath(target);
        if (OperatingSystem.IsWindows())
        {
            var result = await new ProcessRunner().RunAsync(
                new ProcessRequest("cmd", ["/c", "mklink", "/J", link, target]),
                CancellationToken.None
            );
            result.ExitCode.Should().Be(0, result.StandardError);
        }
        else
            Directory.CreateSymbolicLink(link, target);
    }
}
