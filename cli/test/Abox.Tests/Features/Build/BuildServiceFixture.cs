using Abox.Configuration;
using Abox.Features.Build;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

internal sealed class BuildServiceFixture
{
    internal TempDirectory Root { get; } = new();
    internal ISbxClient Sbx { get; } = Substitute.For<ISbxClient>();
    internal IProcessRunner Runner { get; } = LocalWorkloadFactory.Runner();
    internal TestConsole Console { get; } = new();
    internal BuildStore Store { get; }
    internal SquadResolver Resolver { get; } = new(new AboxConfig());
    internal BuildService Service { get; }

    internal BuildServiceFixture()
    {
        var paths = AboxPaths.For(
            Path.Combine(Root.Path, "config"),
            Path.Combine(Root.Path, "state")
        );
        Store = new BuildStore(paths);
        Root.File("teams/sample/ac.abox", "workload: sample\n");
        Root.File("teams/sample/sample.workload", "template: example.com/base:1\n");
        Root.File("config/personal.abox", "kits: [private]\n");
        Root.File("kits/private/private.yaml", "schemaVersion: \"3\"\nkind: mixin\n");
        Sbx.InspectKitAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<IReadOnlyDictionary<string, string>>()
            )
            .Returns(new SbxOutcome(0, ""));
        Service = TestServiceGraphs.Build(
            Resolver,
            new PersonalConfigurations(paths),
            Store,
            LocalWorkloadFactory.Images(Runner),
            Sbx,
            Console
        );
    }
}
