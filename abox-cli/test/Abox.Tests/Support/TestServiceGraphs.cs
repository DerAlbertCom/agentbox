using Abox.Configuration;
using Abox.Credentials;
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
            SandboxFactory.Ports(sbx, console),
            Credentials(paths, sbx, console)
        );

    /// <summary>The credential check, with the bindings of sbx in the state directory of the test.</summary>
    internal static SquadCredentials Credentials(
        AboxPaths paths,
        ISbxClient sbx,
        IConsole console
    ) =>
        new(
            new SquadResolver(new AboxConfig()),
            new PersonalConfigurations(paths),
            sbx,
            new SbxCredentialBindings(BindingsFile(paths)),
            new SecretValues(sbx, console),
            console
        );

    internal static string BindingsFile(AboxPaths paths) =>
        Path.Combine(paths.StateDirectory, "sbx", "credentials.yaml");
}
