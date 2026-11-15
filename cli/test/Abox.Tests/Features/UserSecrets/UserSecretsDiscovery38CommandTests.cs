using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class UserSecretsDiscovery38CommandTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NestedOrdinarySharedConsumersAreValidatedBeforeAnyWrite(
        bool conflict,
        bool reverse
    )
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["first","shared"]}""");
        cli.Store("first", "{}");
        cli.Store("shared", """{"Copied":"synthetic-only"}""");
        Project(cli, reverse ? "z/deep/Api" : "a/deep/Api", "http://localhost:8004/synthetic-path");
        Project(
            cli,
            reverse ? "a/other/deep/Worker" : "z/other/deep/Worker",
            conflict
                ? "http://remote.test:8004/synthetic-private"
                : "http://127.0.0.1:8004/synthetic-path"
        );
        cli.Root.File("folder/Directory.Build.props", "not XML: MSBuild is never evaluated");
        cli.Root.File(
            "folder/unselected/Computed.csproj",
            "<Project><PropertyGroup><UserSecretsId>$(SyntheticPrivateId)</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File("folder/unselected/appsettings.json", "not JSON: no static association");
        var originals = Directory
            .EnumerateFiles(cli.Root.Path, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);
        var processes = Substitute.For<IProcessRunner>();
        cli.Services.AddSingleton(processes);
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });

        // Act
        var exit = await cli.Run("--verbose", "--force");

        // Assert
        exit.Should().Be(conflict ? 1 : 0, cli.Console.AllText);
        if (conflict)
        {
            await cli.NoWrites();
            cli.Console.AllText.Should()
                .Contain("conflict")
                .And.Contain("Api.csproj")
                .And.Contain("Worker.csproj");
        }
        else
        {
            Written(cli, "first").Should().Be("{}");
            Written(cli, "shared")
                .Should()
                .Be(
                    """{"Copied":"synthetic-only","Endpoint":"http://host.docker.internal:8004/synthetic-path"}"""
                );
        }
        foreach (var (path, original) in originals)
            File.ReadAllBytes(path).Should().Equal(original);
        processes.ReceivedCalls().Should().BeEmpty();
        cli.Console.AllText.Should().NotContain("synthetic-").And.NotContain("SyntheticPrivateId");
    }

    [Theory]
    [InlineData("inside", false)]
    [InlineData("inside", true)]
    [InlineData("escape", false)]
    [InlineData("escape", true)]
    [InlineData("excluded", false)]
    [InlineData("excluded", true)]
    public async Task LinkedTargetAncestorsRetainContainmentExclusionsAliasesAndCycles(
        string targetKind,
        bool symbolic
    )
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        using var links = new DirectoryLinks38();
        cli.Configure("""{"userSecrets":["shared"]}""");
        cli.Store("shared", "{}");
        var target = cli.Root.Directory_(
            targetKind switch
            {
                "inside" => "folder/real",
                "escape" => "folder-sibling",
                _ => "folder/obj",
            }
        );
        var project = cli.Root.File(
            Path.GetRelativePath(cli.Root.Path, Path.Combine(target, "nested/Api.csproj")),
            "<Project><PropertyGroup><UserSecretsId>shared</UserSecretsId></PropertyGroup></Project>"
        );
        var settings = Path.Combine(target, "nested/appsettings.json");
        File.WriteAllText(
            settings,
            targetKind == "inside"
                ? """{"Endpoint":"http://localhost:8004/synthetic-path"}"""
                : "not JSON: resolved target must never be consumed"
        );
        var parent = Path.Combine(cli.Location, "z-parent");
        await links.Add(parent, target, symbolic);
        await links.Add(
            Path.Combine(cli.Location, "a-alias"),
            Path.Combine(parent, "nested"),
            symbolic
        );
        await links.Add(Path.Combine(target, "nested/cycle"), cli.Location, symbolic);
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });
        var projectOriginal = File.ReadAllBytes(project);
        var settingsOriginal = File.ReadAllBytes(settings);

        // Act
        var exit = await cli.Run("--verbose");

        // Assert
        exit.Should().Be(0, cli.Console.AllText);
        Written(cli, "shared")
            .Should()
            .Be(
                targetKind == "inside"
                    ? """{"Endpoint":"http://host.docker.internal:8004/synthetic-path"}"""
                    : "{}"
            );
        File.ReadAllBytes(project).Should().Equal(projectOriginal);
        File.ReadAllBytes(settings).Should().Equal(settingsOriginal);
        cli.Console.AllText.Should().NotContain("synthetic-path");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkspaceWithALinkedAncestorKeepsItsLexicalSandboxIdentity(bool symbolic)
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        using var links = new DirectoryLinks38();
        cli.Root.File("folder/src/abox.json", """{"userSecrets":["shared"]}""");
        Project(cli, "src/deep/Api", "http://localhost:8004/synthetic-path");
        var entry = Path.Combine(cli.Root.Path, "entry");
        await links.Add(entry, cli.Location, symbolic);
        var location = Path.Combine(entry, "src");
        var name = SandboxName.For(location);
        cli.Sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", null));
        cli.Sbx.GetUserSecretsHostPortsAsync(name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });

        // Act
        var exit = await cli.Invoke("user-secrets", "--location", location);

        // Assert
        exit.Should().Be(0, cli.Console.AllText);
        await cli
            .Sbx.Received(1)
            .WriteUserSecretsAsync(
                name,
                "shared",
                """{"Endpoint":"http://host.docker.internal:8004/synthetic-path"}""",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task BrokenJunctionTargetsFailClosedBeforeAnyStoreWrite()
    {
        // Arrange
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException(
                Xunit.v3.DynamicSkipToken.Value
                    + "Dangling Unix symlinks are not returned by directory enumeration; this case requires a Windows junction."
            );
        using var cli = new UserSecretsCliHarness();
        using var links = new DirectoryLinks38();
        cli.Configure("""{"userSecrets":["first","shared"]}""");
        cli.Store("first", "{}");
        Project(cli, "src/deep/Api", "http://localhost:8004/synthetic-path");
        var target = cli.Root.Directory_("folder/removed");
        await links.Add(Path.Combine(cli.Location, "broken"), target, symbolic: false);
        Directory.Delete(target);

        // Act
        var exit = await cli.Run("--verbose", "--force");

        // Assert
        exit.Should().Be(1, cli.Console.AllText);
        await cli.NoWrites();
        cli.Console.AllText.Should()
            .Contain("Cannot discover projects")
            .And.Contain("no stores were copied");
    }

    [Theory]
    [InlineData("Api.csproj")]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.development.json")]
    public async Task ConfigurationFileLinksRetainTheirOrdinaryFileRequirements(string file)
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["first","shared"]}""");
        cli.Store("first", "{}");
        cli.Store("shared", "{}");
        Project(cli, "src/deep/Api", "http://localhost:8004/synthetic-path");
        var path = Path.Combine(cli.Location, "src", "deep", "Api", file);
        var target = cli.Root.File(
            "synthetic-file-target",
            file == "Api.csproj"
                ? "<Project><PropertyGroup><UserSecretsId>shared</UserSecretsId></PropertyGroup></Project>"
                : "not JSON: must never be read through a file link"
        );
        File.Delete(path);
        try
        {
            File.CreateSymbolicLink(path, target);
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
                    + "File symlinks require native link privileges on this host.",
                exception
            );
        }

        // Act
        var exit = await cli.Run("--verbose", "--force");

        // Assert
        exit.Should().Be(file == "Api.csproj" ? 0 : 1, cli.Console.AllText);
        if (file == "Api.csproj")
            Written(cli, "shared").Should().Be("{}");
        else
        {
            await cli.NoWrites();
            cli.Console.AllText.Should().Contain("ordinary file");
        }
        cli.Console.AllText.Should().NotContain("must never be read");
    }

    private static void Project(UserSecretsCliHarness cli, string directory, string endpoint)
    {
        cli.Root.File(
            $"folder/{directory}/{Path.GetFileName(directory)}.csproj",
            "<Project><PropertyGroup><UserSecretsId>shared</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File(
            "folder/" + directory + "/appsettings.json",
            "{\"Endpoint\":\"" + endpoint + "\"}"
        );
    }

    private static string Written(UserSecretsCliHarness cli, string id) =>
        (string)
            cli
                .Sbx.ReceivedCalls()
                .Single(call =>
                    call.GetMethodInfo().Name == nameof(ISbxClient.WriteUserSecretsAsync)
                    && (string)call.GetArguments()[1]! == id
                )
                .GetArguments()[2]!;

    private sealed class DirectoryLinks38 : IDisposable
    {
        private readonly List<string> links = [];

        internal async Task Add(string link, string target, bool symbolic)
        {
            link = Path.GetFullPath(link);
            target = Path.GetFullPath(target);
            try
            {
                if (OperatingSystem.IsWindows() && !symbolic)
                {
                    var result = await new ProcessRunner().RunAsync(
                        new ProcessRequest("cmd", ["/c", "mklink", "/J", link, target]),
                        CancellationToken.None
                    );
                    result.ExitCode.Should().Be(0, result.StandardError);
                }
                else
                    Directory.CreateSymbolicLink(link, target);
                links.Add(link);
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
                        + "Directory symlinks require native link privileges on this host.",
                    exception
                );
            }
        }

        public void Dispose()
        {
            for (var index = links.Count - 1; index >= 0; index--)
                if (OperatingSystem.IsWindows())
                    Directory.Delete(links[index]);
                else
                    File.Delete(links[index]);
        }
    }
}
