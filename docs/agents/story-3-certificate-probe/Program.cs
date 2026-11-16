using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sbx;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

try
{
    if (args is ["host", var location, var template])
        return await Host(location, template);
    if (
        !OperatingSystem.IsLinux()
        || Environment.UserName == "root"
        || Environment
            .GetEnvironmentVariable("SANDBOX_NAME")
            ?.StartsWith("abox-issue46-", StringComparison.Ordinal) != true
    )
        throw new InvalidOperationException(
            "Runtime modes require the disposable non-root Linux user."
        );
    if (
        Environment
            .GetEnvironmentVariables()
            .Keys.Cast<string>()
            .Any(key => key.Contains("Kestrel", StringComparison.OrdinalIgnoreCase))
    )
        throw new InvalidOperationException("Runtime must have no Kestrel environment settings.");
    return await Runtime(args);
}
catch (Exception exception)
{
    // Never print raw child diagnostics or certificate/private-key error details.
    Console.Error.WriteLine($"Synthetic certificate probe failed ({exception.GetType().Name}).");
    return 1;
}

static async Task<int> Host(string location, string template)
{
    var workspace = Path.GetFullPath(location);
    if (
        Directory.Exists(workspace)
        || File.Exists(workspace)
        || !Path.GetFileName(workspace).StartsWith("abox-issue46-", StringComparison.Ordinal)
    )
        throw new InvalidOperationException("A fresh task-owned workspace is required.");
    var runner = new ProcessRunner();
    var sbx = new SbxClient(runner, AboxPaths.For(workspace, workspace));
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
    var token = timeout.Token;
    var name = SandboxName.For(workspace);
    Console.WriteLine($"sbx {await sbx.GetVersionAsync(token)}; disposable sandbox {name}");
    if (await sbx.FindSandboxAsync(name, token) is not null)
        throw new InvalidOperationException("Sandbox already exists.");
    Directory.CreateDirectory(workspace);
    var attemptedCreate = false;
    try
    {
        Require(
            await runner.RunAsync(
                new ProcessRequest(
                    "dotnet",
                    [
                        "publish",
                        "docs/agents/story-3-certificate-probe/CertificateProbe.csproj",
                        "-c",
                        "Debug",
                        "-r",
                        "linux-x64",
                        "--self-contained",
                        "true",
                        "-o",
                        Path.Combine(workspace, "payload"),
                    ],
                    Environment: new Dictionary<string, string>
                    {
                        ["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false",
                        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                    },
                    OutputPolicy: ProcessOutputPolicy.CapturedSensitive
                ),
                token
            ),
            "Publish self-contained .NET 10.0.15 probe"
        );
        attemptedCreate = true;
        Require(
            await sbx.CertificateProbeCreateAsync(workspace, name, template, token),
            "Create disposable sandbox"
        );
        // Fixed script only discovers the mount and enables the synthetic apphost.
        var discover = await sbx.CertificateProbeExecAsync(
            name,
            [
                "sh",
                "-c",
                "chmod +x \"$WORKSPACE_DIR/payload/CertificateProbe\"; printf %s \"$WORKSPACE_DIR\"",
            ],
            token
        );
        Require(discover, "Discover workspace");
        var executable = discover.StandardOutput.Trim() + "/payload/CertificateProbe";
        await Setup("initial");
        await ServeAndCheck("host", false);
        await Setup("competitor");
        await Setup("repeat");
        await ServeAndCheck("competitor", true);
        await Setup("renew");
        await ServeAndCheck("competitor", true);
        await Setup("inventory");
        Console.WriteLine(
            "PASS: persistent fallback works alone; newer-format competitor defeats import and renewal."
        );
        return 0;

        async Task Setup(string action)
        {
            var result = await sbx.CertificateProbeExecAsync(name, [executable, action], token);
            Require(result, action + " setup process exited");
            Console.Write(result.StandardOutput);
        }

        async Task ServeAndCheck(string selected, bool rejectHost)
        {
            using var serverTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            serverTimeout.CancelAfter(TimeSpan.FromSeconds(45));
            var server = sbx.CertificateProbeExecAsync(
                name,
                [executable, "serve"],
                serverTimeout.Token
            );
            try
            {
                if (rejectHost)
                    Require(
                        await sbx.CertificateProbeExecAsync(
                            name,
                            [executable, "reject-host"],
                            token
                        ),
                        "Strict TLS with imported-host-only anchor rejects competitor"
                    );
                var check = await sbx.CertificateProbeExecAsync(
                    name,
                    [executable, "client", selected],
                    token
                );
                Require(check, "Strict localhost TLS and selected leaf identity");
                Console.Write(check.StandardOutput);
                Require(await server, "Direct-argv Kestrel process exited");
            }
            finally
            {
                serverTimeout.Cancel();
                try
                {
                    await server;
                }
                catch (OperationCanceledException) { }
            }
        }
    }
    finally
    {
        if (attemptedCreate)
        {
            var removed = await sbx.RemoveSandboxAsync(name, CancellationToken.None);
            Console.WriteLine($"Task-owned sandbox cleanup: exit {removed.ExitCode}");
            if (!removed.Succeeded)
                Console.Error.WriteLine($"CLEANUP REQUIRED: {name}");
        }
        Console.WriteLine($"Synthetic-only workspace retained: {workspace}");
    }
}

static async Task<int> Runtime(string[] args)
{
    var state = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".local/state/abox-issue46-certificate-probe"
    );
    Directory.CreateDirectory(
        state,
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
    );
    using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
    store.Open(
        args is ["initial" or "competitor" or "repeat" or "renew"]
            ? OpenFlags.ReadWrite
            : OpenFlags.ReadOnly
    );
    switch (args)
    {
        case ["initial"]:
            if (store.Certificates.Count != 0)
                throw new InvalidOperationException(
                    "Probe requires an initially empty disposable store."
                );
            Add("host", 4, -30);
            Add("unrelated", null, -20);
            break;
        case ["competitor"]:
            Add("competitor", 6, -1);
            break;
        case ["repeat"]:
            // Same-certificate addition is idempotent; no unrelated removals.
            using (var original = Find("host"))
                store.Add(original);
            break;
        case ["renew"]:
            using (var old = Find("host"))
                store.Remove(old);
            Add("host", 4, -0.01);
            break;
        case ["inventory"]:
            if (store.Certificates.Count != 3)
                throw new InvalidOperationException("Unexpected duplicate or missing certificate.");
            foreach (var label in new[] { "host", "competitor", "unrelated" })
            {
                using var cert = Find(label);
                Console.WriteLine(
                    $"Preserved {label}: {cert.Thumbprint}; private-key={cert.HasPrivateKey}"
                );
            }
            break;
        case ["serve"]:
            store.Close();
            File.Delete(Path.Combine(state, "ready"));
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory, Args = [] }
            );
            builder.Logging.ClearProviders();
            // No certificate configuration: exercise Kestrel's actual store fallback.
            builder.WebHost.UseUrls("https://127.0.0.1:0");
            await using (var app = builder.Build())
            {
                app.MapGet(
                    "/",
                    (HttpContext context) =>
                    {
                        context.Response.OnCompleted(() =>
                        {
                            app.Lifetime.StopApplication();
                            return Task.CompletedTask;
                        });
                        return "abox-issue46-synthetic";
                    }
                );
                await app.StartAsync();
                var address = app
                    .Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()!
                    .Addresses.Single();
                await File.WriteAllTextAsync(
                    Path.Combine(state, "ready"),
                    new Uri(address).Port.ToString()
                );
                await app.WaitForShutdownAsync();
            }
            File.Delete(Path.Combine(state, "ready"));
            break;
        case ["client", var label]:
            await Client(label, false);
            break;
        case ["reject-host"]:
            await Client("host", true);
            break;
        default:
            throw new InvalidOperationException("Unknown probe mode.");
    }
    return 0;

    X509Certificate2 Find(string label)
    {
        using var publicCert = X509CertificateLoader.LoadCertificateFromFile(
            Path.Combine(state, label + ".cer")
        );
        var stored = store
            .Certificates.Cast<X509Certificate2>()
            .Single(c => c.RawData.AsSpan().SequenceEqual(publicCert.RawData));
        if (!stored.HasPrivateKey)
            throw new InvalidOperationException("Missing synthetic stored private key.");
        return stored;
    }

    void Add(string label, byte? version, double startDays)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                true
            )
        );
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true)
        );
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        request.CertificateExtensions.Add(san.Build(true));
        if (version is { } value)
            request.CertificateExtensions.Add(
                new X509Extension("1.3.6.1.4.1.311.84.1.1", [value], false)
            );
        using var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(startDays),
            DateTimeOffset.UtcNow.AddDays(365)
        );
        // Model importing unchanged synthetic source bytes, not minting a preferred identity.
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var pfx = cert.Export(X509ContentType.Pkcs12, password);
        try
        {
            using var imported = X509CertificateLoader.LoadPkcs12(
                pfx,
                password,
                X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable
            );
            if (!imported.RawData.AsSpan().SequenceEqual(cert.RawData) || !imported.HasPrivateKey)
                throw new InvalidOperationException(
                    "Import changed the source certificate identity."
                );
            store.Add(imported);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pfx);
        }
        File.WriteAllBytes(Path.Combine(state, label + ".cer"), cert.Export(X509ContentType.Cert));
        Console.WriteLine(
            $"Synthetic {label}: {cert.Thumbprint}; dev-version={version}; runtime={Environment.Version}"
        );
    }

    async Task Client(string label, bool expectRejection)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ready = Path.Combine(state, "ready");
        while (!File.Exists(ready))
            await Task.Delay(100, timeout.Token);
        var port = int.Parse(await File.ReadAllTextAsync(ready, timeout.Token));
        using var anchor = X509CertificateLoader.LoadCertificateFromFile(
            Path.Combine(state, label + ".cer")
        );
        using var socket = new TcpClient();
        await socket.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
        using var tls = new SslStream(socket.GetStream());
        var policy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            DisableCertificateDownloads = true,
        };
        policy.CustomTrustStore.Add(anchor);
        try
        {
            await tls.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = "localhost",
                    CertificateChainPolicy = policy,
                },
                timeout.Token
            );
        }
        catch (AuthenticationException) when (expectRejection)
        {
            Console.WriteLine(
                "Expected chain rejection with imported-host-only ephemeral trust anchor."
            );
            return;
        }
        if (
            expectRejection
            || !tls.RemoteCertificate!.GetRawCertData().AsSpan().SequenceEqual(anchor.RawData)
        )
            throw new InvalidOperationException("Unexpected selected identity or trust success.");
        await tls.WriteAsync(
            "GET / HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n"u8.ToArray(),
            timeout.Token
        );
        using var reader = new StreamReader(tls);
        var response = await reader.ReadToEndAsync(timeout.Token);
        if (!response.Contains("abox-issue46-synthetic", StringComparison.Ordinal))
            throw new InvalidOperationException("Missing synthetic HTTP response.");
        Console.WriteLine(
            $"Validated localhost, chain, HTTP marker and {label} leaf {anchor.Thumbprint}; runtime={Environment.Version}"
        );
    }
}

static void Require(ProcessResult result, string label)
{
    Console.WriteLine($"{label}: exit {result.ExitCode}");
    if (!result.Succeeded)
        throw new InvalidOperationException("Probe child failed; diagnostics suppressed.");
}
