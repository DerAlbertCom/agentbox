using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Abox.ApplicationPorts;
using Abox.Configuration;
using Abox.Https;
using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sbx;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

try
{
    if (args is ["cleanup", var ownedWorkspace])
    {
        var path = Path.GetFullPath(ownedWorkspace);
        if (
            !Path.GetFileName(path).StartsWith("abox-issue46-activation-", StringComparison.Ordinal)
            || Path.GetFileName(Path.GetDirectoryName(path)) != ".abox"
            || (
                Directory.Exists(path)
                && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
            )
        )
            throw new InvalidOperationException();
        var client = new SbxClient(new ProcessRunner(), AboxPaths.For(path, path));
        var name = SandboxName.For(path);
        if (await client.FindSandboxAsync(name, CancellationToken.None) is not null)
            Require(
                (await client.RemoveSandboxAsync(name, CancellationToken.None)).Succeeded,
                "remove exact task-owned sandbox"
            );
        Require(
            await client.FindSandboxAsync(name, CancellationToken.None) is null,
            "absent exact task-owned sandbox"
        );
        if (Directory.Exists(path))
            Directory.Delete(path, true);
        Console.WriteLine("Exact task-owned sandbox/workspace cleanup confirmed.");
        return 0;
    }
    if (args is ["templates"])
    {
        var client = new SbxClient(
            new ProcessRunner(),
            AboxPaths.For(Environment.CurrentDirectory, Environment.CurrentDirectory)
        );
        foreach (var cached in await client.ListTemplatesAsync(CancellationToken.None))
            Console.WriteLine(cached.Repository + ":" + cached.Tag);
        return 0;
    }
    if (args is ["host", var template])
        return await Host(template);
    if (args is ["prepare-production", var productionTemplate])
        return await PrepareProduction(productionTemplate);
    if (args is ["tool-help", var toolTemplate])
        return await Host(toolTemplate, true);
    if (args is ["setup", var sandbox, var publicPath])
    {
        if (!sandbox.StartsWith("abox-issue46-activation-", StringComparison.Ordinal))
            throw new InvalidOperationException();
        using var runner = new SyntheticExportRunner();
        var client = new SbxClient(
            runner,
            AboxPaths.For(Environment.CurrentDirectory, Environment.CurrentDirectory)
        );
        var setupWorkspace = Path.GetDirectoryName(Path.GetFullPath(publicPath))!;
        await File.WriteAllTextAsync(
            Path.Combine(setupWorkspace, "SyntheticWeb.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />"
        );
        var staging = Path.Combine(
            Directory.GetParent(setupWorkspace)!.FullName,
            "certificate-tests"
        );
        Directory.CreateDirectory(staging);
        var console = new Abox.Infrastructure.SystemConsole();
        var exporter = new HostDevelopmentCertificate(
            runner,
            console,
            staging,
            [new("synthetic-dev-certs.dll", 6)]
        );
        await new AppPorts(
            client,
            console,
            new DevelopmentHttps(exporter, client, console)
        ).PublishAsync(setupWorkspace, sandbox, 10000, CancellationToken.None);
        Require(
            (
                await client.CertificateProbeExecAsync(
                    sandbox,
                    [
                        "python3",
                        "-I",
                        "-c",
                        "import hashlib,os,sys; p='/tmp/abox-https-'+str(os.geteuid())+'/active.pfx'; assert hashlib.sha256(open(p,'rb').read()).hexdigest()==sys.argv[1]",
                        runner.PfxDigest!,
                    ],
                    CancellationToken.None
                )
            ).Succeeded,
            "unchanged exported PFX bytes at runtime"
        );
        await File.WriteAllBytesAsync(publicPath, runner.PublicDer);
        return 0;
    }
    if (
        !OperatingSystem.IsLinux()
        || Environment.UserName == "root"
        || Environment
            .GetEnvironmentVariable("SANDBOX_NAME")
            ?.StartsWith("abox-issue46-activation-", StringComparison.Ordinal) != true
    )
        throw new InvalidOperationException();
    var workspace = Environment.GetEnvironmentVariable("WORKSPACE_DIR")!;
    if (args is ["observe"])
    {
        for (var index = 0; index < 200; index++)
        {
            using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                Environment.GetEnvironmentVariable(
                    "ASPNETCORE_Kestrel__Certificates__Default__Path"
                )!,
                Environment.GetEnvironmentVariable(
                    "ASPNETCORE_Kestrel__Certificates__Default__Password"
                ),
                X509KeyStorageFlags.EphemeralKeySet
            );
            if (!certificate.HasPrivateKey)
                throw new InvalidOperationException();
            if (index == 0)
                await File.WriteAllTextAsync(Path.Combine(workspace, "observer-ready"), "ready");
            await Task.Delay(20);
        }
        return 0;
    }
    if (args is ["serve"])
    {
        var builder = WebApplication.CreateBuilder([]);
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("https://127.0.0.1:0");
        await using var app = builder.Build();
        app.MapGet("/", () => "abox-activation-exact-leaf");
        await app.StartAsync();
        var address = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "ready"),
            new Uri(address).Port.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        while (!File.Exists(Path.Combine(workspace, "stop")))
            await Task.Delay(50, timeout.Token);
        await app.StopAsync();
        return 0;
    }
    if (args is ["client"])
    {
        using var cert = X509CertificateLoader.LoadCertificateFromFile(
            Path.Combine(workspace, "expected.cer")
        );
        var port = int.Parse(
            await File.ReadAllTextAsync(Path.Combine(workspace, "ready")),
            System.Globalization.CultureInfo.InvariantCulture
        );
        using var socket = new TcpClient();
        await socket.ConnectAsync("127.0.0.1", port);
        await using var tls = new SslStream(socket.GetStream());
        var policy = new X509ChainPolicy
        {
            RevocationMode = X509RevocationMode.NoCheck,
            DisableCertificateDownloads = true,
        };
        await tls.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions
            {
                TargetHost = "localhost",
                CertificateChainPolicy = policy,
            }
        );
        if (
            tls.RemoteCertificate is null
            || !tls.RemoteCertificate.GetRawCertData().AsSpan().SequenceEqual(cert.RawData)
        )
            throw new InvalidOperationException();
        await tls.WriteAsync(
            "GET / HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n"u8.ToArray()
        );
        using var reader = new StreamReader(tls);
        if (
            !(await reader.ReadToEndAsync()).Contains(
                "abox-activation-exact-leaf",
                StringComparison.Ordinal
            )
        )
            throw new InvalidOperationException();
        await File.WriteAllTextAsync(Path.Combine(workspace, "stop"), "stop");
        return 0;
    }
    throw new InvalidOperationException();
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        "Activation probe failed: " + exception.GetType().Name + "; private diagnostics suppressed."
    );
    if (exception.Message.StartsWith("Verification step failed: ", StringComparison.Ordinal))
        Console.Error.WriteLine(exception.Message);
    return 1;
}

static async Task<int> Host(string template, bool toolHelp = false)
{
    var checkout = new DirectoryInfo(AppContext.BaseDirectory);
    while (
        checkout is not null && !File.Exists(Path.Combine(checkout.FullName, "CODING_STANDARDS.md"))
    )
        checkout = checkout.Parent;
    if (checkout is null)
        throw new InvalidOperationException();
    var workspace = Path.Combine(
        checkout.FullName,
        ".abox",
        "abox-issue46-activation-" + Guid.NewGuid().ToString("N")[..8]
    );
    var runner = new ProcessRunner();
    var client = new SbxClient(runner, AboxPaths.For(workspace, workspace));
    var name = SandboxName.For(workspace);
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
    var token = timeout.Token;
    if (Directory.Exists(workspace) || await client.FindSandboxAsync(name, token) is not null)
        throw new InvalidOperationException();
    Directory.CreateDirectory(workspace);
    var created = false;
    try
    {
        var publish = await runner.RunAsync(
            new ProcessRequest(
                "dotnet",
                [
                    "publish",
                    Path.Combine(
                        checkout.FullName,
                        "docs/verification/https/ActivationProbe.csproj"
                    ),
                    "-c",
                    BuildConfiguration(),
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
                },
                OutputPolicy: ProcessOutputPolicy.CapturedSensitive
            ),
            token
        );
        if (!publish.Succeeded)
            Console.Error.WriteLine(publish.StandardOutput + publish.StandardError); // Synthetic build only, before any key exists.
        Require(publish.Succeeded, "publish .NET10 synthetic app");
        created = true;
        Require(
            (await client.CertificateProbeCreateAsync(workspace, name, template, token)).Succeeded,
            "create disposable"
        );
        var found = await client.CertificateProbeExecAsync(
            name,
            [
                "sh",
                "-c",
                "chmod +x \"$WORKSPACE_DIR/payload/ActivationProbe\"; printf %s \"$WORKSPACE_DIR\"",
            ],
            token
        );
        Require(found.Succeeded, "workspace");
        var executable = found.StandardOutput.Trim() + "/payload/ActivationProbe";
        foreach (var program in new[] { "hc", "copilot", "opencode", "pi", "claude", "dotnet" })
        {
            var installed = await client.HasProgramAsync(name, program, token);
            var version =
                installed && program != "dotnet"
                    ? await client.CertificateProbeExecAsync(name, [program, "--version"], token)
                    : null;
            var number = version is null
                ? "not checked"
                : System
                    .Text.RegularExpressions.Regex.Match(
                        version.StandardOutput,
                        @"[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.\-]+)?"
                    )
                    .Value;
            Console.WriteLine($"Disposable installed {program}: {installed}; version {number}");
        }
        if (toolHelp)
        {
            foreach (
                var command in new string[][]
                {
                    ["hc", "server", "restart", "--help"],
                    ["opencode", "serve", "--help"],
                    ["opencode", "api", "--help"],
                    ["copilot", "workflow", "--help"],
                    ["copilot", "workflow", "run", "--help"],
                }
            )
            {
                var help = await client.CertificateProbeExecAsync(name, command, token);
                var target = Path.Combine(
                    checkout.FullName,
                    ".abox",
                    "activation-help-"
                        + string.Join('-', command.Where(arg => arg != "--help"))
                        + ".txt"
                );
                await File.WriteAllTextAsync(
                    target,
                    help.StandardOutput + help.StandardError,
                    token
                );
                Console.WriteLine(
                    "Help-only command " + string.Join(' ', command) + ": exit " + help.ExitCode
                );
            }
            return 0;
        }
        string? previousPassword = null;
        for (var generation = 0; generation < 2; generation++)
        {
            Task<int>? observer = null;
            if (generation == 1)
            {
                observer = client.ExecAsync(
                    new SbxExecRequest(name, [executable, "observe"]),
                    token
                );
                for (
                    var attempt = 0;
                    attempt < 100
                        && !File.Exists(Path.Combine(workspace, "observer-ready"))
                        && !observer.IsCompleted;
                    attempt++
                )
                    await Task.Delay(50, token);
                Require(
                    File.Exists(Path.Combine(workspace, "observer-ready")),
                    "concurrent reader initialized before renewal"
                );
            }
            // A separate completed host setup process is gone before EVERY app launch.
            Require(
                (
                    await runner.RunAsync(
                        new ProcessRequest(
                            "dotnet",
                            [
                                typeof(Program).Assembly.Location,
                                "setup",
                                name,
                                Path.Combine(workspace, "expected.cer"),
                            ],
                            OutputPolicy: ProcessOutputPolicy.CapturedSensitive
                        ),
                        token
                    )
                ).Succeeded,
                "independent provisioning helper exited"
            );
            var password = await client.GetDevelopmentCertificatePasswordAsync(name, token);
            if (previousPassword is not null)
                Require(password == previousPassword, "stable sandbox password across refresh");
            previousPassword = password;
            if (observer is not null)
            {
                Require(
                    await observer == 0,
                    "concurrent old-parent PFX reader survived atomic renewal with stable password"
                );
                Console.WriteLine(
                    "PASS: concurrent old-parent PFX loads stayed valid across renewal; stable password, no obsolete generations."
                );
            }
            foreach (var shell in new[] { "direct", "sh", "bash" })
            {
                File.Delete(Path.Combine(workspace, "ready"));
                File.Delete(Path.Combine(workspace, "stop"));
                string[] command =
                    shell == "direct"
                        ? [executable, "serve"]
                        : [shell, "-c", "exec \"$1\" serve", "_", executable];
                var server = client.ExecAsync(new SbxExecRequest(name, command), token);
                for (
                    var attempt = 0;
                    attempt < 120
                        && !File.Exists(Path.Combine(workspace, "ready"))
                        && !server.IsCompleted;
                    attempt++
                )
                    await Task.Delay(100, token);
                Require(
                    File.Exists(Path.Combine(workspace, "ready")),
                    "new Kestrel ready via " + shell
                );
                Require(
                    (
                        await client.CertificateProbeExecAsync(name, [executable, "client"], token)
                    ).Succeeded,
                    "strict sandbox system-store localhost TLS exact leaf"
                );
                Require(await server == 0, "server exited");
                Console.WriteLine(
                    $"PASS generation {generation}: post-helper-exit {shell} new Kestrel exact public DER, hostname and sandbox system-store chain validated (synthetic identity, not host trust)."
                );
            }
            if (generation == 0)
            {
                foreach (var file in new[] { "active.pfx", "config.json" })
                foreach (var fault in new[] { "mode", "link", "hardlink" })
                {
                    await MutateFile(file, fault, false);
                    var unsafeRejected =
                        await client.ExecAsync(new SbxExecRequest(name, ["true"]), token) == 78;
                    Require(unsafeRejected, "reject unsafe activation file " + file + " " + fault);
                    await MutateFile(file, fault, true);
                    await client.VerifyDevelopmentCertificateActivationAsync(name, token);
                }
                Console.WriteLine(
                    "PASS: active PFX and protected config reject permissive modes, symlinks and hardlinks before replacement."
                );
                // A synthetic named parent checks stale handling, not actual hc behavior.
                Require(
                    (
                        await client.CertificateProbeExecAsync(
                            name,
                            [
                                "python3",
                                "-I",
                                "-c",
                                "import os,subprocess; p=subprocess.Popen(['hc','180'],executable='/bin/sleep',stdin=subprocess.DEVNULL,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL); open(os.environ['WORKSPACE_DIR']+'/fixture.pid','w').write(str(p.pid))",
                            ],
                            token
                        )
                    ).Succeeded,
                    "start stale synthetic hc parent"
                );
                var rejected = false;
                try
                {
                    await client.VerifyDevelopmentCertificateActivationAsync(name, token);
                }
                catch (Abox.AboxException error)
                {
                    rejected = error.Message.Contains("stale", StringComparison.Ordinal);
                }
                Require(rejected, "stale parent prevents full activation success");
                Require(
                    await client.ExecAsync(new SbxExecRequest(name, ["true"]), token) == 0,
                    "stale parent retains ordinary save/exit transport"
                );
                Require(
                    await client.ExecAsync(new SbxExecRequest(name, ["hc", "--version"]), token)
                        == 42,
                    "stale parent refuses a new agent launch rather than transport"
                );
                await StopFixture();
                await client.VerifyDevelopmentCertificateActivationAsync(name, token);
                Require(
                    await client.ExecAsync(
                        new SbxExecRequest(
                            name,
                            [
                                "python3",
                                "-I",
                                "-c",
                                "import os,subprocess; p=subprocess.Popen(['hc','180'],executable='/bin/sleep',stdin=subprocess.DEVNULL,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL); open(os.environ['WORKSPACE_DIR']+'/fixture.pid','w').write(str(p.pid))",
                            ]
                        ),
                        token
                    ) == 0,
                    "fresh synthetic hc parent inherits canonical settings"
                );
                await client.VerifyDevelopmentCertificateActivationAsync(name, token);
                Console.WriteLine(
                    "PASS: stale synthetic hc parent detected without restart; explicitly exited fixture and fresh initialized parent accepted."
                );
            }
        }
        await StopFixture();
        var abox = Path.Combine(
            checkout.FullName,
            "cli/src/Abox/bin",
            BuildConfiguration(),
            "net10.0/abox.dll"
        );
        Require(
            File.Exists(abox),
            "build matching production CLI configuration before verification"
        );
        foreach (var route in new[] { "exec", "pipe", "shell" })
        {
            File.Delete(Path.Combine(workspace, "ready"));
            File.Delete(Path.Combine(workspace, "stop"));
            string[] arguments =
                route == "shell"
                    ? [abox, "run", "shell", "--location", workspace, "--", executable, "serve"]
                    : [abox, route, name, "--", executable, "serve"];
            var launch = runner.RunAsync(
                new ProcessRequest(
                    "dotnet",
                    arguments,
                    StandardInput: "",
                    OutputPolicy: ProcessOutputPolicy.CapturedSensitive
                ),
                token
            );
            for (
                var attempt = 0;
                attempt < 150
                    && !File.Exists(Path.Combine(workspace, "ready"))
                    && !launch.IsCompleted;
                attempt++
            )
                await Task.Delay(100, token);
            Require(
                File.Exists(Path.Combine(workspace, "ready")),
                "production CLI " + route + " Kestrel ready"
            );
            Require(
                (
                    await client.CertificateProbeExecAsync(name, [executable, "client"], token)
                ).Succeeded,
                "production " + route + " exact leaf TLS"
            );
            Require((await launch).Succeeded, "production " + route + " exit");
            Console.WriteLine(
                "PASS actual production abox "
                    + route
                    + ": post-helper-exit Kestrel exact public DER with strict sandbox system-store localhost TLS (synthetic identity)."
            );
        }
        using (var pipe = new System.Diagnostics.Process())
        {
            pipe.StartInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (
                var argument in new[]
                {
                    abox,
                    "pipe",
                    name,
                    "--",
                    "python3",
                    "-I",
                    "-c",
                    "import sys; sys.stdout.buffer.write(sys.stdin.buffer.read()); sys.stderr.write('pipe-stderr'); sys.exit(37)",
                }
            )
                pipe.StartInfo.ArgumentList.Add(argument);
            Require(pipe.Start(), "production binary pipe start");
            var input = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
            using var output = new MemoryStream();
            var read = pipe.StandardOutput.BaseStream.CopyToAsync(output, token);
            var error = pipe.StandardError.ReadToEndAsync(token);
            await pipe.StandardInput.BaseStream.WriteAsync(input, token);
            pipe.StandardInput.Close();
            await pipe.WaitForExitAsync(token);
            await read;
            Require(
                pipe.ExitCode == 37
                    && output.ToArray().AsSpan().SequenceEqual(input)
                    && await error == "pipe-stderr",
                "exact binary pipe stdout/stderr/exit invariants"
            );
            Console.WriteLine(
                "PASS production abox pipe: all 256 byte values unchanged, separate stderr and exit 37; bootstrap consumed no stdin."
            );
        }
        if (await client.HasProgramAsync(name, "hc", token))
        {
            Require(
                (
                    await client.CertificateProbeExecAsync(name, ["hc", "server", "start"], token)
                ).Succeeded,
                "start actual disposable stale core without bootstrap"
            );
            var stale = false;
            try
            {
                await client.VerifyDevelopmentCertificateActivationAsync(name, token);
            }
            catch (Abox.AboxException error)
            {
                stale = error.Message.Contains("stale", StringComparison.Ordinal);
            }
            Require(stale, "actual stale core must not yield full activation success");
            Require(
                await client.ExecAsync(new SbxExecRequest(name, ["hc", "server", "stop"]), token)
                    == 0,
                "explicitly stop only disposable core"
            );
            Require(
                await client.ExecAsync(new SbxExecRequest(name, ["hc", "server", "start"]), token)
                    == 0,
                "explicitly start disposable core with bootstrap"
            );
            await client.VerifyDevelopmentCertificateActivationAsync(name, token);
            Console.WriteLine(
                "PASS: actual installed hc stale core detected; explicit disposable stop/start yields fresh initialized core (no --fresh support in installed help)."
            );
        }
        if (await client.HasProgramAsync(name, "opencode", token))
        {
            Require(
                (
                    await client.CertificateProbeExecAsync(
                        name,
                        ["opencode", "api", "get", "/api/info"],
                        token
                    )
                ).Succeeded,
                "start actual disposable stale OpenCode service without bootstrap"
            );
            var stale = false;
            try
            {
                await client.VerifyDevelopmentCertificateActivationAsync(name, token);
            }
            catch (Abox.AboxException error)
            {
                stale = error.Message.Contains("stale", StringComparison.Ordinal);
            }
            Require(stale, "actual stale OpenCode service must not yield full activation success");
            Require(
                await client.ExecAsync(
                    new SbxExecRequest(name, ["opencode", "service", "stop"]),
                    token
                ) == 0,
                "explicitly stop only disposable OpenCode service"
            );
            await client.VerifyDevelopmentCertificateActivationAsync(name, token);
            Console.WriteLine(
                "PASS: actual installed OpenCode stale service detected; explicitly stopped disposable service, ready for fresh startup through abox."
            );
        }
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "tools_probe.py"),
            await File.ReadAllTextAsync(
                Path.Combine(checkout.FullName, "docs/verification/https/tools_probe.py"),
                token
            ),
            token
        );
        foreach (var tool in new[] { "hc", "pi", "claude", "opencode" })
        {
            if (!await client.HasProgramAsync(name, tool, token))
                continue;
            File.Delete(Path.Combine(workspace, "ready"));
            File.Delete(Path.Combine(workspace, "stop"));
            var launch = client.ExecAsync(
                new SbxExecRequest(
                    name,
                    [
                        "python3",
                        "-I",
                        found.StandardOutput.Trim() + "/tools_probe.py",
                        tool,
                        executable,
                    ]
                ),
                token
            );
            for (
                var attempt = 0;
                attempt < 400
                    && !File.Exists(Path.Combine(workspace, "ready"))
                    && !launch.IsCompleted;
                attempt++
            )
                await Task.Delay(100, token);
            if (!File.Exists(Path.Combine(workspace, "ready")))
            {
                Console.WriteLine(
                    $"UNVERIFIED actual {tool} shell path: no Kestrel readiness; launch exit {(launch.IsCompleted ? await launch : -1)}. No model request was sent."
                );
                continue;
            }
            Require(
                (
                    await client.CertificateProbeExecAsync(name, [executable, "client"], token)
                ).Succeeded,
                "actual " + tool + " exact leaf TLS"
            );
            Require(await launch == 0, "actual " + tool + " local tool process");
            Console.WriteLine(
                $"PASS actual installed {tool} local shell tool: post-helper-exit Kestrel exact public DER with strict sandbox system-store localhost TLS (synthetic identity); no prompt/model call."
            );
        }
        Require(
            (
                await client.CertificateProbeExecAsync(
                    name,
                    [
                        "python3",
                        "-I",
                        "-c",
                        "import os,re,stat; p='/tmp/abox-https-'+str(os.geteuid()); names=set(os.listdir(p)); required={'config.json','active.pfx','.lock','bootstrap.py','public.pem'}; assert required<=names; assert all(n in required or re.fullmatch(r'node-[0-9a-f]{64}\\.(pem|source)',n) for n in names); assert stat.S_IMODE(os.stat(p).st_mode)==0o700; assert all(stat.S_IMODE(os.stat(p+'/'+n).st_mode)==0o600 for n in names)",
                    ],
                    token
                )
            ).Succeeded,
            "bounded protected runtime inventory"
        );
        return 0;

        async Task StopFixture() =>
            Require(
                (
                    await client.CertificateProbeExecAsync(
                        name,
                        [
                            "python3",
                            "-I",
                            "-c",
                            "import os,signal; p=int(open(os.environ['WORKSPACE_DIR']+'/fixture.pid').read()); assert open('/proc/'+str(p)+'/cmdline','rb').read()==b'hc\\x00180\\x00'; os.kill(p,signal.SIGTERM)",
                        ],
                        token
                    )
                ).Succeeded,
                "stop only synthetic owned parent fixture"
            );

        async Task MutateFile(string file, string fault, bool restore) =>
            Require(
                (
                    await client.CertificateProbeExecAsync(
                        name,
                        [
                            "python3",
                            "-I",
                            "-c",
                            "import os,sys; p='/tmp/abox-https-'+str(os.geteuid())+'/'+sys.argv[1]; b='/tmp/abox-activation-fixture-'+str(os.geteuid()); fault,restore=sys.argv[2],sys.argv[3]=='True';\nif fault=='mode': os.chmod(p,0o600 if restore else 0o644)\nelif fault=='link':\n if restore: os.unlink(p); os.rename(b,p)\n else: assert not os.path.lexists(b); os.rename(p,b); os.symlink(b,p)\nelif fault=='hardlink':\n if restore: os.unlink(b)\n else: assert not os.path.lexists(b); os.link(p,b)",
                            file,
                            fault,
                            restore.ToString(),
                        ],
                        token
                    )
                ).Succeeded,
                "task-owned activation file fixture"
            );
    }
    finally
    {
        if (created)
            Require(
                (await client.RemoveSandboxAsync(name, CancellationToken.None)).Succeeded,
                "remove task-owned sandbox"
            );
        Require(
            await client.FindSandboxAsync(name, CancellationToken.None) is null,
            "absent inventory after cleanup"
        );
        Directory.Delete(workspace, true);
        Console.WriteLine("Task-owned sandbox/workspace cleanup confirmed.");
    }
}

static async Task<int> PrepareProduction(string template)
{
    // This mode creates only a clean disposable and a trivial app. Certificate
    // authorization/export is a subsequent explicit production CLI invocation.
    var checkout = new DirectoryInfo(Environment.CurrentDirectory);
    while (
        checkout is not null && !File.Exists(Path.Combine(checkout.FullName, "CODING_STANDARDS.md"))
    )
        checkout = checkout.Parent;
    if (checkout is null)
        throw new InvalidOperationException();
    var workspace = Path.Combine(
        checkout.FullName,
        ".abox",
        "abox-issue46-activation-production-" + Guid.NewGuid().ToString("N")[..8]
    );
    var client = new SbxClient(new ProcessRunner(), AboxPaths.For(workspace, workspace));
    var name = SandboxName.For(workspace);
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    var token = timeout.Token;
    if (Directory.Exists(workspace) || await client.FindSandboxAsync(name, token) is not null)
        throw new InvalidOperationException();
    Directory.CreateDirectory(Path.Combine(workspace, "Properties"));
    try
    {
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "Verifier.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>
            """,
            token
        );
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "Program.cs"),
            """
            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();
            app.MapGet("/", () => "abox-production-verifier");
            app.Run();
            """,
            token
        );
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "Properties", "launchSettings.json"),
            """
            {"profiles":{"verifier":{"commandName":"Project","applicationUrl":"https://localhost:8443"}}}
            """,
            token
        );
        Require(
            (await client.CertificateProbeCreateAsync(workspace, name, template, token)).Succeeded,
            "create clean production-verification disposable"
        );
        var clean = await client.CertificateProbeExecAsync(
            name,
            [
                "sh",
                "-c",
                "uid=$(id -u) && test ! -e /tmp/abox-https-$uid && test ! -L /tmp/abox-https-$uid && test ! -e /usr/local/share/ca-certificates/abox-development-https-$uid",
            ],
            token
        );
        Require(clean.Succeeded, "fresh runtime has no prior abox HTTPS activation/trust");
        Console.WriteLine(
            "Prepared only; NO certificate export/setup has run. Review the clean-template trust inventory before the separately authorized production command."
        );
        Console.WriteLine("Workspace: " + workspace);
        Console.WriteLine("Sandbox: " + name);
        Console.WriteLine(
            "Follow docs/verification/https/production-verification.md; cleanup this exact workspace even if a later check fails."
        );
        return 0;
    }
    catch
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        if (await client.FindSandboxAsync(name, cleanup.Token) is not null)
            Require(
                (await client.RemoveSandboxAsync(name, cleanup.Token)).Succeeded,
                "cleanup failed production-verification preparation"
            );
        Directory.Delete(workspace, true);
        throw;
    }
}

static void Require(bool condition, string step)
{
    if (!condition)
        throw new InvalidOperationException("Verification step failed: " + step);
}

static string BuildConfiguration()
{
#if DEBUG
    return "Debug";
#else
    return "Release";
#endif
}

sealed class SyntheticExportRunner : IProcessRunner, IDisposable
{
    private readonly ProcessRunner actual = new();
    private readonly RSA key = RSA.Create(2048);
    private readonly X509Certificate2 certificate;
    private readonly string metadata;
    public string? PfxDigest { get; private set; }
    public byte[] PublicDer => certificate.RawData;

    public SyntheticExportRunner()
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
        certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1)
        );
        metadata = System.Text.Json.JsonSerializer.Serialize(
            new[]
            {
                new
                {
                    certificate.Thumbprint,
                    certificate.Subject,
                    Version = 6,
                    ValidityNotBefore = certificate.NotBefore.ToUniversalTime(),
                    ValidityNotAfter = certificate.NotAfter.ToUniversalTime(),
                    IsHttpsDevelopmentCertificate = true,
                    IsExportable = true,
                    TrustLevel = "Full",
                    X509SubjectAlternativeNameExtension = new[] { "localhost" },
                },
            }
        );
    }

    public async Task<ProcessResult> RunAsync(
        ProcessRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.FileName != "dotnet" || !request.Arguments.Contains("synthetic-dev-certs.dll"))
            return await actual.RunAsync(request, cancellationToken);
        if (request.Arguments.Contains("--check-trust-machine-readable"))
            return new ProcessResult(0, metadata, "synthetic private metadata diagnostic");
        var arguments = request.Arguments.ToArray();
        var bytes = certificate.Export(
            X509ContentType.Pfx,
            arguments[Array.IndexOf(arguments, "--password") + 1]
        );
        try
        {
            PfxDigest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            await File.WriteAllBytesAsync(
                arguments[Array.IndexOf(arguments, "--export-path") + 1],
                bytes,
                cancellationToken
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
        return new ProcessResult(
            0,
            "synthetic private export diagnostic",
            "synthetic private export error"
        );
    }

    public void Dispose()
    {
        certificate.Dispose();
        key.Dispose();
    }
}
