using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Abox.Infrastructure;

namespace Abox.Sbx;

public sealed partial class SbxClient
{
    private static readonly Lazy<string> DevelopmentCertificateReceiver = new(
        ReadDevelopmentCertificateReceiver
    );
    private const string DevelopmentCertificateBusy =
        "Protected HTTPS runtime is busy after 3 seconds; retry the operation or 'abox ports'. No complete readiness is claimed. Child diagnostics were suppressed. ";

    public Task StageDevelopmentCertificateAsync(
        string sandbox,
        ReadOnlyMemory<byte> pfx,
        string password,
        CancellationToken cancellationToken
    ) => TransferDevelopmentCertificateAsync(sandbox, pfx, password, "stage", cancellationToken);

    public async Task ActivateDevelopmentCertificateAsync(
        string sandbox,
        ReadOnlyMemory<byte> pfx,
        string password,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        string publicCertificate;
        try
        {
            using var certificate = X509CertificateLoader.LoadPkcs12(
                pfx.Span,
                password,
                X509KeyStorageFlags.EphemeralKeySet
            );
            publicCertificate = certificate.ExportCertificatePem();
            var user = await runner.RunAsync(
                new ProcessRequest(
                    ExecutableName,
                    ["exec", sandbox, "id", "-u"],
                    StandardInput: "",
                    OutputPolicy: ProcessOutputPolicy.CapturedSensitive
                ),
                cancellationToken
            );
            var uid = user.StandardOutput.Trim();
            if (
                !user.Succeeded
                || !System.Text.RegularExpressions.Regex.IsMatch(uid, "^[1-9][0-9]{0,9}$")
            )
                throw new InvalidOperationException();
            var trust = await runner.RunAsync(
                new ProcessRequest(
                    ExecutableName,
                    [
                        "exec",
                        "-i",
                        "-u",
                        "root",
                        sandbox,
                        "python3",
                        "-I",
                        "-c",
                        ReadDevelopmentCertificateResource("Abox.DevelopmentCertificateTrust"),
                        uid,
                    ],
                    StandardInput: publicCertificate,
                    OutputPolicy: ProcessOutputPolicy.Sensitive
                ),
                cancellationToken
            );
            if (!trust.Succeeded)
                throw new InvalidOperationException();
        }
        catch (OperationCanceledException)
        {
            throw new OperationCanceledException(
                "Sandbox trust setup canceled; trust may be partially updated. Retry abox ports; no complete activation is claimed.",
                cancellationToken
            );
        }
        catch (Exception)
        {
            throw new AboxException(
                "Cannot establish exact public development-certificate sandbox trust. Require sandbox root exec, Python 3.8+, an existing Debian-style ca-certificates store and /usr/sbin/update-ca-certificates on ordinary root-owned, nonmounted paths. No packages were installed and host trust was not changed. Correct the sandbox prerequisite and retry abox ports; trust may be partially updated, no complete activation is claimed. Child diagnostics were suppressed."
            );
        }
        await TransferDevelopmentCertificateAsync(
            sandbox,
            pfx,
            password,
            "activate",
            cancellationToken,
            publicCertificate
        );
    }

    public async Task<string> GetDevelopmentCertificatePasswordAsync(
        string sandbox,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProcessResult result;
        try
        {
            result = await runner.RunAsync(
                new ProcessRequest(
                    ExecutableName,
                    [
                        "exec",
                        sandbox,
                        "python3",
                        "-I",
                        "-c",
                        DevelopmentCertificateReceiverProgram,
                        "prepare",
                        Guid.NewGuid().ToString("N"),
                    ],
                    StandardInput: "",
                    OutputPolicy: ProcessOutputPolicy.CapturedSensitive
                ),
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            throw new OperationCanceledException(
                "HTTPS runtime preparation canceled.",
                cancellationToken
            );
        }
        catch (Exception)
        {
            throw new AboxException(
                "Cannot prepare protected HTTPS runtime configuration. Check Python 3.8+, non-root execution and owner-only nonmounted /tmp paths. Child diagnostics were suppressed."
            );
        }
        if (result.ExitCode == 75)
            throw new AboxException(DevelopmentCertificateBusy);
        var password = result.StandardOutput.Trim();
        if (
            !result.Succeeded
            || !System.Text.RegularExpressions.Regex.IsMatch(password, "^[0-9a-f]{64}$")
        )
            throw new AboxException(
                "Cannot prepare protected HTTPS runtime configuration. Check Python 3.8+, non-root execution and owner-only nonmounted /tmp paths. Child diagnostics were suppressed."
            );
        return password;
    }

    public async Task VerifyDevelopmentCertificateActivationAsync(
        string sandbox,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProcessResult result;
        try
        {
            result = await RunDevelopmentCertificateReceiverAsync(
                sandbox,
                "status",
                Guid.NewGuid().ToString("N"),
                null,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            throw new OperationCanceledException(
                "HTTPS activation check canceled.",
                cancellationToken
            );
        }
        catch (Exception)
        {
            throw new AboxException(
                "Cannot verify protected HTTPS launch configuration; child diagnostics were suppressed."
            );
        }
        if (result.ExitCode == 42)
            throw new AboxException(
                "HTTPS certificate copied, but existing Herdr/agent parents are stale: full activation is NOT complete. Explicitly exit existing Copilot/OpenCode/pi/Claude Code parents and open fresh ones through abox. Save work and obtain consent before stopping core-managed sessions. For an existing Herdr core, deliberately run 'abox run shell -- hc server restart --fresh' if supported by installed hc; otherwise explicitly run 'abox run shell -- hc server stop', then 'abox run shell -- hc server start'. For a stale OpenCode service, explicitly run 'abox run shell -- opencode service stop', then reopen it through abox. Nothing was restarted automatically. Then retry 'abox ports'. Already-running apps require explicit restart for renewal."
            );
        if (result.ExitCode == 43)
            throw new AboxException(
                "Cannot confirm existing agent HTTPS readiness: sandbox /proc process environments are not inspectable (permissions or process visibility). Ordinary shell/exec/pipe and recovery remain available; no existing agent is claimed configured and nothing was restarted."
            );
        if (result.ExitCode == 44)
            throw new AboxException(
                "HTTPS trust/private activation may be present, but Node extra-CA configuration is incomplete. Check NODE_EXTRA_CA_CERTS: require a user-readable regular PEM file (caller-selected symlinks are supported) of at most 1 MiB and no more than 16 distinct source-content variants. Ordinary/recovery access retains the original Node setting and protected Kestrel configuration. Correct the input or inspect unused owned node pairs and retry abox ports. No complete activation is claimed."
            );
        if (result.ExitCode == 75)
            throw new AboxException(DevelopmentCertificateBusy);
        if (!result.Succeeded)
            throw new AboxException(
                "Cannot verify protected HTTPS launch configuration. Check runtime file ownership, permissions and process visibility; no complete activation is claimed."
            );
    }

    private async Task TransferDevelopmentCertificateAsync(
        string sandbox,
        ReadOnlyMemory<byte> pfx,
        string password,
        string mode,
        CancellationToken cancellationToken,
        string? publicCertificate = null
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (
            pfx.IsEmpty
            || pfx.Length > 1024 * 1024
            || string.IsNullOrEmpty(password)
            || password.Length > 1024
            || password.Contains('\0')
        )
            throw new AboxException(
                "Invalid certificate transfer payload; no sandbox operation was started."
            );
        var data = new Dictionary<string, object>
        {
            ["version"] = 1,
            ["pfx"] = Convert.ToBase64String(pfx.Span),
            ["password"] = password,
        };
        if (publicCertificate is not null)
            data["public"] = publicCertificate;
        var json = JsonSerializer.Serialize(data);
        var bytes = Encoding.UTF8.GetBytes(json);
        string frame;
        try
        {
            frame =
                "ABOX-CERTIFICATE-1\n"
                + bytes.Length.ToString(CultureInfo.InvariantCulture)
                + "\n"
                + Convert.ToHexStringLower(SHA256.HashData(bytes))
                + "\n"
                + json;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
        var transfer = Guid.NewGuid().ToString("N");
        var busy = false;
        try
        {
            var result = await RunDevelopmentCertificateReceiverAsync(
                sandbox,
                mode,
                transfer,
                frame,
                cancellationToken
            );
            busy = result.ExitCode == 75;
            if (!result.Succeeded)
                throw new AboxException("Certificate transfer failed.");
        }
        catch (Exception exception)
        {
            // Killing the local sbx process does not prove that the remote receiver ran its finally block.
            var cleaned = false;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                cleaned = (
                    await RunDevelopmentCertificateReceiverAsync(
                        sandbox,
                        "cleanup",
                        transfer,
                        null,
                        timeout.Token
                    )
                ).Succeeded;
            }
            catch (Exception)
            {
                // Preserve the primary failure, without exposing either child's diagnostics.
            }
            if (exception is OperationCanceledException)
                throw new OperationCanceledException(
                    cleaned
                        ? "Certificate transfer canceled; retry after checking runtime state."
                        : "Certificate transfer canceled; remote temporary cleanup is unconfirmed. Remove task-owned .transfer-*.tmp files from the protected runtime certificate directory before retrying.",
                    cancellationToken
                );
            throw new AboxException(
                (
                    busy
                        ? DevelopmentCertificateBusy
                        : "Cannot transfer the development certificate. Check Python 3.8+, the non-root application user, ordinary owner-only runtime paths and mount layout. Child diagnostics were suppressed. "
                )
                    + (
                        cleaned
                            ? "Retry after correcting the destination; an existing active certificate is retained unless the completed commit was not acknowledged. Initial protected configuration may already be present; no rollback is promised."
                            : "Remote temporary cleanup is unconfirmed; remove task-owned .transfer-*.tmp files from the protected runtime certificate directory before retrying."
                    )
            );
        }
    }

    private Task<ProcessResult> RunDevelopmentCertificateReceiverAsync(
        string sandbox,
        string mode,
        string transfer,
        string? frame,
        CancellationToken cancellationToken
    ) =>
        runner.RunAsync(
            new ProcessRequest(
                ExecutableName,
                frame is null
                    ?
                    [
                        "exec",
                        sandbox,
                        "python3",
                        "-I",
                        "-c",
                        DevelopmentCertificateReceiverProgram,
                        mode,
                        transfer,
                    ]
                    :
                    [
                        "exec",
                        "-i",
                        sandbox,
                        "python3",
                        "-I",
                        "-c",
                        DevelopmentCertificateReceiverProgram,
                        mode,
                        transfer,
                    ],
                StandardInput: frame ?? string.Empty,
                OutputPolicy: ProcessOutputPolicy.Sensitive
            ),
            cancellationToken
        );

    private static string DevelopmentCertificateReceiverProgram =>
        "SOURCE = "
        + JsonSerializer.Serialize(DevelopmentCertificateReceiver.Value)
        + "\nexec(compile(SOURCE, '<abox-https>', 'exec'))";

    private static string ReadDevelopmentCertificateReceiver() =>
        ReadDevelopmentCertificateResource("Abox.DevelopmentCertificateReceiver");

    private static string ReadDevelopmentCertificateResource(string name)
    {
        using var stream =
            typeof(SbxClient).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing certificate receiver resource.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
