using Abox.ApplicationPorts;
using Abox.Https;
using Abox.Infrastructure;
using Abox.Sandboxes;
using Abox.Sbx;

namespace Abox.Tests;

internal static class SandboxFactory
{
    public static SandboxResolver Resolver(ISbxClient sbx) => new(sbx, new ProcessRunner());

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
