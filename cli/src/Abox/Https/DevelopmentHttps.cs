using Abox.Infrastructure;
using Abox.Sbx;

namespace Abox.Https;

/// <summary>Shared on-demand HTTPS setup, independent of command and lifecycle slices.</summary>
public sealed class DevelopmentHttps(
    HostDevelopmentCertificate exporter,
    ISbxClient sbx,
    IConsole console
)
{
    public async Task ConfigureAsync(string sandbox, CancellationToken token)
    {
        var password = await sbx.GetDevelopmentCertificatePasswordAsync(sandbox, token);
        using var certificate = await exporter.ExportAsync(password, token);
        await sbx.ActivateDevelopmentCertificateAsync(
            sandbox,
            certificate.Pfx,
            certificate.Password,
            token
        );
        await sbx.VerifyDevelopmentCertificateActivationAsync(sandbox, token);
        console.WriteLine(
            "HTTPS default certificate and exact public sandbox system trust configured for fresh abox shell/direct/exec/pipe processes and their new agent children. Node extra-CA roots are preserved and merged at launch; unreadable/conflicting trust input fails actionably rather than being discarded. Existing apps/Node clients must be explicitly restarted for renewal; existing parents must be fresh. Custom endpoint certificate settings can override the default. Host/browser trust was not tested by this setup."
        );
    }
}
