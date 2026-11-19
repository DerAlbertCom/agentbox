using Abox.Configuration;
using Abox.Features.Publish;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Naming;
using NSubstitute;

namespace Abox.Tests;

internal sealed class PublishServiceFixture
{
    internal TempDirectory Root { get; } = new();
    internal BuildStore Store { get; }
    internal IProcessRunner Runner { get; } = Substitute.For<IProcessRunner>();
    internal IHostPrograms Programs { get; } = Substitute.For<IHostPrograms>();
    internal TestConsole Console { get; } = new();
    internal List<ProcessRequest> Calls { get; } = [];
    internal List<string> Descriptors { get; } = [];
    internal List<string> Configurations { get; } = [];
    internal PublishService Service { get; }

    internal PublishServiceFixture()
    {
        Store = new BuildStore(AboxPaths.For(Root.Path, Path.Combine(Root.Path, "state")));
        Service = new PublishService(Store, Programs, new OciRegistry(Runner), Console);
        Programs.Exists("docker").Returns(true);
        Runner
            .When(runner =>
                runner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            )
            .Do(call => Calls.Add(call.Arg<ProcessRequest>()));
        Runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessRequest>();
                var arguments = request.Arguments.ToList();
                if (arguments.Contains("--file"))
                {
                    Descriptors.Add(File.ReadAllText(arguments[arguments.IndexOf("--file") + 1]));
                    var config = Path.Combine(arguments.Last(), "squad.json");
                    if (File.Exists(config))
                        Configurations.Add(File.ReadAllText(config));
                }
                return PublishTestProcess.Run(request);
            });
    }

    internal PublishRequest Request(params string[] squads) =>
        new([new SquadResolver.ResolvedTeam("sample", Root.Path, squads)], "latest");

    internal string ManifestPath(SquadBuildIdentity identity) =>
        Path.Combine(Store.DirectoryFor(identity), "build.json");

    internal void SaveBuild(
        string squad,
        string repository = "mtiae-developer-container-images-local"
    )
    {
        var workload = Path.Combine(Store.DirectoryFor(new("sample", squad, "latest")), "workload");
        PublicationBuilds.SaveWorkload(
            Store,
            new BuildManifest
            {
                Squad = squad,
                Tag = "latest",
                Reference = "agentbox-rwest-sample:latest",
                PublishRepository = repository,
                WorkloadKitDirectory = workload,
            },
            Path.Combine(workload, "workload.yaml"),
            "# syntax=docker/sandbox-kit:3\nschemaVersion: \"3\"\nkind: workload\n"
        );
    }

    internal void SaveVerifiedBuild(BuildManifest build) =>
        PublicationBuilds.SaveVerified(Store, build);

    internal void SaveRegistryKitBuild()
    {
        SaveBuild("ac");
        var build = Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        build.Kits.Add(new ManifestKit { Name = "tool", Reference = "ghcr.io/example/tool:1" });
        SaveVerifiedBuild(build);
    }
}
