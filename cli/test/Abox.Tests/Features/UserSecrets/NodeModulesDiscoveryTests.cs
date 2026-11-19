using System.Text.Json;
using Abox.Naming;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class NodeModulesDiscoveryTests
{
    [Theory]
    [InlineData("node_modules/package")]
    [InlineData("src/web/NoDe_MoDuLeS/@scope/package/node_modules/nested")]
    [InlineData("src/NODE_MODULES/package")]
    public async Task DependencyConsumersWithInvalidSettingsCannotBlockIncludedProjects(
        string dependency
    )
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["shared"]}""");
        Project(cli, "src/Api", """{"Url":"http://localhost:8004/synthetic-private"}""");
        Project(cli, dependency, "not JSON: synthetic-private");
        Authorize(cli);
        var originals = FileSnapshots.CaptureText(cli.Root.Path);

        // Act
        var exit = await cli.Run("--verbose", "--force");

        // Assert
        exit.Should().Be(0, cli.Console.AllText);
        cli.Written("shared")
            .Should()
            .Be("""{"Url":"http://host.docker.internal:8004/synthetic-private"}""");
        cli.Console.AllText.Should().NotContain("synthetic-private");
        FileSnapshots.AssertUnchanged(originals);
    }

    [Fact]
    public async Task SimilarlyNamedOrdinaryDirectoriesStillSupplySelectedProjectSettings()
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["shared"]}""");
        foreach (
            var directory in new[]
            {
                "node_modules-backup",
                "my_node_modules",
                "node_modules.cs",
                "node-module",
            }
        )
            Project(
                cli,
                $"src/{directory}/Api",
                JsonSerializer.Serialize(
                    new Dictionary<string, string>
                    {
                        [directory] = "http://localhost:8004/synthetic-private",
                    }
                )
            );
        Authorize(cli);
        var originals = FileSnapshots.CaptureText(cli.Root.Path);

        // Act
        var exit = await cli.Run("--verbose");

        // Assert
        exit.Should().Be(0, cli.Console.AllText);
        using var written = JsonDocument.Parse(cli.Written("shared"));
        written
            .RootElement.EnumerateObject()
            .Select(property => property.Name)
            .Should()
            .BeEquivalentTo(
                "node_modules-backup",
                "my_node_modules",
                "node_modules.cs",
                "node-module"
            );
        written
            .RootElement.EnumerateObject()
            .Should()
            .OnlyContain(property =>
                property.Value.GetString() == "http://host.docker.internal:8004/synthetic-private"
            );
        cli.Console.AllText.Should().NotContain("synthetic-private");
        FileSnapshots.AssertUnchanged(originals);
    }

    [Theory]
    [InlineData(true, "conflict")]
    [InlineData(false, "conflict")]
    [InlineData(true, "json")]
    [InlineData(false, "json")]
    [InlineData(true, "declaration")]
    [InlineData(false, "declaration")]
    public async Task OnlyExcludedConsumersAreOmittedFromAllStoreValidation(
        bool excluded,
        string failure
    )
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["first","shared"]}""");
        cli.Store("first", """{"Copied":"synthetic-private"}""");
        Project(cli, "AApi", """{"Url":"http://localhost:8004/synthetic-private"}""");
        var directory = excluded ? "src/node_modules/Conflict" : "src/dependencies/Conflict";
        Project(
            cli,
            directory,
            failure == "json"
                ? "not JSON: synthetic-private"
                : """{"Url":"http://synthetic-remote.test:8004/synthetic-other"}"""
        );
        if (failure == "declaration")
            cli.Root.File(
                $"folder/{directory}/Consumer.csproj",
                "<Project><PropertyGroup Condition=\"true\"><UserSecretsId>shared</UserSecretsId></PropertyGroup></Project>"
            );
        Authorize(cli);
        var originals = FileSnapshots.CaptureText(cli.Root.Path);

        // Act
        var exit = await cli.Run("--verbose", "--force");

        // Assert
        exit.Should().Be(excluded ? 0 : 1, cli.Console.AllText);
        if (excluded)
        {
            cli.Written("first").Should().Be("""{"Copied":"synthetic-private"}""");
            cli.Written("shared")
                .Should()
                .Be("""{"Url":"http://host.docker.internal:8004/synthetic-private"}""");
        }
        else
        {
            await cli.NoWrites();
            cli.Console.AllText.Should().Contain("Consumer.csproj").And.Contain("shared");
            cli.Console.AllText.Should()
                .Contain(
                    failure switch
                    {
                        "conflict" => "conflict",
                        "json" => "JSON",
                        _ => "unconditional literal",
                    }
                );
        }
        cli.Console.AllText.Should().NotContain("synthetic-");
        FileSnapshots.AssertUnchanged(originals);
    }

    [Fact]
    public async Task DependencyProjectsCannotAssociateAStoreWithoutItsHostFile()
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["first","shared"]}""");
        cli.Store("first", "{}");
        Project(
            cli,
            "node_modules/OnlyConsumer",
            """{"Url":"http://localhost:8004/synthetic-private"}"""
        );
        Authorize(cli);
        var originals = FileSnapshots.CaptureText(cli.Root.Path);

        // Act
        var exit = await cli.Run("--verbose");

        // Assert
        exit.Should().Be(1, cli.Console.AllText);
        await cli.NoWrites();
        cli.Console.AllText.Should()
            .Contain("shared")
            .And.Contain("appsettings")
            .And.NotContain("OnlyConsumer")
            .And.NotContain("synthetic-private");
        FileSnapshots.AssertUnchanged(originals);
    }

    [Theory]
    [InlineData("node_modules")]
    [InlineData("node_modules/Workspace")]
    public async Task AnExplicitRootAtOrBeneathNodeModulesRetainsItsMeaning(string selectedRoot)
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        Project(
            cli,
            $"{selectedRoot}/Api",
            """{"Url":"http://localhost:8004/synthetic-private"}"""
        );
        Project(cli, $"{selectedRoot}/node_modules/Ignored", "not JSON: synthetic-private");
        var location = cli.Root.CreateDirectory($"folder/{selectedRoot}");
        cli.Root.File($"folder/{selectedRoot}/abox.json", """{"userSecrets":["shared"]}""");
        var name = SandboxName.For(location);
        cli.Sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", null));
        cli.Sbx.GetUserSecretsHostPortsAsync(name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });
        var originals = FileSnapshots.CaptureText(cli.Root.Path);

        // Act
        var exit = await cli.Invoke("user-secrets", "--location", location, "--verbose");

        // Assert
        exit.Should().Be(0, cli.Console.AllText);
        cli.Written("shared")
            .Should()
            .Be("""{"Url":"http://host.docker.internal:8004/synthetic-private"}""");
        cli.Console.AllText.Should().NotContain("synthetic-private");
        FileSnapshots.AssertUnchanged(originals);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task DirectoryAliasesRespectResolvedExclusionsAndContainment(
        bool linkedAncestor,
        bool outside
    )
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["shared"]}""");
        Project(
            cli,
            "src/node_modules-backup/Api",
            """{"Url":"http://localhost:8004/synthetic-private"}"""
        );
        var target = outside ? "outside/ordinary" : "folder/web/NoDe_MoDuLeS";
        cli.Root.File(
            $"{target}/package/Bad.csproj",
            "<Project><PropertyGroup><UserSecretsId>shared</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File($"{target}/package/appsettings.json", "not JSON: synthetic-private");
        Authorize(cli);
        var originals = FileSnapshots.CaptureText(cli.Root.Path);
        using var links = new DirectoryLinks();
        var targetPath = Path.Combine(cli.Root.Path, target, "package");
        if (linkedAncestor)
        {
            var parentAlias = Path.Combine(cli.Location, "parent-alias");
            await links.Add(parentAlias, Path.GetDirectoryName(targetPath)!);
            targetPath = Path.Combine(parentAlias, "package");
        }
        await links.Add(Path.Combine(cli.Location, "AAlias"), targetPath);
        await links.Add(Path.Combine(cli.Location, "ZAlias"), Path.Combine(cli.Location, "src"));
        await links.Add(Path.Combine(cli.Location, "src", "cycle"), cli.Location);

        // Act
        var exit = await cli.Run("--verbose", "--force");

        // Assert
        exit.Should().Be(0, cli.Console.AllText);
        cli.Written("shared")
            .Should()
            .Be("""{"Url":"http://host.docker.internal:8004/synthetic-private"}""");
        cli.Console.AllText.Should().NotContain("synthetic-private").And.NotContain("Bad.csproj");
        FileSnapshots.AssertUnchanged(originals);
    }

    private static void Project(UserSecretsCliHarness cli, string directory, string settings) =>
        UserSecretProjects.Write(
            cli.Root,
            $"folder/{directory}/Consumer.csproj",
            "shared",
            settings
        );

    private static void Authorize(UserSecretsCliHarness cli) =>
        cli
            .Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });
}
