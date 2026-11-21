using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Abox.Https;
using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

/// <summary>Synthetic CLI identity/exports only; never accesses host certificates or trust.</summary>
internal sealed class LifecycleCertificateCli : IDisposable
{
    private readonly RSA key = RSA.Create(2048);
    private readonly CertificateTestDirectory staging = new();
    public X509Certificate2 Current { get; private set; }
    public int Exports { get; private set; }
    public Action? Exported { get; set; }
    public HostDevelopmentCertificate Exporter { get; }

    public LifecycleCertificateCli(TestConsole console)
    {
        Current = Create();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Run(call.Arg<ProcessRequest>()));
        Exporter = new HostDevelopmentCertificate(
            runner,
            console,
            staging.Path,
            [new("synthetic-dev-certs", 6)]
        );
    }

    public void Renew()
    {
        Current.Dispose();
        Current = Create();
    }

    private X509Certificate2 Create()
    {
        var request = new CertificateRequest(
            "CN=localhost",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509Extension("1.3.6.1.4.1.311.84.1.1", [6], false));
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1)
        );
    }

    private ProcessResult Run(ProcessRequest request)
    {
        if (request.Arguments.Contains("--export-path"))
        {
            var args = request.Arguments.ToList();
            File.WriteAllBytes(
                args[args.IndexOf("--export-path") + 1],
                Current.Export(X509ContentType.Pfx, args[args.IndexOf("--password") + 1])
            );
            Exports++;
            Exported?.Invoke();
            return new ProcessResult(0, "private synthetic export", "");
        }
        return new ProcessResult(
            0,
            JsonSerializer.Serialize(
                new[]
                {
                    new
                    {
                        Current.Thumbprint,
                        Current.Subject,
                        Version = 6,
                        ValidityNotBefore = Current.NotBefore.ToUniversalTime(),
                        ValidityNotAfter = Current.NotAfter.ToUniversalTime(),
                        IsHttpsDevelopmentCertificate = true,
                        IsExportable = true,
                        TrustLevel = "Full",
                        X509SubjectAlternativeNameExtension = new[] { "localhost" },
                    },
                }
            ),
            "private synthetic metadata"
        );
    }

    public void Dispose()
    {
        Current.Dispose();
        key.Dispose();
        staging.Dispose();
    }
}
