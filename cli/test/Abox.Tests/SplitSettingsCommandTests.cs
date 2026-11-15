using System.Text.Json;
using Abox.Configuration;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class SplitSettingsCommandTests
{
    [Fact]
    public async Task RecognizedAppsettingsPairGeneratesOnlyTheHostOverride()
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        Project(
            cli,
            """{"Database_Config":{"Host":"localhost","Port":8004,"Database":"synthetic-db"}}"""
        );
        Mapped(cli);
        var originals = Originals(cli);

        (await cli.Run()).Should().Be(0, cli.Console.AllText);

        Written(cli).Should().Be("""{"Database_Config:Host":"host.docker.internal"}""");
        AssertOriginals(originals);
    }

    private static void Project(UserSecretsCliHarness cli, string settings, string name = "Api")
    {
        cli.Root.File(
            $"folder/{name}/{name}.csproj",
            "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File($"folder/{name}/appsettings.json", settings);
    }

    [Fact]
    public async Task HostOnlyFilterUsesCaseInsensitiveDevelopmentPortMetadataWithoutEmittingIt()
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":[{"id":"sample","keys":["database_config:host"]}]}""");
        Project(cli, """{"Database_Config":{"Host":"localhost","Port":5000}}""");
        cli.Root.File(
            "folder/Api/appsettings.development.json",
            """{"database_config:PORT":"08004"}"""
        );
        Mapped(cli);

        (await cli.Run()).Should().Be(0, cli.Console.AllText);

        Written(cli).Should().Be("""{"Database_Config:Host":"host.docker.internal"}""");
    }

    [Theory]
    [InlineData("[\"Database_Config:*\"]", 8004, 5000, "{\"DATABASE_CONFIG:port\":5000}")]
    [InlineData(
        "[\"Database_Config:*\"]",
        5000,
        8004,
        "{\"DATABASE_CONFIG:port\":8004,\"Database_Config:Host\":\"host.docker.internal\"}"
    )]
    [InlineData(
        "[\"Database_Config:Host\"]",
        8004,
        5000,
        "{\"Database_Config:Host\":\"host.docker.internal\"}"
    )]
    [InlineData("[\"Database_Config:Host\"]", 5000, 8004, "{}")]
    public async Task OnlySelectedSecretPortsOverrideAppsettingsBeforeHostEligibility(
        string keys,
        int settingsPort,
        int secretPort,
        string expected
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("{\"userSecrets\":[{\"id\":\"sample\",\"keys\":" + keys + "}]}");
        Project(
            cli,
            JsonSerializer.Serialize(
                new { Database_Config = new { Host = "localhost", Port = settingsPort } }
            )
        );
        cli.Store(
            "sample",
            JsonSerializer.Serialize(
                new Dictionary<string, int> { ["DATABASE_CONFIG:port"] = secretPort }
            )
        );
        Mapped(cli);
        var originals = Originals(cli);

        (await cli.Run()).Should().Be(0, cli.Console.AllText);

        Written(cli).Should().Be(expected);
        AssertOriginals(originals);
    }

    [Theory]
    [InlineData("localhost", "host.docker.internal")]
    [InlineData("127.0.0.2", "host.docker.internal")]
    [InlineData("::1", "host.docker.internal")]
    [InlineData("[::1]", "host.docker.internal")]
    [InlineData("0:0:0:0:0:0:0:1", "host.docker.internal")]
    [InlineData("remote.test", "remote.test")]
    [InlineData("host.docker.internal", "host.docker.internal")]
    public async Task SelectedSecretHostIsAuthoritativeAndUsesResolvedProjectPort(
        string host,
        string expected
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":[{"id":"sample","keys":["Database_Config:Host"]}]}""");
        Project(cli, """{"Database_Config":{"Host":"remote-appsettings.test","Port":5000}}""");
        cli.Root.File(
            "folder/Api/appsettings.development.json",
            """{"database_config:PORT":8004}"""
        );
        cli.Store(
            "sample",
            JsonSerializer.Serialize(
                new Dictionary<string, string> { ["DATABASE_CONFIG:host"] = host }
            )
        );
        Mapped(cli);
        var originals = Originals(cli);

        (await cli.Run()).Should().Be(0, cli.Console.AllText);

        using var written = JsonDocument.Parse(Written(cli));
        written.RootElement.EnumerateObject().Should().ContainSingle();
        written.RootElement.GetProperty("DATABASE_CONFIG:host").GetString().Should().Be(expected);
        AssertOriginals(originals);
    }

    private static void Mapped(UserSecretsCliHarness cli) =>
        cli
            .Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });

    [Theory]
    [InlineData("localhost,remote.test", "8004")]
    [InlineData("localhost", "8004,5000")]
    [InlineData(" localhost ", "8004")]
    [InlineData("[::1", "8004")]
    [InlineData("::1]", "8004")]
    [InlineData("[127.0.0.1]", "8004")]
    [InlineData("localhost:8004", "8004")]
    [InlineData("http://localhost:8004/synthetic-private", "8004")]
    public async Task RecognizedMappedLocalAmbiguityFailsWithoutValuesOrAnyStoreWrites(
        string host,
        string port
    )
    {
        foreach (var selected in new[] { false, true })
        foreach (var verbose in new[] { false, true })
        {
            using var cli = new UserSecretsCliHarness();
            cli.Configure("""{"userSecrets":["first","sample"]}""");
            cli.Store("first", """{"Valid":"http://localhost:8004/synthetic-private"}""");
            var pair = JsonSerializer.Serialize(
                new
                {
                    Database_Config = new
                    {
                        Host = host,
                        Port = port,
                        Password = "synthetic-password",
                    },
                }
            );
            Project(cli, selected ? "{}" : pair);
            if (selected)
                cli.Store("sample", pair);
            Mapped(cli);
            var originals = Originals(cli);

            (await cli.Run(["--force", .. verbose ? new[] { "--verbose" } : []]))
                .Should()
                .Be(1, cli.Console.AllText);

            await cli.NoWrites();
            cli.Console.AllText.Should()
                .Contain("sample")
                .And.Contain("Database_Config:Host")
                .And.Contain("unsupported")
                .And.NotContain("synthetic-private")
                .And.NotContain("synthetic-password")
                .And.NotContain("remote.test");
            if (!selected)
                cli.Console.AllText.Should().Contain("Api.csproj");
            AssertOriginals(originals);
        }
    }

    private static string Written(UserSecretsCliHarness cli) =>
        (string)
            cli
                .Sbx.ReceivedCalls()
                .Last(call => call.GetMethodInfo().Name == nameof(ISbxClient.WriteUserSecretsAsync))
                .GetArguments()[2]!;

    [Theory]
    [InlineData("LOCALHOST")]
    [InlineData("127.0.0.1")]
    [InlineData("127.12.34.56")]
    [InlineData("::1")]
    [InlineData("[0:0:0:0:0:0:0:1]")]
    public async Task SplitSecretPairsWorkWithoutProjectDiscoveryAndRetainTheOriginalPort(
        string host
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        cli.Store(
            "sample",
            JsonSerializer.Serialize(
                new
                {
                    Database_Config = new
                    {
                        Host = host,
                        Port = "08004",
                        Password = "synthetic-password",
                    },
                }
            )
        );
        Mapped(cli);

        (await cli.Run()).Should().Be(0, cli.Console.AllText);

        Written(cli)
            .Should()
            .Be(
                """{"Database_Config":{"Host":"host.docker.internal","Port":"08004","Password":"synthetic-password"}}"""
            );
        cli.Console.AllText.Should()
            .Contain("1 keys (\"Database_Config:Host\")")
            .And.NotContain("synthetic-password");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"remote.test\"")]
    [InlineData("\"host.docker.internal\"")]
    public async Task SelectedNonlocalOrNullHostsBlockLowerPriorityLocalhostGeneration(string host)
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        Project(cli, """{"Database_Config":{"Host":"localhost","Port":8004}}""");
        var source = "{\"Database_Config:Host\":" + host + "}";
        cli.Store("sample", source);
        Mapped(cli);

        (await cli.Run()).Should().Be(0, cli.Console.AllText);

        Written(cli).Should().Be(source);
    }

    [Theory]
    [InlineData("[\"Database_Config:Port\"]", "{\"Database_Config:Port\":5000}")]
    [InlineData("[]", "{}")]
    [InlineData("[\"Other:*\"]", "{}")]
    public async Task ExcludingTheHostPreventsGenerationAndDoesNotRestoreFilteredSecrets(
        string keys,
        string expected
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("{\"userSecrets\":[{\"id\":\"sample\",\"keys\":" + keys + "}]}");
        Project(cli, """{"Database_Config":{"Host":"localhost,remote.test","Port":8004}}""");
        cli.Store("sample", """{"Database_Config":{"Host":"localhost","Port":5000}}""");
        Mapped(cli);

        (await cli.Run()).Should().Be(0, cli.Console.AllText);

        Written(cli).Should().Be(expected);
    }

    [Theory]
    [InlineData("localhost", "5000")]
    [InlineData("remote.test", "8004")]
    [InlineData("host.docker.internal", "8004")]
    [InlineData("localhost", "null")]
    [InlineData("localhost", "\"not-a-port\"")]
    [InlineData("localhost", "-1")]
    [InlineData("localhost", "65536")]
    [InlineData("localhost", "8004.0")]
    [InlineData("localhost", "true")]
    [InlineData("localhost", "[8004]")]
    [InlineData("localhost", "{\"Value\":8004}")]
    [InlineData("localhost,remote.test", "5000")]
    [InlineData("a note about localhost", "8004")]
    [InlineData("127.1", "8004")]
    public async Task RemoteUnmappedOrUnresolvedSplitSettingsDoNotGenerateOverrides(
        string host,
        string port
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        var pair =
            "{\"Database_Config\":{\"Host\":"
            + JsonSerializer.Serialize(host)
            + ",\"Port\":"
            + port
            + "}}";
        Project(cli, pair);
        Mapped(cli);

        (await cli.Run()).Should().Be(0, cli.Console.AllText);

        Written(cli).Should().Be("{}");
        cli.Console.AllText.Should().NotContain("unsupported");
    }

    [Theory]
    [InlineData("Database_Config", "Host", "Port")]
    [InlineData("Other", "Host", "Port")]
    [InlineData("Nested:Database_Config", "Host", "Port")]
    [InlineData("Database_Config", "Server", "Port")]
    [InlineData("Database_Config", "Host", "OtherPort")]
    public async Task MissingPortsAndArbitraryNeighboringPropertiesAreNotInferred(
        string section,
        string hostKey,
        string portKey
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        var settings = new Dictionary<string, object> { [section + ":" + hostKey] = "localhost" };
        if (section != "Database_Config" || hostKey != "Host" || portKey != "Port")
            settings.Add(section + ":" + portKey, 8004);
        Project(cli, JsonSerializer.Serialize(settings));
        cli.Store("sample", JsonSerializer.Serialize(settings));
        Mapped(cli);

        (await cli.Run()).Should().Be(0, cli.Console.AllText);

        using var written = JsonDocument.Parse(Written(cli));
        written
            .RootElement.GetProperty(section + ":" + hostKey)
            .GetString()
            .Should()
            .Be("localhost");
        written.RootElement.EnumerateObject().Should().HaveCount(settings.Count);
    }

    [Theory]
    [InlineData("localhost", "remote.test", 8004, false)]
    [InlineData("remote.test", "localhost", 8004, true)]
    [InlineData("localhost", "::1", 5000, false)]
    [InlineData("remote.test", "[::1]", 8004, true)]
    public async Task DevelopmentResolvesBothHostAndPortBeforeEligibility(
        string baseHost,
        string developmentHost,
        int developmentPort,
        bool eligible
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        Project(
            cli,
            JsonSerializer.Serialize(new { Database_Config = new { Host = baseHost, Port = 8004 } })
        );
        cli.Root.File(
            "folder/Api/appsettings.development.json",
            JsonSerializer.Serialize(
                new Dictionary<string, object>
                {
                    ["DATABASE_CONFIG:HOST"] = developmentHost,
                    ["database_config:PORT"] = developmentPort,
                }
            )
        );
        Mapped(cli);

        (await cli.Run()).Should().Be(0, cli.Console.AllText);

        Written(cli)
            .Should()
            .Be(eligible ? """{"DATABASE_CONFIG:HOST":"host.docker.internal"}""" : "{}");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SplitHostGenerationUsesOriginalValueDenyChecksAndForceAccounting(
        bool force,
        bool selected
    )
    {
        using var cli = new UserSecretsCliHarness(
            new AboxConfig { UserSecretsDenyList = ["localhost"] }
        );
        cli.Configure("""{"userSecrets":["first","sample"]}""");
        cli.Store("first", "{}");
        Project(cli, """{"Database_Config":{"Host":"localhost","Port":8004}}""");
        if (selected)
            cli.Store("sample", """{"Database_Config:Host":"localhost"}""");
        Mapped(cli);

        (await cli.Run(["--verbose", .. force ? new[] { "--force" } : []]))
            .Should()
            .Be(force ? 0 : 1, cli.Console.AllText);

        if (force)
        {
            Written(cli).Should().Be("""{"Database_Config:Host":"host.docker.internal"}""");
            cli.Console.AllText.Should().Contain("Safety override");
        }
        else
        {
            await cli.NoWrites();
            cli.Console.AllText.Should()
                .Contain("deny list")
                .And.Contain("Database_Config:Host")
                .And.NotContain("localhost");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SplitGenerationRetainsPolicyAndAllStoreDestinationValidation(bool destination)
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["first","sample"]}""");
        cli.Store("first", "{}");
        Project(cli, """{"Database_Config":{"Host":"localhost","Port":8004}}""");
        Mapped(cli);
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
    public async Task RecopyDropsStaleGeneratedHostWhenTheEffectivePortStopsBeingMapped()
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        Project(cli, """{"Database_Config":{"Host":"localhost","Port":8004}}""");
        Mapped(cli);
        (await cli.Run()).Should().Be(0, cli.Console.AllText);
        Written(cli).Should().Be("""{"Database_Config:Host":"host.docker.internal"}""");
        cli.Root.File(
            "folder/Api/appsettings.development.json",
            """{"Database_Config:Port":5000}"""
        );

        (await cli.Run()).Should().Be(0, cli.Console.AllText);

        Written(cli).Should().Be("{}");
    }

    private static Dictionary<string, byte[]> Originals(UserSecretsCliHarness cli) =>
        Directory
            .EnumerateFiles(cli.Root.Path, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);

    private static void AssertOriginals(Dictionary<string, byte[]> originals)
    {
        foreach (var (path, original) in originals)
            File.ReadAllBytes(path).Should().Equal(original);
    }
}
