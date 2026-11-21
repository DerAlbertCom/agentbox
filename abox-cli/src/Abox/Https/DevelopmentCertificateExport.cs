using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Abox.Infrastructure;

namespace Abox.Https;

/// <summary>Owns private staging and bytes until an exact, freshly trusted export is proven.</summary>
internal sealed class DevelopmentCertificateExport(
    IProcessRunner runner,
    IConsole console,
    string temporaryParent,
    DevelopmentCertificateTool tool
)
{
    internal async Task<DevelopmentCertificateIdentity?> ReadEligibleIdentityAsync(
        CancellationToken token
    )
    {
        var result = await RunAsync(
            ["https", "--check-trust-machine-readable"],
            ProcessOutputPolicy.CapturedSensitive,
            token
        );
        return result.Succeeded && result.StandardOutput.Length <= 1024 * 1024
            ? DevelopmentCertificateIdentity.ReadEligible(result.StandardOutput, tool.FormatVersion)
            : null;
    }

    internal async Task<ExportedDevelopmentCertificate> ExportAsync(
        DevelopmentCertificateIdentity expected,
        string password,
        CancellationToken token
    )
    {
        byte[]? bytes = null;
        using var staging = new ProtectedTemporaryDirectory(temporaryParent, console);
        try
        {
            var path = Path.Combine(staging.Path, "certificate.pfx");
            var result = await RunAsync(
                ["https", "--export-path", path, "--password", password, "--format", "Pfx"],
                ProcessOutputPolicy.Sensitive,
                token,
                staging.Path
            );
            if (!result.Succeeded)
                throw new InvalidOperationException("Certificate export failed.");
            RequireBoundedOrdinaryFile(path);
            bytes = await File.ReadAllBytesAsync(path, token);
            using var certificate = X509CertificateLoader.LoadPkcs12(
                bytes,
                password,
                X509KeyStorageFlags.EphemeralKeySet
            );
            expected.ValidatePfx(certificate);
            var after = await ReadEligibleIdentityAsync(token);
            if (after != expected)
                throw new InvalidOperationException("Certificate identity changed during export.");
            // Transfer the original bytes only after both identity proofs succeed.
            var exported = new ExportedDevelopmentCertificate(bytes, password);
            bytes = null;
            return exported;
        }
        finally
        {
            if (bytes is not null)
                CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static void RequireBoundedOrdinaryFile(string path)
    {
        var info = new FileInfo(path);
        if (
            !info.Exists
            || info.Length is < 1 or > 1024 * 1024
            || (info.Attributes & FileAttributes.ReparsePoint) != 0
        )
            throw new InvalidOperationException("Certificate export file is invalid.");
    }

    private Task<ProcessResult> RunAsync(
        string[] arguments,
        ProcessOutputPolicy policy,
        CancellationToken token,
        string? temporary = null
    ) =>
        runner.RunAsync(
            new ProcessRequest(
                "dotnet",
                ["exec", tool.Path, .. arguments],
                Environment: new Dictionary<string, string>
                {
                    ["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false",
                    ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                    ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
                    ["DOTNET_NOLOGO"] = "1",
                    // The CLI may itself create a temporary export file on Unix.
                    ["TMPDIR"] = temporary ?? temporaryParent,
                    ["TMP"] = temporary ?? temporaryParent,
                    ["TEMP"] = temporary ?? temporaryParent,
                },
                StandardInput: "",
                OutputPolicy: policy
            ),
            token
        );
}
