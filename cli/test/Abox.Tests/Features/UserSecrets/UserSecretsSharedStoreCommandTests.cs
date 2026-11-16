using System.Text.Json;
using Abox.Configuration;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class UserSecretsSharedStoreCommandTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompatibleOverridesMergeOnceAcrossCaseInsensitiveKeys(bool reverse)
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["shared"]}""");
        Project(
            cli,
            reverse ? "ZWorker" : "AApi",
            """{"Services":{"Url":"http://localhost:8004/synthetic-path"},"Unrelated":"api-only"}"""
        );
        Project(
            cli,
            reverse ? "AApi" : "ZWorker",
            """{"services:URL":"http://127.0.0.2:8004/synthetic-path","Unrelated":"worker-only"}"""
        );
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });
        var originals = Sources(cli);

        // Act
        var exit = await cli.Run("--verbose");

        // Assert
        exit.Should().Be(0, cli.Console.AllText);
        Written(cli, "shared")
            .Should()
            .Be("""{"Services:Url":"http://host.docker.internal:8004/synthetic-path"}""");
        cli.Console.AllText.Should().Contain("1 keys.").And.NotContain("synthetic-path");
        AssertUnchanged(originals);
    }

    [Theory]
    [InlineData("http://localhost:8004/synthetic-other")]
    [InlineData("http://localhost:4317/synthetic-path")]
    [InlineData("http://synthetic-remote.test:8004/synthetic-path")]
    [InlineData("http://localhost:5000/synthetic-worker-api")]
    [InlineData("Host=synthetic-remote.test;Port=8004;Password=synthetic-password")]
    [InlineData("null")]
    public async Task ConflictsProtectEveryConsumerBeforeWritingAnyStoreInEitherDiscoveryOrder(
        string second
    )
    {
        foreach (var reverse in new[] { false, true })
        foreach (var verbose in new[] { false, true })
        {
            // Arrange
            using var cli = new UserSecretsCliHarness();
            cli.Configure("""{"userSecrets":["first","shared"]}""");
            cli.Store("first", """{"Valid":"http://localhost:8004/synthetic-first"}""");
            Project(
                cli,
                reverse ? "ZWorker" : "AApi",
                """{"Services:Endpoint":"http://localhost:8004/synthetic-path?token=synthetic-token"}"""
            );
            Project(
                cli,
                reverse ? "AApi" : "ZWorker",
                second == "null"
                    ? """{"services":{"ENDPOINT":null}}"""
                    : JsonSerializer.Serialize(
                        new Dictionary<string, string> { ["services:ENDPOINT"] = second }
                    )
            );
            cli.Root.File(
                "folder/Unrelated/Unrelated.csproj",
                "<Project><PropertyGroup><UserSecretsId>$(SyntheticPrivateId)</UserSecretsId></PropertyGroup></Project>"
            );
            cli.Root.File("folder/Unrelated/appsettings.json", "not JSON: unrelated configuration");
            cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
                .Returns(new[] { 8004, 4317 });
            var originals = Sources(cli);

            // Act
            var exit = await cli.Run(["--force", .. verbose ? new[] { "--verbose" } : []]);

            // Assert
            exit.Should().Be(1, cli.Console.AllText);
            await cli.NoWrites();
            cli.Console.AllText.Should()
                .Contain("shared")
                .And.Contain("Services:Endpoint")
                .And.Contain("AApi.csproj")
                .And.Contain("ZWorker.csproj")
                .And.Contain("conflict")
                .And.Contain("authoritative selected host-secret")
                .And.NotContain("Warning:")
                .And.NotContain("Unrelated.csproj")
                .And.NotContain("synthetic-");
            AssertUnchanged(originals);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompatibleDisjointKeysAndAlreadyAdaptedConsumersMergeWithoutUnrelatedConflicts(
        bool reverse
    )
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["shared"]}""");
        Project(
            cli,
            reverse ? "ZWorker" : "AApi",
            """{"Url":"http://localhost:8004/synthetic-url","Postgres":"Host=localhost;Port=5432;Database=synthetic-db;Password=synthetic-password","Unrelated":1}"""
        );
        Project(
            cli,
            reverse ? "AApi" : "ZWorker",
            """{"Url":"http://host.docker.internal:8004/synthetic-url","Postgres":"Host=127.0.0.1;Port=5432;Database=synthetic-db;Password=synthetic-password","Telemetry":"http://localhost:4317/synthetic-telemetry","Unrelated":false}"""
        );
        Project(cli, "NoSettings", null);
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004, 4317, 5432 });

        // Act
        var exit = await cli.Run();

        // Assert
        exit.Should().Be(0, cli.Console.AllText);
        Written(cli, "shared")
            .Should()
            .Be(
                """{"Postgres":"Host=host.docker.internal;Port=5432;Database=synthetic-db;Password=synthetic-password","Telemetry":"http://host.docker.internal:4317/synthetic-telemetry","Url":"http://host.docker.internal:8004/synthetic-url"}"""
            );
        cli.Console.AllText.Should().Contain("3 keys.").And.NotContain("synthetic-");
    }

    [Theory]
    [InlineData("Host=localhost;Port=5432;Database=synthetic-other;Password=synthetic-password")]
    [InlineData("Host=localhost;Port=5432;Database=synthetic-db;Password=synthetic-other")]
    [InlineData(
        "Host=synthetic-remote.test;Port=5432;Database=synthetic-db;Password=synthetic-password"
    )]
    [InlineData("Host=localhost;Port=5000;Database=synthetic-db;Password=synthetic-password")]
    public async Task PostgresCompatibilityProtectsTheCompleteConsumerValueNotOnlyTheHost(
        string second
    )
    {
        foreach (var reverse in new[] { false, true })
        {
            // Arrange
            using var cli = new UserSecretsCliHarness();
            cli.Configure("""{"userSecrets":["first","shared"]}""");
            cli.Store("first", "{}");
            Project(
                cli,
                reverse ? "Worker" : "Api",
                """{"ConnectionStrings":{"Database":"Host=localhost;Port=5432;Database=synthetic-db;Password=synthetic-password"}}"""
            );
            Project(
                cli,
                reverse ? "Api" : "Worker",
                JsonSerializer.Serialize(
                    new Dictionary<string, string> { ["connectionstrings:DATABASE"] = second }
                )
            );
            cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
                .Returns(new[] { 5432 });

            // Act
            var exit = await cli.Run("--verbose");

            // Assert
            exit.Should().Be(1);
            await cli.NoWrites();
            cli.Console.AllText.Should()
                .Contain("conflict")
                .And.Contain("Api.csproj")
                .And.Contain("Worker.csproj")
                .And.NotContain("synthetic-");
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"synthetic-authoritative\"")]
    [InlineData("\"http://synthetic-remote.test:8004/synthetic-host\"")]
    [InlineData("\"http://localhost:8004/synthetic-host\"")]
    public async Task SelectedHostKeysAreAuthoritativeBeforeSharedCompatibilityChecks(
        string hostValue
    )
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":[{"id":"shared","keys":["endpoint","Safe"]}]}""");
        cli.Store("shared", "{\"ENDPOINT\":" + hostValue + "}");
        Project(
            cli,
            "Api",
            """{"Endpoint":"http://localhost:8004/synthetic-api","Safe":"http://localhost:4317/synthetic-safe","Excluded":"http://localhost:8004/synthetic-api"}"""
        );
        Project(
            cli,
            "Worker",
            """{"endpoint":"http://synthetic-remote.test:5000/synthetic-worker","Safe":"http://127.0.0.1:4317/synthetic-safe","Excluded":"http://localhost:8004/synthetic-worker"}"""
        );
        cli.Root.File("folder/Unknown.csproj", "not XML: no explicit matching association");
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004, 4317 });
        var originals = Sources(cli);

        // Act
        var exit = await cli.Run("--verbose");

        // Assert
        exit.Should().Be(0, cli.Console.AllText);
        using var written = JsonDocument.Parse(Written(cli, "shared"));
        written.RootElement.EnumerateObject().Should().HaveCount(2);
        written
            .RootElement.GetProperty("Safe")
            .GetString()
            .Should()
            .Be("http://host.docker.internal:4317/synthetic-safe");
        using var expected = JsonDocument.Parse(hostValue);
        var endpoint = written.RootElement.GetProperty("ENDPOINT");
        if (expected.RootElement.ValueKind == JsonValueKind.Null)
            endpoint.ValueKind.Should().Be(JsonValueKind.Null);
        else
            endpoint
                .GetString()
                .Should()
                .Be(
                    hostValue.Contains("localhost")
                        ? "http://host.docker.internal:8004/synthetic-host"
                        : expected.RootElement.GetString()
                );
        cli.Console.AllText.Should().NotContain("synthetic-");
        AssertUnchanged(originals);
    }

    [Theory]
    [InlineData("[\"services:allowed\"]", 1)]
    [InlineData("[\"SERVICES:ALLOW*\"]", 1)]
    [InlineData("[]", 0)]
    [InlineData("[\"NoMatch\"]", 0)]
    public async Task ExactWildcardAndEmptyFiltersApplyBeforeCompatibility(
        string keys,
        int expectedCount
    )
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        cli.Configure("{\"userSecrets\":[{\"id\":\"shared\",\"keys\":" + keys + "}]}");
        cli.Store("shared", """{"Excluded":"http://localhost:8004/synthetic-host"}""");
        Project(
            cli,
            "Api",
            """{"Services":{"Allowed":"http://localhost:8004/synthetic-base","Excluded":"http://localhost:8004/synthetic-api"},"Excluded":"http://localhost:8004/synthetic-api"}"""
        );
        cli.Root.File(
            "folder/Api/appsettings.development.json",
            """{"services:ALLOWED":"http://localhost:8004/synthetic-development"}"""
        );
        Project(
            cli,
            "Worker",
            """{"SERVICES:Allowed":"http://127.0.0.1:8004/synthetic-development","Services:Excluded":"http://synthetic-remote.test:8004/synthetic-worker","Excluded":"http://localhost:8004/synthetic-worker"}"""
        );
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });
        var originals = Sources(cli);

        // Act
        var exit = await cli.Run();

        // Assert
        exit.Should().Be(0, cli.Console.AllText);
        using var written = JsonDocument.Parse(Written(cli, "shared"));
        written.RootElement.EnumerateObject().Should().HaveCount(expectedCount);
        if (expectedCount > 0)
            written
                .RootElement.EnumerateObject()
                .Single()
                .Value.GetString()
                .Should()
                .Be("http://host.docker.internal:8004/synthetic-development");
        cli.Console.AllText.Should().NotContain("Excluded").And.NotContain("synthetic-");
        AssertUnchanged(originals);
    }

    [Theory]
    [InlineData(false, "Api", "Worker")]
    [InlineData(false, "Worker", "Api")]
    [InlineData(true, "Api", "Worker")]
    [InlineData(true, "Worker", "Api")]
    public async Task CompatibleMergingStillChecksEveryGeneratedOriginalAgainstDenyPolicy(
        bool force,
        string localProject,
        string deniedProject
    )
    {
        // Arrange
        using var cli = new UserSecretsCliHarness(
            new AboxConfig { UserSecretsDenyList = ["127.0.0.2"] }
        );
        DenyConsumers(cli, localProject, deniedProject);

        // Act
        var exit = await cli.Run(force ? ["--force", "--verbose"] : ["--verbose"]);

        // Assert
        exit.Should().Be(force ? 0 : 1, cli.Console.AllText);
        if (force)
        {
            Written(cli, "shared")
                .Should()
                .Be("""{"Endpoint":"http://host.docker.internal:8004/synthetic-path"}""");
            cli.Console.AllText.Should().Contain("Safety override");
        }
        else
        {
            await cli.NoWrites();
            cli.Console.AllText.Should()
                .Contain("deny list")
                .And.Contain(deniedProject + ".csproj")
                .And.Contain("Endpoint");
        }
        cli.Console.AllText.Should().NotContain("synthetic-").And.NotContain("127.0.0.2");
    }

    private static void DenyConsumers(
        UserSecretsCliHarness cli,
        string localProject,
        string deniedProject
    )
    {
        cli.Configure("""{"userSecrets":["first","shared"]}""");
        cli.Store("first", "{}");
        Project(cli, localProject, """{"Endpoint":"http://localhost:8004/synthetic-path"}""");
        Project(cli, deniedProject, """{"Endpoint":"http://127.0.0.2:8004/synthetic-path"}""");
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });
    }

    private static void Project(UserSecretsCliHarness cli, string name, string? json)
    {
        cli.Root.File(
            $"folder/{name}/{name}.csproj",
            "<Project><PropertyGroup><UserSecretsId>shared</UserSecretsId></PropertyGroup></Project>"
        );
        if (json is not null)
            cli.Root.File($"folder/{name}/appsettings.json", json);
    }

    private static Dictionary<string, byte[]> Sources(UserSecretsCliHarness cli) =>
        Directory
            .EnumerateFiles(cli.Root.Path, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);

    private static void AssertUnchanged(Dictionary<string, byte[]> originals)
    {
        foreach (var (path, original) in originals)
            File.ReadAllBytes(path).Should().Equal(original);
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
}
