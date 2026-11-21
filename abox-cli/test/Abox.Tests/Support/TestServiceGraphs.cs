using Abox.Configuration;
using Abox.Features.Build;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Sandboxes;
using Abox.Sbx;
using Abox.UserSecrets;

namespace Abox.Tests;

/// <summary>Shared wiring only; callers supply their runtime, configuration and source policies.</summary>
internal static class TestServiceGraphs
{
    internal static BuildService Build(
        SquadResolver resolver,
        PersonalConfigurations personal,
        BuildStore store,
        LocalWorkloadImages images,
        ISbxClient sbx,
        IConsole console
    ) =>
        new(
            resolver,
            new WorkloadKitGenerator(),
            new StateMixinGenerator(),
            personal,
            store,
            images,
            new KitBuildStep(new ProcessRunner(), console),
            sbx,
            console
        );

    internal static SandboxCreator Creator(
        SandboxRecordStore records,
        AboxPaths paths,
        ISbxClient sbx,
        IConsole console,
        LocalSquadSource local,
        RemoteSquadSource remote,
        UserSecretsCopy copy
    ) =>
        new(
            records,
            paths,
            SandboxFactory.Resolver(
                sbx,
                SandboxFactory.Hints(new AboxConfig(), new BuildStore(paths), records)
            ),
            sbx,
            console,
            local,
            remote,
            copy,
            SandboxFactory.Ports(sbx, console)
        );
}
