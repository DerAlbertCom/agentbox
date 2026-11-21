using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using static Abox.Tests.LegacySandboxInventoryFixture;

namespace Abox.Tests;

public sealed class LegacySandboxWorkspaceInventoryTests
{
    [Theory]
    [InlineData("ports", "drive")]
    [InlineData("ports", "case")]
    [InlineData("ports", "trailing")]
    [InlineData("ports", "combined")]
    [InlineData("user-secrets", "drive")]
    [InlineData("user-secrets", "case")]
    [InlineData("user-secrets", "trailing")]
    [InlineData("user-secrets", "combined")]
    [InlineData("run", "case")]
    public async Task WindowsLegacyWorkspaceSpellingChangesRetainIdentity(
        string command,
        string spelling
    )
    {
        // Arrange
        RequireWindowsWorkspaceLookup();
        using var cli = new UserSecretsCliHarness(existingSandbox: false);
        cli.Configure("{}");
        var original = cli.Location;
        var name = HistoricalFolderName(original);
        var location = WindowsLocationSpelling(original, spelling);
        var calls = UseRealSbx(
            cli,
            _ => new ProcessResult(0, WorkspaceInventory((name, original)), "")
        );

        // Act
        var exit = await cli.Invoke(command, "--location", location);

        // Assert
        exit.Should().Be(0, cli.Console.AllText);
        AssertOnlyLookupOrLaunch(calls, name);
    }

    [Theory]
    [InlineData("current")]
    [InlineData("current-trailing")]
    [InlineData("exact-legacy")]
    public async Task WindowsWorkspaceFallbackCannotOverrideAnExactPreferredIdentity(
        string preferred
    )
    {
        // Arrange
        RequireWindowsWorkspaceLookup();
        using var cli = new UserSecretsCliHarness(existingSandbox: false);
        var location = LegacySandboxFixture.Location;
        var name =
            preferred == "exact-legacy" ? LegacySandboxFixture.Name : SandboxName.For(location);
        var inventory = WorkspaceInventory(
            ("case_name-ai-sbx-0c1bf5", location.ToLowerInvariant()),
            (name, location)
        );
        var calls = UseRealSbx(cli, _ => new ProcessResult(0, inventory, ""));

        // Act
        var exit = await cli.Invoke(
            "run",
            "--location",
            preferred == "current-trailing" ? location + Path.DirectorySeparatorChar : location
        );

        // Assert
        exit.Should().Be(0, cli.Console.AllText);
        calls.Count(call => call.Arguments[0] == "ls").Should().Be(preferred == "current" ? 1 : 2);
        AssertOnlyLookupOrLaunch(calls, name);
    }

    [Theory]
    [InlineData("other-folder")]
    [InlineData("custom-name")]
    [InlineData("wrong-hash")]
    [InlineData("secondary-workspace")]
    [InlineData("relative-workspace")]
    [InlineData("missing-workspace")]
    public async Task WindowsWorkspaceFallbackCannotAdoptAnotherFolderOrUnverifiedLegacyName(
        string candidate
    )
    {
        // Arrange
        RequireWindowsWorkspaceLookup();
        using var cli = new UserSecretsCliHarness(existingSandbox: false);
        var location = LegacySandboxFixture.Location;
        var inventory = RejectedWorkspaceInventory(candidate, location);
        var calls = UseRealSbx(cli, _ => new ProcessResult(0, inventory, ""));

        // Act
        var exit = await cli.Invoke("run", "--location", location.ToUpperInvariant());

        // Assert
        AssertNoSandboxMatched(cli, exit, calls);
    }

    [Fact]
    public async Task WindowsWorkspaceFallbackRejectsAmbiguousVerifiedLegacyIdentities()
    {
        // Arrange
        RequireWindowsWorkspaceLookup();
        using var cli = new UserSecretsCliHarness(existingSandbox: false);
        var location = LegacySandboxFixture.Location;
        var inventory = WorkspaceInventory(
            (LegacySandboxFixture.Name, location),
            ("case_name-ai-sbx-0c1bf5", location.ToLowerInvariant())
        );
        var calls = UseRealSbx(cli, _ => new ProcessResult(0, inventory, ""));

        // Act
        var exit = await cli.Invoke("run", "--location", location.ToUpperInvariant());

        // Assert
        exit.Should().Be(1);
        cli.Console.AllText.Should()
            .Contain("Multiple legacy sandboxes")
            .And.Contain("--location")
            .And.NotContain("abox create");
        calls.Should().NotContain(call => call.Arguments[0] == "exec");
    }

    [Theory]
    [InlineData("failure", 1)]
    [InlineData("malformed", 1)]
    [InlineData("invalid-workspaces", 1)]
    [InlineData("cancel", 130)]
    public async Task WindowsWorkspaceInventoryErrorsCannotBecomeMissingSandboxOrLaunch(
        string failure,
        int expected
    )
    {
        // Arrange
        RequireWindowsWorkspaceLookup();
        using var cli = new UserSecretsCliHarness(existingSandbox: false);
        var listings = 0;
        var calls = UseRealSbx(
            cli,
            _ =>
                ++listings < 3
                    ? new ProcessResult(0, "[]", "")
                    : failure switch
                    {
                        "failure" => new ProcessResult(7, "", "inventory unavailable"),
                        "malformed" => new ProcessResult(0, "{broken", ""),
                        "invalid-workspaces" => new ProcessResult(
                            0,
                            """[{"name":"legacy","workspaces":{}}]""",
                            ""
                        ),
                        _ => throw new OperationCanceledException(),
                    }
        );

        // Act
        var exit = await cli.Invoke("run", "--location", LegacySandboxFixture.Location);

        // Assert
        exit.Should().Be(expected, cli.Console.AllText);
        listings.Should().Be(3);
        cli.Console.AllText.Should().NotContain("abox create");
        calls.Should().NotContain(call => call.Arguments[0] == "exec");
    }

    [Fact]
    public async Task UnixWorkspaceCaseDifferencesNeverAdoptAnotherFolder()
    {
        // Arrange
        if (OperatingSystem.IsWindows())
            throw new InvalidOperationException(
                Xunit.v3.DynamicSkipToken.Value + "Requires Unix path semantics."
            );
        using var cli = new UserSecretsCliHarness(existingSandbox: false);
        var location = LegacySandboxFixture.Location;
        var calls = UseRealSbx(
            cli,
            _ => new ProcessResult(0, WorkspaceInventory((LegacySandboxFixture.Name, location)), "")
        );

        // Act
        var exit = await cli.Invoke("run", "--location", location.ToLowerInvariant());

        // Assert
        AssertNoSandboxMatched(cli, exit, calls);
    }

    [Fact]
    public async Task WindowsWorkspaceFallbackPreservesCreateReplacementConsentAndIdentity()
    {
        // Arrange
        RequireWindowsWorkspaceLookup();
        using var cli = new UserSecretsCliHarness(existingSandbox: false);
        var name = HistoricalFolderName(cli.Location);
        cli.Sbx.ListSandboxesAsync(Arg.Any<CancellationToken>())
            .Returns(
                new[] { new SandboxInfo(name, "running", null) { Workspaces = [cli.Location] } }
            );
        cli.Services.AddSingleton(PublishedSquadFactory.Source());
        cli.Console.Answer = false;

        // Act
        var exit = await cli.Invoke(
            "create",
            "--location",
            cli.Location.ToUpperInvariant(),
            "--team",
            "sample",
            "--squad",
            "ac"
        );

        // Assert
        exit.Should().Be(1);
        cli.Console.AllText.Should()
            .Contain($"The sandbox '{name}' already exists")
            .And.Contain("not replaced");
        await cli
            .Sbx.DidNotReceive()
            .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
        await cli
            .Sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static string WindowsLocationSpelling(string original, string spelling) =>
        spelling switch
        {
            "drive" => char.ToLowerInvariant(original[0]) + original[1..],
            "case" => original.ToUpperInvariant(),
            "combined" => original.ToUpperInvariant() + Path.DirectorySeparatorChar,
            _ => original + Path.DirectorySeparatorChar,
        };

    private static string RejectedWorkspaceInventory(string candidate, string location)
    {
        var otherFolder = Path.Combine(
            Path.GetDirectoryName(location)!,
            "OtherParent",
            "Case_Name"
        );
        var workspaces = candidate switch
        {
            "other-folder" => new[] { otherFolder },
            "secondary-workspace" => new[] { otherFolder, location },
            "relative-workspace" => new[] { "Case_Name" },
            "missing-workspace" => [],
            _ => new[] { location },
        };
        var name = candidate switch
        {
            "custom-name" => "custom-workspace-name",
            "wrong-hash" => "case_name-ai-sbx-ffffff",
            "other-folder" or "secondary-workspace" => "case_name-ai-sbx-"
                + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(otherFolder)))[
                    ..6
                ],
            _ => LegacySandboxFixture.Name,
        };
        return JsonSerializer.Serialize(new[] { new { name, workspaces } });
    }

    private static void AssertNoSandboxMatched(
        UserSecretsCliHarness cli,
        int exit,
        List<ProcessRequest> calls
    )
    {
        exit.Should().Be(1);
        cli.Console.AllText.Should().Contain("abox create");
        calls.Should().NotContain(call => call.Arguments[0] == "exec");
    }

    private static void RequireWindowsWorkspaceLookup()
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException(
                Xunit.v3.DynamicSkipToken.Value + "Requires Windows workspace path semantics."
            );
    }

    private static string HistoricalFolderName(string location) =>
        "folder-ai-sbx-"
        + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(location)))[..6];

    private static string WorkspaceInventory(params (string Name, string Workspace)[] sandboxes) =>
        JsonSerializer.Serialize(
            sandboxes.Select(sandbox => new
            {
                name = sandbox.Name,
                status = "running",
                workspaces = new[] { sandbox.Workspace },
            })
        );

    private static void AssertOnlyLookupOrLaunch(List<ProcessRequest> calls, string name)
    {
        calls
            .Where(call => call.Arguments[0] == "exec")
            .Should()
            .OnlyContain(call => call.Arguments.Contains(name));
        calls
            .Where(call => call.FileName == "sbx")
            .Should()
            .OnlyContain(call =>
                new[] { "version", "diagnose", "ls", "exec" }.Contains(call.Arguments[0])
            );
    }
}
