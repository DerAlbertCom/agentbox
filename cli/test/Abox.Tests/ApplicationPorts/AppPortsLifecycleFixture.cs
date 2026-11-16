using Abox.Configuration;
using Abox.Features;
using Abox.Infrastructure;
using Abox.LocalState;
using Abox.Naming;
using Abox.Sandboxes;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

internal sealed class AppPortsLifecycleFixture : IDisposable
{
    private readonly UserSecretsCliHarness cli = new(existingSandbox: false);
    public TempDirectory Root => cli.Root;
    public TestConsole Console => cli.Console;
    public ISbxClient Sbx => cli.Sbx;
    public string Location => cli.Location;
    public string Name { get; set; }
    public SandboxRecordStore Records { get; }
    public IServiceCollection Services => cli.Services;
    public static readonly DateTimeOffset OldBuild = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public AppPortsLifecycleFixture()
    {
        Name = cli.Name;
        Root.File("teams/sample/ac.abox", "workload: sample\n");
        var paths = AboxPaths.For(Root.Directory_("config"), Root.Directory_("state"));
        Records = new SandboxRecordStore(paths);
        new BuildStore(paths).Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                RootDirectory = Root.Path,
                Reference = ImageName.OfTeam("sample", "latest"),
                CreatedAt = OldBuild.AddDays(1),
                LocalWorkload = LocalWorkloadTest.Artifact(Root),
            }
        );
        Sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, ""));
        Sbx.RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, ""));
        Sbx.ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>()).Returns(0);
        Services.AddSingleton(
            new AboxConfig { DefaultTeam = "sample", DefaultLocation = Location }
        );
        Services.AddSingleton(paths);
        Services.AddSingleton<IProcessRunner>(LocalWorkloadTest.Runner());
        Services.AddSingleton(PublishedSquadTest.Source());
    }

    public void Angular() =>
        Root.File(
            "folder/angular.json",
            """{"projects":{"ui":{"architect":{"serve":{"options":{"port":4200}}}}}}"""
        );

    public void Web()
    {
        Root.File("folder/api/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");
        Root.File(
            "folder/api/Properties/launchSettings.json",
            """{"profiles":{"https":{"applicationUrl":"https://localhost:8203"}}}"""
        );
        Sbx.GetDevelopmentCertificatePasswordAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new string('a', 64));
    }

    public void Configure(string json) => cli.Configure(json);

    public void HttpsOutcome(string stage, Task outcome)
    {
        if (stage == "activate")
            Sbx.ActivateDevelopmentCertificateAsync(
                    Arg.Any<string>(),
                    Arg.Any<ReadOnlyMemory<byte>>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(outcome);
        else
            Sbx.VerifyDevelopmentCertificateActivationAsync(
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(outcome);
    }

    public void Existing()
    {
        Records.Save(
            Name,
            new SandboxRecord
            {
                Team = "sample",
                Squad = "ac",
                Tag = "latest",
                BuildCreatedAt = OldBuild,
            }
        );
        Sbx.FindSandboxAsync(Name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(Name, "running", OldBuild));
    }

    public Task<int> Replace(string entry) =>
        entry == "run" ? Invoke("run") : Create(entry == "local", "--yes");

    public Task<int> CreateShared(CancellationToken token) =>
        CommandHandling.RunAsync(
            async () =>
                await cli
                    .Provider.GetRequiredService<SandboxCreator>()
                    .CreateAsync(
                        new SandboxCreationRequest(
                            new("sample", "", ["ac"]),
                            "ac",
                            Location,
                            "latest",
                            await FolderConfig.LoadAsync(Location, token),
                            AssumeYes: true
                        ),
                        token
                    ),
            Console
        );

    public Task<int> Invoke(params string[] args) => cli.Invoke(args);

    public Task<int> Create(bool local, params string[] args) =>
        Invoke([
            "create",
            "--squad",
            "ac",
            .. local ? new[] { "--local", "--root", Root.Path } : [],
            .. args,
        ]);

    public void Dispose() => cli.Dispose();
}
