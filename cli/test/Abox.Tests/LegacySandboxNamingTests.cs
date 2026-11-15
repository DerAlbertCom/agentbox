using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class LegacySandboxNamingTests
{
    // Independent literals from Get-SandboxNameForPath/Get-AgentSbxRealPath at
    // cape-agent-sandbox 1512ff7. Unix absent-path digests cross-checked with Python hashlib.
    public static IEnumerable<object[]> HistoricalNames =>
        OperatingSystem.IsWindows()
            ?
            [
                [LegacySandboxFixture.Location, LegacySandboxFixture.Name],
                [@"c:\__agentbox_legacy_fixtures__\case_name", "case_name-ai-sbx-0c1bf5"],
                ["C:/__agentbox_legacy_fixtures__/Case_Name", LegacySandboxFixture.Name],
                [@"C:\__agentbox_legacy_fixtures__\Case_Name\", "case_name-ai-sbx-c1b1cb"],
                [@"C:\__agentbox_legacy_fixtures__\--Dot.Name!--", "dot-name-ai-sbx-3fbc82"],
                [@"C:\__agentbox_legacy_fixtures__\!!!", "root-ai-sbx-6a57d6"],
                ["C:\\__agentbox_legacy_fixtures__\\\u00c4BC_\u00e9", "bc_-ai-sbx-16ccc1"],
                [@"C:\", "c-ai-sbx-28f709"],
            ]
            :
            [
                [LegacySandboxFixture.Location, LegacySandboxFixture.Name],
                ["/__agentbox_legacy_fixtures__/case_name", "case_name-ai-sbx-e14841"],
                ["/__agentbox_legacy_fixtures__/Case_Name/", "case_name-ai-sbx-7c41da"],
                ["/__agentbox_legacy_fixtures__/--Dot.Name!--", "dot-name-ai-sbx-99b218"],
                ["/__agentbox_legacy_fixtures__/!!!", "root-ai-sbx-562235"],
                ["/__agentbox_legacy_fixtures__/\u00c4BC_\u00e9", "bc_-ai-sbx-73bfdf"],
            ];

    [Theory]
    [MemberData(nameof(HistoricalNames))]
    public async Task RunFindsTheHistoricalGeneratedName(string location, string expectedName)
    {
        using var cli = new UserSecretsCliHarness(existingSandbox: false);
        cli.Sbx.FindSandboxAsync(expectedName, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(expectedName, "running", null));

        (await cli.Invoke("run", "--location", location)).Should().Be(0, cli.Console.AllText);

        await cli
            .Sbx.Received(1)
            .ExecAsync(
                Arg.Is<SbxExecRequest>(request => request.Sandbox == expectedName),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task LinkHashUsesTheFinalTargetButThePrefixUsesTheOriginalLeaf()
    {
        using var cli = new UserSecretsCliHarness(existingSandbox: false);
        var target = cli.Root.Directory_("Final_Target");
        var link = Path.Combine(cli.Root.Path, "Alias_Name");
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
        try
        {
            // Only the dynamic target digest is calculated here; the prefix is a
            // historical literal and is deliberately different from the target leaf.
            var digest = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(target)
            );
            var expectedName =
                "alias_name-ai-sbx-" + Convert.ToHexString(digest)[..6].ToLowerInvariant();
            cli.Sbx.FindSandboxAsync(expectedName, Arg.Any<CancellationToken>())
                .Returns(new SandboxInfo(expectedName, "running", null));

            (await cli.Invoke("run", "--location", link)).Should().Be(0, cli.Console.AllText);

            await cli
                .Sbx.Received(1)
                .ExecAsync(
                    Arg.Is<SbxExecRequest>(request => request.Sandbox == expectedName),
                    Arg.Any<CancellationToken>()
                );
        }
        finally
        {
            Directory.Delete(link);
        }
    }
}
