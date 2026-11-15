using System.Text.Json;
using Abox.Cli;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sbx;
using Abox.UserSecrets;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class UserSecretsRewriteCommandTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OnDemandAndAutomaticCopyReportOnlyRewrittenStoreKeyNamesAndCounts(
        bool automatic,
        bool verbose
    )
    {
        // Arrange
        using var root = new TempDirectory();
        var console = new TestConsole();
        var location = root.Directory_("folder");
        root.File(
            "folder/abox.json",
            automatic
                ? """{"squad":"ac","userSecrets":["sample"]}"""
                : """{"userSecrets":["sample"]}"""
        );
        root.File(
            "appdata/Microsoft/UserSecrets/sample/secrets.json",
            """
            {"Nested":{"Url":"http://localhost:8004/synthetic-private-path"},
             "Odd\nKey":"Server=127.0.0.1,8004;Password=synthetic-password",
             "Named":"http://named.internal:8004"}
            """
        );
        var sbx = Substitute.For<ISbxClient>();
        sbx.GetVersionAsync(Arg.Any<CancellationToken>()).Returns(new Version(0, 51, 0));
        sbx.DiagnoseAsync(Arg.Any<CancellationToken>()).Returns(new DiagnoseReport([]));
        sbx.GetUserSecretsHostPortsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });
        sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, ""));
        if (!automatic)
            sbx.FindSandboxAsync(SandboxName.For(location), Arg.Any<CancellationToken>())
                .Returns(new SandboxInfo(SandboxName.For(location), "running", null));
        var programs = Substitute.For<IHostPrograms>();
        programs.Exists(Arg.Any<string>()).Returns(true);
        var services = new ServiceCollection().AddAbox(console, new TrailingArguments());
        services.AddSingleton(
            new AboxConfig { DefaultTeam = "sample", DefaultLocation = location }
        );
        services.AddSingleton(AboxPaths.For(root.Directory_("config"), root.Directory_("state")));
        services.AddSingleton(new UserSecretsHost(root.Directory_("appdata"), null));
        services.AddSingleton(sbx);
        services.AddSingleton(programs);
        services.AddSingleton(PublishedSquadTest.Source());
        using var provider = services.BuildServiceProvider();

        // Act
        var exit = await provider
            .GetRequiredService<AboxRootCommand>()
            .InvokeAsync([
                .. verbose ? new[] { "--verbose" } : [],
                automatic ? "create" : "user-secrets",
            ]);

        // Assert
        exit.Should().Be(0, console.AllText);
        console
            .AllText.Should()
            .Contain("Copied user-secret store 'sample': 3 keys.")
            .And.Contain("Rewrote user-secret store 'sample': 2 keys")
            .And.Contain("\"Nested:Url\"")
            .And.Contain(JsonSerializer.Serialize("Odd\nKey"))
            .And.NotContain("synthetic-private-path")
            .And.NotContain("synthetic-password")
            .And.NotContain("Odd\nKey");
        await sbx.Received(1)
            .WriteUserSecretsAsync(
                SandboxName.For(location),
                "sample",
                Arg.Is<string>(json =>
                    json.Contains("host.docker.internal") && json.Contains("synthetic-password")
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
