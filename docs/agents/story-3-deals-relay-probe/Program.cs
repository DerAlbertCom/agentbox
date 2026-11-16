using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;

const string sandbox = "rwe-ai-sbx-41bc30";
const string knownLeaf = "CA669D34D0FA19407DFD5788F3A639AA759A6953";
if (args.Length != 1 || args[0] is not ("--apply" or "--check"))
    return 2;
using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var token = deadline.Token;
var runner = new ProcessRunner();
var observer = new InventoryObserver(runner);
var sbx = new SbxClient(
    observer,
    AboxPaths.For(AppContext.BaseDirectory, AppContext.BaseDirectory)
);
Console.WriteLine($"UTC: {DateTimeOffset.UtcNow:O}; mode: {args[0]}");
Console.WriteLine($"Client: {await sbx.GetVersionAsync(token)}");
var selected = await sbx.FindSandboxAsync(sandbox, token);
if (selected is not { IsRunning: true } || selected.Name != sandbox)
    throw new InvalidOperationException("Exact selected sandbox must already be running.");
Console.WriteLine($"Exact identity: {selected.Name}; running: {selected.IsRunning}");
await CheckApplication(args[0] == "--check");
if (args[0] == "--apply")
{
    // The production adapter alone chooses/starts relays and upgrades this single mapping.
    await sbx.EnsureApplicationPortsAsync(sandbox, [new(8203, 18203)], token);
    observer.VerifyUpgrade();
    await sbx.EnsureApplicationPortsAsync(sandbox, [new(8203, 18203)], token);
    observer.VerifyRepeat();
    await CheckApplication(true);
}
VerifyHostBind();
if (args[0] == "--check")
{
    var curl = await runner.RunAsync(
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
                "5",
                "--head",
                "--silent",
                "--output",
                "NUL",
                "--write-out",
                "%{http_code}",
                "https://localhost:18203/",
            ],
            StandardInput: ""
        ),
        token
    );
    var status = curl.StandardOutput.Trim();
    if (status.Length != 3 || status.Any(character => !char.IsAsciiDigit(character)))
        throw new InvalidOperationException("Unexpected curl status shape; output suppressed.");
    Console.WriteLine($"Host default-validation HEAD: curl={curl.ExitCode}; HTTP={status}");
    if (curl.ExitCode != 60 || status != "000")
        throw new InvalidOperationException("Expected certificate rejection was not observed.");
    await StrictTls();
}
Console.WriteLine("Verification completed; app, relay and mapping intentionally retained.");
return 0;

async Task CheckApplication(bool relay)
{
    const string source = """
        import json, os, socket, stat, struct, subprocess, sys
        try:
            processes = []
            for pid, ticks in [(43130,1378797),(43843,1390583)]:
                fields = open('/proc/%s/stat' % pid).read().rsplit(')',1)[1].split()
                assert fields[0] != 'Z' and int(fields[2]) == 43130 and int(fields[19]) == ticks
                processes.append({'pid':pid,'start_ticks':ticks,'process_group':43130})
            assert os.path.basename(os.readlink('/proc/43843/exe')) == 'Cape.Deals.WebApi.External'
            owned = set()
            for item in os.scandir('/proc/43843/fd'):
                try: owned.add(os.readlink(item.path))
                except FileNotFoundError: pass
            listeners = []
            for path, family in [('/proc/net/tcp',socket.AF_INET),('/proc/net/tcp6',socket.AF_INET6)]:
                for line in open(path).read().splitlines()[1:]:
                    parts = line.split()
                    address, port = parts[1].split(':')
                    if parts[3] != '0A' or int(port,16) != 8203: continue
                    raw = bytes.fromhex(address)
                    raw = raw[::-1] if family == socket.AF_INET else b''.join(raw[i:i+4][::-1] for i in range(0,16,4))
                    ip = socket.inet_ntop(family,raw)
                    assert 'socket:[%s]' % parts[9] in owned
                    listeners.append(ip)
            assert sorted(listeners) == sorted(['127.0.0.1','::1'])
            report = {'processes':processes,'application_listeners':listeners,'application_port':8203}
            if sys.argv[1] == 'relay':
                root = '/home/agent/.abox-ports-v1'
                for path, mode in [(root,0o700),(root+'/lock',0o600),(root+'/8203.port',0o600),(root+'/8203.sock',0o600)]:
                    info = os.lstat(path)
                    assert info.st_uid == os.getuid() and stat.S_IMODE(info.st_mode) == mode
                    assert not stat.S_ISLNK(info.st_mode)
                with socket.socket(socket.AF_UNIX) as control:
                    control.settimeout(3)
                    control.connect(root+'/8203.sock')
                    reply = json.loads(control.makefile('rb').readline(512))
                assert set(reply) == {'pid','relay_port','application_port'} and reply['application_port'] == 8203
                assert 55536 <= reply['relay_port'] <= 65535
                assert int(open(root+'/8203.port').read()) == reply['relay_port']
                fields = open('/proc/%s/stat' % reply['pid']).read().rsplit(')',1)[1].split()
                reply['start_ticks'] = int(fields[19])
                report['relay'] = reply
                report['private_state_modes_verified'] = True
                result = subprocess.run(['curl','-q','--noproxy','*','--proto','=https','--connect-timeout','3','--max-time','5','--head','--silent','--output','/dev/null','--write-out','%{http_code}','https://localhost:8203/'],stdin=subprocess.DEVNULL,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,timeout=8)
                assert result.returncode == 60 and result.stdout == b'000'
                report['sandbox_head'] = {'curl':result.returncode,'http':'000'}
            print(json.dumps(report,sort_keys=True))
        except Exception as error:
            print(json.dumps({'verification_failed':type(error).__name__}))
            sys.exit(1)
        """;
    var code = await sbx.ExecAsync(
        new SbxExecRequest(sandbox, ["python3", "-I", "-c", source, relay ? "relay" : "app"]),
        token
    );
    if (code != 0)
        throw new InvalidOperationException(
            "Application/relay identity or loopback verification failed."
        );
}

void VerifyHostBind()
{
    var endpoints = IPGlobalProperties
        .GetIPGlobalProperties()
        .GetActiveTcpListeners()
        .Where(endpoint => endpoint.Port == 18203)
        .ToArray();
    if (endpoints.Length != 1 || !endpoints[0].Address.Equals(IPAddress.Loopback))
        throw new InvalidOperationException("Expected only IPv4 loopback on host port 18203.");
    Console.WriteLine("Host bind: 127.0.0.1:18203 only (no wildcard/IPv6).");
}

async Task StrictTls()
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
    timeout.CancelAfter(TimeSpan.FromSeconds(8));
    using var tcp = new TcpClient();
    await tcp.ConnectAsync("localhost", 18203, timeout.Token);
    string? thumbprint = null;
    string? subject = null;
    string? chainFlags = null;
    var validationErrors = SslPolicyErrors.None;
    var callbackSeen = false;
    using var tls = new SslStream(
        tcp.GetStream(),
        false,
        (_, certificate, chain, errors) =>
        {
            callbackSeen = true;
            thumbprint = certificate?.GetCertHashString();
            subject = certificate?.Subject;
            validationErrors = errors;
            chainFlags = string.Join(
                ",",
                chain?.ChainStatus.Select(status => status.Status.ToString()) ?? []
            );
            // Observe public identity, but never accept an invalid certificate.
            return errors == SslPolicyErrors.None;
        }
    );
    var authenticated = false;
    string? failureType = null;
    try
    {
        await tls.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions { TargetHost = "localhost" },
            timeout.Token
        );
        authenticated = true;
    }
    catch (Exception error)
        when (error is System.Security.Authentication.AuthenticationException or IOException)
    {
        failureType = error.GetType().Name;
    }
    Console.WriteLine(
        JsonSerializer.Serialize(
            new
            {
                tcp_connected = tcp.Connected,
                callback_seen = callbackSeen,
                leaf_sha1 = thumbprint,
                subject,
                validation_errors = validationErrors.ToString(),
                chain_flags = chainFlags,
                tls_authenticated = authenticated,
                failure_type = failureType,
            }
        )
    );
    if (
        !callbackSeen
        || thumbprint != knownLeaf
        || authenticated
        || validationErrors == SslPolicyErrors.None
    )
        throw new InvalidOperationException(
            "Expected served sandbox leaf and strict certificate rejection."
        );
}

sealed class InventoryObserver(IProcessRunner inner) : IProcessRunner
{
    private readonly List<Binding[]> snapshots = [];
    private int publishes;
    private int unpublishes;
    private Binding[]? afterUpgrade;

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken token)
    {
        var result = await inner.RunAsync(request, token);
        if (request.FileName == "sbx" && request.Arguments[0] == "ports")
        {
            if (request.Arguments.Contains("--publish"))
                publishes++;
            if (request.Arguments.Contains("--unpublish"))
                unpublishes++;
            if (request.Arguments.Count == 3 && result.Succeeded)
            {
                var bindings =
                    JsonSerializer.Deserialize<Binding[]>(result.StandardOutput)
                    ?? throw new InvalidOperationException("Missing runtime inventory.");
                snapshots.Add(bindings);
                Console.WriteLine(
                    "Observed production inventory: " + JsonSerializer.Serialize(bindings)
                );
                if (snapshots.Count == 1 && bindings.Count(IsRequestedNative) != 1)
                    throw new InvalidOperationException(
                        "Expected exactly the known direct native mapping before upgrade."
                    );
            }
        }
        return result;
    }

    public void VerifyUpgrade()
    {
        var before = snapshots.First();
        var after = snapshots.Last();
        var unrelated = before.Where(binding => !IsRequestedNative(binding)).ToArray();
        var requested = after.Where(binding => binding.HostPort == 18203).ToArray();
        if (
            publishes != 1
            || unpublishes != 1
            || requested.Length != 1
            || requested[0].HostIp != "127.0.0.1"
            || requested[0].Protocol != "tcp4"
            || requested[0].SandboxPort is < 55536 or > 65535
            || after.Length != unrelated.Length + 1
            || unrelated.Any(binding => !after.Contains(binding))
        )
            throw new InvalidOperationException(
                "Exact upgrade/unrelated-inventory verification failed."
            );
        afterUpgrade = after;
        Console.WriteLine(
            $"Upgrade verified; unrelated mappings preserved: {unrelated.Length} (none added for testing)."
        );
    }

    public void VerifyRepeat()
    {
        var after = snapshots.Last();
        if (
            afterUpgrade is null
            || publishes != 1
            || unpublishes != 1
            || after.Length != afterUpgrade.Length
            || afterUpgrade.Any(binding => !after.Contains(binding))
        )
            throw new InvalidOperationException(
                "Repeat modified mappings or did not reuse the relay."
            );
        Console.WriteLine("Repeat verified: identical inventory; no further publish/unpublish.");
    }

    private static bool IsRequestedNative(Binding binding) =>
        binding is { HostIp: "127.0.0.1", HostPort: 18203, SandboxPort: 8203, Protocol: "tcp4" };

    private sealed record Binding(
        [property: JsonPropertyName("host_ip")] string HostIp,
        [property: JsonPropertyName("host_port")] int HostPort,
        [property: JsonPropertyName("sandbox_port")] int SandboxPort,
        [property: JsonPropertyName("protocol")] string Protocol
    );
}
