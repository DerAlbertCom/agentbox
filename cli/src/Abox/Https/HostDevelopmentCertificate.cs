using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using Abox.Infrastructure;

namespace Abox.Https;

/// <summary>CLI-only export of an already eligible, exactly correlated trusted identity.</summary>
public sealed class HostDevelopmentCertificate(IProcessRunner runner, IConsole console)
{
    private readonly string temporaryParent = Path.GetTempPath();
    private readonly IReadOnlyList<DevelopmentCertificateTool>? tools;

    internal HostDevelopmentCertificate(
        IProcessRunner runner,
        IConsole console,
        string temporaryParent,
        IReadOnlyList<DevelopmentCertificateTool>? tools
    )
        : this(runner, console)
    {
        this.temporaryParent = temporaryParent;
        this.tools = tools;
    }

    public async Task<ExportedDevelopmentCertificate> ExportAsync(
        string password,
        CancellationToken token
    )
    {
        token.ThrowIfCancellationRequested();
        byte[]? bytes = null;
        try
        {
            var installed = tools ?? await InstalledToolsAsync(token);
            foreach (var tool in installed)
            {
                var before = await MetadataAsync(tool, token);
                // The CLI has no thumbprint selector. Ambiguity is not permission to
                // let it choose an untrusted candidate or generate a replacement.
                if (before.Length != 1 || !Eligible(before[0], tool.FormatVersion))
                    continue;
                using var staging = new ProtectedTemporaryDirectory(temporaryParent, console);
                var path = Path.Combine(staging.Path, "certificate.pfx");
                var result = await RunAsync(
                    tool,
                    ["https", "--export-path", path, "--password", password, "--format", "Pfx"],
                    ProcessOutputPolicy.Sensitive,
                    token,
                    staging.Path
                );
                if (!result.Succeeded)
                    throw Failure();
                var info = new FileInfo(path);
                if (
                    !info.Exists
                    || info.Length is < 1 or > 1024 * 1024
                    || (info.Attributes & FileAttributes.ReparsePoint) != 0
                )
                    throw Failure();
                bytes = await File.ReadAllBytesAsync(path, token);
                using var certificate = X509CertificateLoader.LoadPkcs12(
                    bytes,
                    password,
                    X509KeyStorageFlags.EphemeralKeySet
                );
                Validate(certificate, before[0]);
                var after = await MetadataAsync(tool, token);
                if (
                    after.Length != 1
                    || !Eligible(after[0], tool.FormatVersion)
                    || after[0] != before[0]
                )
                    throw Failure();
                var exported = new ExportedDevelopmentCertificate(bytes, password);
                bytes = null;
                return exported;
            }
            throw Failure();
        }
        catch (OperationCanceledException)
        {
            throw new OperationCanceledException(
                "Development certificate export canceled; private staging cleanup was attempted.",
                token
            );
        }
        catch (AboxException exception)
            when (exception.Message == ProtectedTemporaryDirectory.PreparationFailureMessage)
        {
            // Only our fixed staging diagnostic is safe to retain. Arbitrary
            // runner exceptions must still be sanitized by the catch below.
            throw new AboxException(ProtectedTemporaryDirectory.PreparationFailureMessage);
        }
        catch (Exception)
        {
            throw Failure();
        }
        finally
        {
            if (bytes is not null)
                CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static AboxException Failure() =>
        new(
            "Cannot export an exact existing trusted ASP.NET Core development certificate. Require one date-valid, exportable CN=localhost identity with localhost DNS SAN and Full trust, eligible for an already-installed SDK dev-certs tool's current certificate format. Inspect 'dotnet dev-certs https --check --trust' and installed SDKs; resolve missing/untrusted/ambiguous/older-format certificates explicitly, then retry. abox will not install tools, generate an alternative, export through a native-store fallback, or change host trust. Do not renew/delete certificates concurrently with export. Child diagnostics were suppressed."
        );

    private async Task<CertificateMetadata[]> MetadataAsync(
        DevelopmentCertificateTool tool,
        CancellationToken token
    )
    {
        var result = await RunAsync(
            tool,
            ["https", "--check-trust-machine-readable"],
            ProcessOutputPolicy.CapturedSensitive,
            token
        );
        if (!result.Succeeded || result.StandardOutput.Length > 1024 * 1024)
            return [];
        try
        {
            using var document = JsonDocument.Parse(
                result.StandardOutput,
                new JsonDocumentOptions { AllowDuplicateProperties = false }
            );
            return document
                .RootElement.EnumerateArray()
                .Select(value => new CertificateMetadata(
                    value.GetProperty("Thumbprint").GetString()!,
                    value.GetProperty("Subject").GetString()!,
                    value.GetProperty("Version").GetInt32(),
                    value.GetProperty("ValidityNotBefore").GetDateTime().ToUniversalTime(),
                    value.GetProperty("ValidityNotAfter").GetDateTime().ToUniversalTime(),
                    value.GetProperty("IsHttpsDevelopmentCertificate").GetBoolean(),
                    value.GetProperty("IsExportable").GetBoolean(),
                    value.GetProperty("TrustLevel").GetString()!,
                    value
                        .GetProperty("X509SubjectAlternativeNameExtension")
                        .EnumerateArray()
                        .Any(name => name.GetString() == "localhost")
                ))
                .ToArray();
        }
        catch (Exception exception)
            when (exception
                    is JsonException
                        or InvalidOperationException
                        or KeyNotFoundException
                        or FormatException
            )
        {
            return [];
        }
    }

    private static bool Eligible(CertificateMetadata value, int format) =>
        Regex.IsMatch(value.Thumbprint ?? "", "^[0-9A-Fa-f]{40}$")
        && value.Subject == "CN=localhost"
        && value.Version >= format
        && value.Before <= DateTime.UtcNow
        && value.After > DateTime.UtcNow
        && value.Development
        && value.Exportable
        && value.Trust == "Full"
        && value.Localhost;

    private static void Validate(X509Certificate2 certificate, CertificateMetadata expected)
    {
        var version = certificate
            .Extensions.Cast<X509Extension>()
            .Single(extension => extension.Oid?.Value == "1.3.6.1.4.1.311.84.1.1")
            .RawData;
        if (
            !certificate.HasPrivateKey
            || certificate.Thumbprint != expected.Thumbprint
            || certificate.Subject != expected.Subject
            || version.Length != 1
            || version[0] != expected.Version
            || certificate.NotBefore.ToUniversalTime() != expected.Before
            || certificate.NotAfter.ToUniversalTime() != expected.After
            || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow
            || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow
            || !certificate
                .Extensions.OfType<X509SubjectAlternativeNameExtension>()
                .Any(san =>
                    san.EnumerateDnsNames().Contains("localhost", StringComparer.OrdinalIgnoreCase)
                )
        )
            throw Failure();
        using var key = certificate.GetRSAPrivateKey();
        using var publicKey = certificate.GetRSAPublicKey();
        var challenge = RandomNumberGenerator.GetBytes(32);
        if (
            key is null
            || publicKey is null
            || !publicKey.VerifyData(
                challenge,
                key.SignData(challenge, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            )
        )
            throw Failure();
    }

    private Task<ProcessResult> RunAsync(
        DevelopmentCertificateTool tool,
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

    private async Task<IReadOnlyList<DevelopmentCertificateTool>> InstalledToolsAsync(
        CancellationToken token
    )
    {
        var result = await runner.RunAsync(
            new ProcessRequest(
                "dotnet",
                ["--list-sdks"],
                StandardInput: "",
                OutputPolicy: ProcessOutputPolicy.CapturedSensitive,
                Environment: new Dictionary<string, string>
                {
                    ["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false",
                }
            ),
            token
        );
        if (!result.Succeeded)
            throw Failure();
        var found = new List<DevelopmentCertificateTool>();
        foreach (var line in result.StandardOutput.Split('\n').Reverse())
        {
            var match = Regex.Match(
                line.Trim(),
                @"^(?<version>[0-9][0-9A-Za-z.\-+]*) \[(?<root>.+)\]$"
            );
            if (!match.Success)
                continue;
            var root = Path.Combine(
                match.Groups["root"].Value,
                match.Groups["version"].Value,
                "DotnetTools",
                "dotnet-dev-certs"
            );
            if (!Directory.Exists(root))
                continue;
            foreach (
                var path in Directory.EnumerateFiles(
                    root,
                    "dotnet-dev-certs.dll",
                    SearchOption.AllDirectories
                )
            )
            {
                // Read the installed tool's literal without loading/executing it.
                // Unknown tool layouts/formats fail closed instead of guessing SDK major.
                using var stream = File.OpenRead(path);
                using var pe = new PEReader(stream);
                var metadata = pe.GetMetadataReader();
                foreach (var handle in metadata.TypeDefinitions)
                {
                    var type = metadata.GetTypeDefinition(handle);
                    if (
                        metadata.GetString(type.Name) != "CertificateManager"
                        || metadata.GetString(type.Namespace)
                            != "Microsoft.AspNetCore.Certificates.Generation"
                    )
                        continue;
                    foreach (var fieldHandle in type.GetFields())
                    {
                        var field = metadata.GetFieldDefinition(fieldHandle);
                        if (
                            metadata.GetString(field.Name) != "CurrentAspNetCoreCertificateVersion"
                            || field.GetDefaultValue().IsNil
                        )
                            continue;
                        var constant = metadata.GetConstant(field.GetDefaultValue());
                        if (constant.TypeCode == ConstantTypeCode.Int32)
                        {
                            var format = metadata.GetBlobReader(constant.Value).ReadInt32();
                            if (format is >= 1 and <= 255)
                                found.Add(new DevelopmentCertificateTool(path, format));
                        }
                    }
                }
            }
        }
        return found;
    }

    private sealed record CertificateMetadata(
        string Thumbprint,
        string Subject,
        int Version,
        DateTime Before,
        DateTime After,
        bool Development,
        bool Exportable,
        string Trust,
        bool Localhost
    );
}

internal sealed record DevelopmentCertificateTool(string Path, int FormatVersion);

public sealed class ExportedDevelopmentCertificate : IDisposable
{
    private readonly byte[] pfx;

    internal ExportedDevelopmentCertificate(byte[] pfx, string password)
    {
        this.pfx = pfx;
        Password = password;
    }

    public ReadOnlyMemory<byte> Pfx => pfx;
    public string Password { get; }

    public override string ToString() => "ExportedDevelopmentCertificate (private material hidden)";

    public void Dispose() => CryptographicOperations.ZeroMemory(pfx);
}
