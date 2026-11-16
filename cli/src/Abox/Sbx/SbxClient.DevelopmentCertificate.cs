using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Abox.Infrastructure;

namespace Abox.Sbx;

public sealed partial class SbxClient
{
    private static readonly Lazy<string> DevelopmentCertificateReceiver = new(
        ReadDevelopmentCertificateReceiver
    );

    public async Task StageDevelopmentCertificateAsync(
        string sandbox,
        ReadOnlyMemory<byte> pfx,
        string password,
        CancellationToken cancellationToken
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
        var json = JsonSerializer.Serialize(
            new
            {
                version = 1,
                pfx = Convert.ToBase64String(pfx.Span),
                password,
            }
        );
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
        try
        {
            var result = await RunDevelopmentCertificateReceiverAsync(
                sandbox,
                "stage",
                transfer,
                frame,
                cancellationToken
            );
            if (!result.Succeeded)
                throw new AboxException("Certificate staging failed.");
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
                        ? "Certificate staging canceled; retry after checking runtime state."
                        : "Certificate staging canceled; remote temporary cleanup is unconfirmed. Remove task-owned .transfer-*.tmp files from the protected runtime certificate directory before retrying.",
                    cancellationToken
                );
            throw new AboxException(
                "Cannot stage the development certificate. Check Python 3.8+, the non-root application user, ordinary owner-only runtime paths and mount layout. Child diagnostics were suppressed. "
                    + (
                        cleaned
                            ? "Retry after correcting the destination; any previously staged bundle is retained unless the completed commit was not acknowledged."
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
                        DevelopmentCertificateReceiver.Value,
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
                        DevelopmentCertificateReceiver.Value,
                        mode,
                        transfer,
                    ],
                StandardInput: frame ?? string.Empty,
                OutputPolicy: ProcessOutputPolicy.Sensitive
            ),
            cancellationToken
        );

    private static string ReadDevelopmentCertificateReceiver()
    {
        using var stream =
            typeof(SbxClient).Assembly.GetManifestResourceStream(
                "Abox.DevelopmentCertificateReceiver"
            ) ?? throw new InvalidOperationException("Missing certificate receiver resource.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
