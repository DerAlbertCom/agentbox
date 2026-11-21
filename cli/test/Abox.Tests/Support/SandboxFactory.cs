using Abox.ApplicationPorts;
using Abox.Configuration;
using Abox.Https;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Sandboxes;
using Abox.Sbx;

namespace Abox.Tests;

internal static class SandboxFactory
{
    public static FolderSandbox Resolver(ISbxClient sbx, CreateRecoveryHints hints) =>
        new(sbx, new ProcessRunner(), hints);

    public static CreateRecoveryHints Hints(
        AboxConfig config,
        BuildStore store,
        SandboxRecordStore records
    ) => new(config, new SquadResolver(config), store, records);

    public static AppPorts Ports(ISbxClient sbx, IConsole console) =>
        new(
            sbx,
            console,
            new SandboxHttps(
                new HostDevelopmentCertificate(LocalWorkloadFactory.Runner(), console),
                sbx,
                console
            )
        );
}
