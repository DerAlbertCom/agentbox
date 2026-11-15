using Abox.Cli;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.LocalState;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

/// <summary>Composed CLI with isolated source projects and synthetic Docker/sbx adapters.</summary>
internal sealed class SourceProjectFixture : IDisposable
{
    private readonly ServiceProvider _provider;

    public SourceProjectFixture(string teams = "teams", string kits = "kits")
    {
        ProjectDirectory = Root.Directory_("project");
        NestedDirectory = Path.GetFullPath(Root.Directory_("project/src/app/deep"));
        Workspace = Root.Directory_("workspace");
        ProjectKit = Path.GetFullPath(Root.Directory_($"project/{kits}/project-kit"));
        PersonalKit = Path.GetFullPath(Root.Directory_($"project/{kits}/personal-kit"));
        Root.File($"project/{teams}/sample/ac.abox", "base: cape\nkits: [project-kit]\n");
        Root.File(
            $"project/{teams}/sample/cape.abox",
            "workload: shared\nsecrets: [{service: github}]\n"
        );
        Root.File(
            $"project/{kits}/shared.workload",
            "template: example.com/base:1\npublishRepository: source-images-local\nscripts: [setup.sh]\n"
        );
        Root.File($"project/{kits}/setup.sh", "#!/bin/sh\necho owning-project-workload\n");
        Root.File(
            $"project/{kits}/project-kit/project-kit.yaml",
            """
            schemaVersion: "3"
            kind: mixin
            capabilities:
              - type: com.docker.sandbox/credential@1
                config:
                  service: github
                  phase: runtime
            """
        );
        Root.File(
            $"project/{kits}/personal-kit/personal-kit.yaml",
            "schemaVersion: \"3\"\nkind: mixin\n"
        );
        Root.File("user-config/extra.abox", "kits: [personal-kit]\n");
        var paths = AboxPaths.For(
            Path.Combine(Root.Path, "user-config"),
            Path.Combine(Root.Path, "user-state")
        );
        Programs.Exists(Arg.Any<string>()).Returns(true);
        Runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessRequest>();
                return request.Arguments.Contains("--raw") || request.Arguments.Contains("--push")
                    ? PublishTestProcess.Run(request)
                    : LocalWorkloadTest.Result(request);
            });
        Sbx.GetVersionAsync(Arg.Any<CancellationToken>()).Returns(new Version(0, 51, 0));
        Sbx.DiagnoseAsync(Arg.Any<CancellationToken>()).Returns(new DiagnoseReport([]));
        Sbx.InspectKitAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>()
            )
            .Returns(new SbxOutcome(0, ""));
        Sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, ""));
        Sbx.ListSecretsAsync(Arg.Any<CancellationToken>()).Returns(new HostSecrets([], []));
        var services = new ServiceCollection().AddAbox(Console, new TrailingArguments());
        services.AddSingleton(paths);
        services.AddSingleton(
            new AboxConfig
            {
                DefaultTeam = "sample",
                DefaultTag = "release",
                TeamsDirectory = teams,
                KitsDirectory = kits,
            }
        );
        services.AddSingleton(Programs);
        services.AddSingleton(Runner);
        services.AddSingleton(Sbx);
        _provider = services.BuildServiceProvider();
    }

    public TempDirectory Root { get; } = new();
    public TestConsole Console { get; } = new();
    public IHostPrograms Programs { get; } = Substitute.For<IHostPrograms>();
    public IProcessRunner Runner { get; } = Substitute.For<IProcessRunner>();
    public ISbxClient Sbx { get; } = Substitute.For<ISbxClient>();
    public string ProjectDirectory { get; }
    public string NestedDirectory { get; }
    public string Workspace { get; }
    public string ProjectKit { get; }
    public string PersonalKit { get; }

    public BuildManifest LoadBuild(string squad = "ac", string team = "sample") =>
        _provider.GetRequiredService<BuildStore>().Load(team, squad, "release")!;

    public async Task<int> InvokeAsync(string workingDirectory, params string[] arguments)
    {
        var originalDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(workingDirectory);
            return await _provider.GetRequiredService<AboxRootCommand>().InvokeAsync(arguments);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }

    public void Dispose()
    {
        _provider.Dispose();
        Root.Dispose();
    }
}
