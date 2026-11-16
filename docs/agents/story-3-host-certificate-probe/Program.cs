using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;

const string sandbox = "rwe-ai-sbx-41bc30";
try
{
    if (
        !OperatingSystem.IsWindows()
        || args
            is not [
                "--preflight"
                or "--stage"
                or "--trust-preflight"
                or "--trust"
                or "--trust-audit"
                or "--activate"
                or "--check"
                or "--selftest",
            ]
    )
        return 2;
    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    var token = deadline.Token;
    var runner = new ProcessRunner();
    await HostCertificateExport.PreflightAsync(runner, token);
    using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
    {
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        using var host = store
            .Certificates.Find(
                X509FindType.FindByThumbprint,
                "8E84EA4F2F4EC8548DAF359CCB1179008251C714",
                false
            )
            .OfType<X509Certificate2>()
            .Single();
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    phase = "intended-host-public-identity",
                    leaf_sha1 = host.Thumbprint,
                    leaf_der_sha256 = host.GetCertHashString(HashAlgorithmName.SHA256),
                    spki_sha256 = Convert.ToHexString(
                        SHA256.HashData(host.PublicKey.ExportSubjectPublicKeyInfo())
                    ),
                }
            )
        );
    }
    if (args[0] is "--preflight" or "--stage")
        await ObserveCurrentHostLeaf(token);
    var sbx = new SbxClient(
        runner,
        AboxPaths.For(AppContext.BaseDirectory, AppContext.BaseDirectory)
    );
    var selected = await sbx.FindSandboxAsync(sandbox, token);
    if (selected is not { IsRunning: true } || selected.Name != sandbox)
        throw new InvalidOperationException("Exact sandbox must already be running.");
    Console.WriteLine($"UTC: {DateTimeOffset.UtcNow:O}; sbx: {await sbx.GetVersionAsync(token)}");
    using var ports = JsonDocument.Parse(
        Require(await sbx.HostCertificateProbePortsAsync(token)).StandardOutput
    );
    var bindings = ports.RootElement;
    if (bindings.GetArrayLength() != 1)
        throw new InvalidOperationException("Expected exactly the retained mapping.");
    var binding = bindings[0];
    if (
        binding.GetProperty("host_ip").GetString() != "127.0.0.1"
        || binding.GetProperty("host_port").GetInt32() != 18203
        || binding.GetProperty("sandbox_port").GetInt32() != 60719
        || binding.GetProperty("protocol").GetString() != "tcp4"
    )
        throw new InvalidOperationException("Retained mapping differs; no mutation permitted.");
    var source = Resource("HostCertificateProbe.Receiver");
    if (args[0] == "--selftest")
        PrintReport(await sbx.HostCertificateProbeAsync(source, "selftest", null, token));
    if (args[0] is "--preflight" or "--stage")
        PrintReport(await sbx.HostCertificateProbeAsync(source, "preflight", null, token));
    if (args[0] is "--trust-preflight" or "--trust" or "--trust-audit")
    {
        var frame =
            args[0] == "--trust"
                ? "ABOX-PUBLIC-TRUST-1\n"
                    + Convert.ToBase64String(HostCertificateExport.InspectExisting())
                : "";
        PrintReport(
            await sbx.HostCertificateTrustAsync(
                Resource("HostCertificateProbe.Trust"),
                args[0] == "--trust" ? "install"
                    : args[0] == "--trust-audit" ? "audit"
                    : "check",
                frame,
                token
            )
        );
    }
    if (args[0] == "--stage")
    {
        await HostCertificateExport.StageAsync(
            runner,
            async (pfx, password, publicDer) =>
            {
                var payload = JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        version = 1,
                        pfx = Convert.ToBase64String(pfx),
                        password,
                        supervisor = Resource("HostCertificateProbe.Supervisor"),
                        public_der = Convert.ToBase64String(publicDer),
                    }
                );
                try
                {
                    var frame =
                        "ABOX-MANUAL-CERT-1\n"
                        + payload.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + "\n"
                        + Convert.ToHexStringLower(SHA256.HashData(payload))
                        + "\n"
                        + Encoding.UTF8.GetString(payload);
                    Require(await sbx.HostCertificateProbeAsync(source, "receive", frame, token));
                }
                catch
                {
                    try
                    {
                        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        PrintReport(
                            await sbx.HostCertificateProbeAsync(
                                source,
                                "cleanup-incomplete",
                                null,
                                cleanup.Token
                            )
                        );
                    }
                    catch
                    {
                        Console.Error.WriteLine(
                            "Remote partial-runtime cleanup unconfirmed; inspect only the exact protected task runtime directory before retrying."
                        );
                    }
                    throw;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(payload);
                }
            },
            token
        );
        PrintReport(await sbx.HostCertificateProbeAsync(source, "inspect-staged", null, token));
        Console.WriteLine(
            "Staging helper exits now; no API has been stopped or activated by this mode."
        );
    }
    if (args[0] == "--activate")
        PrintReport(await sbx.HostCertificateProbeAsync(source, "activate", null, token));
    if (args[0] == "--check")
    {
        PrintReport(await sbx.HostCertificateProbeAsync(source, "check", null, token));
        await VerifyHostDefaultTls(runner, token);
        Console.WriteLine(
            "PASS: manual activation serves the exact host identity; independent host/sandbox default TLS and HEAD pass. App/relay/mapping retained."
        );
    }
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(
        $"Manual host-certificate probe failed ({error.GetType().Name}); child diagnostics suppressed."
    );
    return 1;
}

static void PrintReport(ProcessResult result)
{
    // Fixed public probe scripts emit allowlisted reports, never raw subprocess output.
    using var json = JsonDocument.Parse(result.StandardOutput);
    Console.WriteLine(JsonSerializer.Serialize(json.RootElement));
    Require(result);
}

static async Task VerifyHostDefaultTls(IProcessRunner runner, CancellationToken token)
{
    var endpoints = IPGlobalProperties
        .GetIPGlobalProperties()
        .GetActiveTcpListeners()
        .Where(e => e.Port == 18203)
        .ToArray();
    if (endpoints.Length != 1 || !endpoints[0].Address.Equals(IPAddress.Loopback))
        throw new InvalidOperationException("Host mapping is not IPv4-loopback-only.");
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
    timeout.CancelAfter(TimeSpan.FromSeconds(30));
    using var tcp = new TcpClient();
    await tcp.ConnectAsync("localhost", 18203, timeout.Token);
    // No validation callback, custom chain policy, or alternate trust anchor.
    using var tls = new SslStream(tcp.GetStream(), false);
    await tls.AuthenticateAsClientAsync(
        new SslClientAuthenticationOptions { TargetHost = "localhost" },
        timeout.Token
    );
    using var leaf = X509CertificateLoader.LoadCertificate(tls.RemoteCertificate!.GetRawCertData());
    if (!leaf.RawDataMemory.Span.SequenceEqual(HostCertificateExport.InspectExisting()))
        throw new InvalidOperationException("Host TLS leaf does not equal the intended host DER.");
    Console.WriteLine(
        JsonSerializer.Serialize(
            new
            {
                phase = "post-activation-host-default-tls",
                tls_authenticated = tls.IsAuthenticated,
                leaf_sha1 = leaf.Thumbprint,
                leaf_der_sha256 = leaf.GetCertHashString(HashAlgorithmName.SHA256),
                spki_sha256 = Convert.ToHexString(
                    SHA256.HashData(leaf.PublicKey.ExportSubjectPublicKeyInfo())
                ),
                host_listener = "127.0.0.1:18203",
            }
        )
    );
    foreach (var path in new[] { "/", "/swagger/index.html" })
    {
        var response = await runner.RunAsync(
            new ProcessRequest(
                "curl.exe",
                [
                    "-q",
                    "--noproxy",
                    "*",
                    "--proto",
                    "=https",
                    "--connect-timeout",
                    "3",
                    "--max-time",
                    "8",
                    "--head",
                    "--silent",
                    "--output",
                    "NUL",
                    "--write-out",
                    "%{http_code}",
                    "https://localhost:18203" + path,
                ],
                StandardInput: "",
                OutputPolicy: ProcessOutputPolicy.CapturedSensitive
            ),
            timeout.Token
        );
        var status = response.StandardOutput.Trim();
        if (
            response.ExitCode != 0
            || status.Length != 3
            || status.Any(c => !char.IsAsciiDigit(c))
            || status[0] is < '1' or > '5'
        )
            throw new InvalidOperationException("Host default-validation HEAD failed.");
        Console.WriteLine(
            $"Host default-validation HEAD {path}: curl={response.ExitCode}; HTTP={status}."
        );
    }
}

static async Task ObserveCurrentHostLeaf(CancellationToken token)
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
    timeout.CancelAfter(TimeSpan.FromSeconds(8));
    using var tcp = new TcpClient();
    await tcp.ConnectAsync("localhost", 18203, timeout.Token);
    string? thumbprint = null;
    string? sha256 = null;
    string? spkiSha256 = null;
    string? errorsSeen = null;
    string? chainFlags = null;
    using var tls = new SslStream(
        tcp.GetStream(),
        false,
        (_, certificate, chain, errors) =>
        {
            thumbprint = certificate?.GetCertHashString();
            if (certificate is not null)
            {
                using var leaf = X509CertificateLoader.LoadCertificate(
                    certificate.GetRawCertData()
                );
                sha256 = leaf.GetCertHashString(HashAlgorithmName.SHA256);
                spkiSha256 = Convert.ToHexString(
                    SHA256.HashData(leaf.PublicKey.ExportSubjectPublicKeyInfo())
                );
            }
            errorsSeen = errors.ToString();
            chainFlags = string.Join(
                ",",
                chain?.ChainStatus.Select(status => status.Status.ToString()) ?? []
            );
            return errors == SslPolicyErrors.None;
        }
    );
    var authenticated = false;
    try
    {
        await tls.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions { TargetHost = "localhost" },
            timeout.Token
        );
        authenticated = true;
    }
    catch (System.Security.Authentication.AuthenticationException) { }
    Console.WriteLine(
        JsonSerializer.Serialize(
            new
            {
                phase = "pre-copy-host-tls",
                leaf_sha1 = thumbprint,
                leaf_der_sha256 = sha256,
                spki_sha256 = spkiSha256,
                validation_errors = errorsSeen,
                chain_flags = chainFlags,
                tls_authenticated = authenticated,
            }
        )
    );
}

static string Resource(string name)
{
    using var stream =
        typeof(SbxClient).Assembly.GetManifestResourceStream(name)
        ?? throw new InvalidOperationException("Missing public probe resource.");
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

static ProcessResult Require(ProcessResult result)
{
    if (!result.Succeeded)
        throw new InvalidOperationException(
            "Private child operation failed; diagnostics suppressed."
        );
    return result;
}
