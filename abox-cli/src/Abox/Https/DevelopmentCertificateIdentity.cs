using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Abox.Https;

/// <summary>The complete CLI identity, retained for exact before/after correlation.</summary>
internal sealed record DevelopmentCertificateIdentity(
    string Thumbprint,
    string Subject,
    int Version,
    DateTime Before,
    DateTime After,
    bool Development,
    bool Exportable,
    string Trust,
    bool Localhost
)
{
    internal static DevelopmentCertificateIdentity? ReadEligible(string report, int format)
    {
        try
        {
            using var document = JsonDocument.Parse(
                report,
                new JsonDocumentOptions { AllowDuplicateProperties = false }
            );
            var values = document.RootElement.EnumerateArray().Select(Read).ToArray();
            return values.Length == 1 && values[0].IsEligible(format) ? values[0] : null;
        }
        catch (Exception exception)
            when (exception
                    is JsonException
                        or InvalidOperationException
                        or KeyNotFoundException
                        or FormatException
            )
        {
            return null;
        }
    }

    private static DevelopmentCertificateIdentity Read(JsonElement value) =>
        new(
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
        );

    private bool IsEligible(int format) =>
        Regex.IsMatch(Thumbprint ?? "", "^[0-9A-Fa-f]{40}$")
        && Version >= format
        && IsDateValid
        && IsTrustedDevelopmentIdentity;

    private bool IsDateValid => Before <= DateTime.UtcNow && After > DateTime.UtcNow;

    private bool IsTrustedDevelopmentIdentity =>
        Subject == "CN=localhost" && Development && Exportable && Trust == "Full" && Localhost;

    internal void ValidatePfx(X509Certificate2 certificate)
    {
        if (
            !MatchesPfxIdentity(certificate)
            || !HasLocalhostSan(certificate)
            || !HasUsablePrivateKey(certificate)
        )
            throw new InvalidOperationException(
                "Exported certificate does not prove the expected identity."
            );
    }

    private bool MatchesPfxIdentity(X509Certificate2 certificate)
    {
        var version = certificate
            .Extensions.Cast<X509Extension>()
            .Single(extension => extension.Oid?.Value == "1.3.6.1.4.1.311.84.1.1")
            .RawData;
        return certificate.Thumbprint == Thumbprint
            && certificate.Subject == Subject
            && version.Length == 1
            && version[0] == Version
            && MatchesPfxDates(certificate);
    }

    private bool MatchesPfxDates(X509Certificate2 certificate) =>
        certificate.NotBefore.ToUniversalTime() == Before
        && certificate.NotAfter.ToUniversalTime() == After
        && certificate.NotBefore.ToUniversalTime() <= DateTime.UtcNow
        && certificate.NotAfter.ToUniversalTime() > DateTime.UtcNow;

    private static bool HasLocalhostSan(X509Certificate2 certificate) =>
        certificate
            .Extensions.OfType<X509SubjectAlternativeNameExtension>()
            .Any(san =>
                san.EnumerateDnsNames().Contains("localhost", StringComparer.OrdinalIgnoreCase)
            );

    private static bool HasUsablePrivateKey(X509Certificate2 certificate)
    {
        if (!certificate.HasPrivateKey)
            return false;
        using var key = certificate.GetRSAPrivateKey();
        using var publicKey = certificate.GetRSAPublicKey();
        var challenge = RandomNumberGenerator.GetBytes(32);
        return key is not null
            && publicKey is not null
            && publicKey.VerifyData(
                challenge,
                key.SignData(challenge, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );
    }
}
