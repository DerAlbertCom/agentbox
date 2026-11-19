using System.Text.Json;
using Abox.Cli;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using static Abox.Tests.LegacySandboxInventoryFixture;

namespace Abox.Tests;

public sealed class LegacySandboxInventoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyLookupUsesTheExplicitLocationBeforeTheConfiguredDefault(
        bool explicitLocation
    )
    {
        // Arrange
        using var cli = new UserSecretsCliHarness(
            new AboxConfig { DefaultLocation = LegacySandboxFixture.Location },
            existingSandbox: false
        );
        var other = OperatingSystem.IsWindows()
            ? @"c:\__agentbox_legacy_fixtures__\case_name"
            : "/__agentbox_legacy_fixtures__/case_name";
        var otherName = OperatingSystem.IsWindows()
            ? "case_name-ai-sbx-0c1bf5"
            : "case_name-ai-sbx-e14841";
        cli.Sbx.FindSandboxAsync(LegacySandboxFixture.Name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(LegacySandboxFixture.Name, "running", null));
        cli.Sbx.FindSandboxAsync(otherName, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(otherName, "running", null));

        // Act
        var exitCode = await cli.Invoke(explicitLocation ? ["run", "--location", other] : ["run"]);

        // Assert
        exitCode.Should().Be(0);
        await cli
            .Sbx.Received(1)
            .ExecAsync(
                Arg.Is<SbxExecRequest>(request =>
                    request.Sandbox == (explicitLocation ? otherName : LegacySandboxFixture.Name)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task InventoryMatchesOnlyExactCandidatesAndCurrentNamesWin(
        bool current,
        bool legacy
    )
    {
        // Arrange
        using var cli = new UserSecretsCliHarness(existingSandbox: false);
        var preferred = SandboxName.For(LegacySandboxFixture.Location);
        var names = new List<string>
        {
            LegacySandboxFixture.Name + "-other",
            "Case_Name",
            "custom-workspace-name",
        };
        if (legacy)
            names.Add(LegacySandboxFixture.Name);
        if (current)
            names.Add(preferred);
        var inventory = JsonSerializer.Serialize(
            names.Select(name => new
            {
                name,
                status = "running",
                workspaces = new[] { LegacySandboxFixture.Location },
            })
        );
        var requests = UseRealSbx(cli, _ => new ProcessResult(0, inventory, ""));

        // Act
        var exitCode = await cli.Invoke("run", "--location", LegacySandboxFixture.Location);

        // Assert
        exitCode.Should().Be(current || legacy ? 0 : 1);
        var executions = requests
            .Where(request => request.FileName == "sbx" && request.Arguments[0] == "exec")
            .ToList();
        if (current || legacy)
            executions
                .Should()
                .OnlyContain(request =>
                    request.Arguments.Contains(current ? preferred : LegacySandboxFixture.Name)
                );
        else
        {
            executions.Should().BeEmpty();
            cli.Console.AllText.Should().Contain("abox create");
        }
        requests
            .Where(request => request.FileName == "sbx")
            .Should()
            .OnlyContain(request =>
                new[] { "version", "diagnose", "ls", "exec" }.Contains(request.Arguments[0])
            );
        Directory.Exists(Path.Combine(cli.Root.Path, "sandboxes")).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, 7, "", "permission denied")]
    [InlineData(false, 0, "{broken", "")]
    [InlineData(false, 0, "{}", "")]
    [InlineData(true, 7, "", "communication failed")]
    [InlineData(true, 0, "null", "")]
    public async Task InventoryFailuresAreNotAbsenceOrPermissionToFallback(
        bool legacyLookup,
        int exit,
        string output,
        string error
    )
    {
        // Arrange
        using var cli = new UserSecretsCliHarness(existingSandbox: false);
        var count = 0;
        var requests = UseRealSbx(
            cli,
            _ =>
                ++count == 1 && legacyLookup
                    ? new ProcessResult(0, "[]", "")
                    : new ProcessResult(exit, output, error)
        );

        // Act
        var exitCode = await cli.Invoke("run", "--location", LegacySandboxFixture.Location);

        // Assert
        exitCode.Should().Be(1);
        count.Should().Be(legacyLookup ? 2 : 1);
        cli.Console.AllText.Should().Contain("sbx ls").And.NotContain("Create it with");
        requests
            .Where(request => request.FileName == "sbx")
            .Should()
            .OnlyContain(request =>
                new[] { "version", "diagnose", "ls" }.Contains(request.Arguments[0])
            );
    }
}
