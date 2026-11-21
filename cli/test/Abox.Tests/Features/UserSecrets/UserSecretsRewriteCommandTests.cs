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
        var location = root.CreateDirectory("folder");
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
            sbx.ListSandboxesAsync(Arg.Any<CancellationToken>())
                .Returns(new[] { new SandboxInfo(SandboxName.For(location), "running", null) });
        var programs = Substitute.For<IHostPrograms>();
        programs.Exists(Arg.Any<string>()).Returns(true);
        var paths = AboxPaths.For(root.CreateDirectory("config"), root.CreateDirectory("state"));
        using var application = new CliTestApplication(console, paths, new TrailingArguments());
        var config = new AboxConfig { DefaultTeam = "sample", DefaultLocation = location };
        application.Services.AddSingleton(config);
        var host = new UserSecretsHost(root.CreateDirectory("appdata"), null);
        application.Services.AddSingleton(host);
        application.Services.AddSingleton(sbx);
        application.Services.AddSingleton(programs);
        application.Services.AddSingleton(PublishedSquadFactory.Source());
        var provider = application.Provider;

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
