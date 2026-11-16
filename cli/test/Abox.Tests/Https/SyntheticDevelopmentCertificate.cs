using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;

namespace Abox.Tests;

/// <summary>Disposable local identity; never reads or changes a certificate store.</summary>
internal sealed class SyntheticDevelopmentCertificate : IDisposable
{
    private readonly RSA key = RSA.Create(2048);
    private readonly int format;

    internal SyntheticDevelopmentCertificate(
        int format = 6,
        string dnsName = "localhost",
        DateTimeOffset? before = null,
        DateTimeOffset? after = null
    )
    {
        this.format = format;
        var request = new CertificateRequest(
            "CN=localhost",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(dnsName);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(
            new X509Extension("1.3.6.1.4.1.311.84.1.1", [(byte)format], false)
        );
        Certificate = request.CreateSelfSigned(
            before ?? DateTimeOffset.UtcNow.AddDays(-10),
            after ?? DateTimeOffset.UtcNow.AddDays(10)
        );
    }

    internal X509Certificate2 Certificate { get; }

    internal byte[] Export(string password) => Certificate.Export(X509ContentType.Pfx, password);

    internal byte[] ExportPublicOnly(string password)
    {
        using var publicOnly = X509CertificateLoader.LoadCertificate(Certificate.RawData);
        return publicOnly.Export(X509ContentType.Pfx, password);
    }

    internal JsonArray Metadata() =>
        new(
            new JsonObject
            {
                ["Thumbprint"] = Certificate.Thumbprint,
                ["Subject"] = Certificate.Subject,
                ["Version"] = format,
                ["ValidityNotBefore"] = Certificate.NotBefore.ToUniversalTime(),
                ["ValidityNotAfter"] = Certificate.NotAfter.ToUniversalTime(),
                ["IsHttpsDevelopmentCertificate"] = true,
                ["IsExportable"] = true,
                ["TrustLevel"] = "Full",
                ["X509SubjectAlternativeNameExtension"] = new JsonArray("localhost"),
            }
        );

    public void Dispose()
    {
        Certificate.Dispose();
        key.Dispose();
    }
}
