using Abox.Infrastructure;
using Abox.Sbx;

namespace Abox.Https;

/// <summary>
/// Protected development HTTPS for one Sandbox: full configuration, the bootstrap-only
/// refresh and the launch-readiness check. The only production caller of the sbx
/// certificate operations.
/// </summary>
public sealed class SandboxHttps(
    HostDevelopmentCertificate exporter,
    ISbxClient sbx,
    IConsole console
)
{
    public async Task ConfigureAsync(string sandbox, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var password = await sbx.GetDevelopmentCertificatePasswordAsync(sandbox, token);
        using var certificate = await exporter.ExportAsync(password, token);
        token.ThrowIfCancellationRequested();
        await sbx.ActivateDevelopmentCertificateAsync(
            sandbox,
            certificate.Pfx,
            certificate.Password,
            token
        );
        token.ThrowIfCancellationRequested();
        await sbx.VerifyDevelopmentCertificateActivationAsync(sandbox, token);
        token.ThrowIfCancellationRequested();
        console.WriteLine(
            "HTTPS default certificate and exact public sandbox system trust configured for fresh abox shell/direct/exec/pipe processes and their new agent children. Node extra-CA roots are preserved and merged at launch; unreadable/conflicting trust input fails actionably rather than being discarded. Existing apps/Node clients must be explicitly restarted for renewal; existing parents must be fresh. Custom endpoint certificate settings can override the default. Host/browser trust was not tested by this setup."
        );
    }

    /// <summary>Refreshes only the verified reader; no host export, root trust or port change.</summary>
    public async Task RefreshBootstrapAsync(string sandbox, CancellationToken token)
    {
        await sbx.RefreshDevelopmentCertificateBootstrapAsync(sandbox, token);
        console.WriteLine(
            "HTTPS bootstrap refreshed from this CLI. Private activation retained; HTTPS/Node/agent readiness was not verified. Retry the original launch; use abox ports for full setup."
        );
    }

    /// <summary>
    /// Crosses the protected HTTPS bootstrap once, so a launch fails before anything starts
    /// when the bootstrap refuses.
    /// </summary>
    public async Task EnsureLaunchReadyAsync(string sandbox, CancellationToken token)
    {
        if (await sbx.ExecAsync(new SbxExecRequest(sandbox, ["true"]), token) != 0)
            throw new AboxException(
                "Herdr was not opened: sandbox HTTPS launch readiness failed. Exit stale agents and explicitly initialize fresh core/service parents through abox after saving work; rerun 'abox ports' for recovery guidance. No core or app was restarted."
            );
    }
}
