using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sbx;

var options = args.Skip(3).ToArray();
var localhost = options.Contains("--sandbox-localhost");
var portOptions = options
    .Where(option => option.StartsWith("--port=", StringComparison.Ordinal))
    .ToArray();
var sandboxPort = 8203;
if (
    args.Length is < 3 or > 5
    || options.Distinct().Count() != options.Length
    || options.Any(option =>
        option != "--sandbox-localhost" && !option.StartsWith("--port=", StringComparison.Ordinal)
    )
    || portOptions.Length > 1
    || (portOptions.Length == 1 && !int.TryParse(portOptions[0][7..], out sandboxPort))
    || sandboxPort is < 1 or > 55532
)
{
    Console.Error.WriteLine(
        "Pass a NEW task-owned workspace (leaf abox-issue43-...), an existing local shell template, and Debug abox.dll; optionally --sandbox-localhost and --port=8203 (1..55532)."
    );
    return 2;
}
var workspace = Path.GetFullPath(args[0]);
if (
    !Path.GetFileName(workspace).StartsWith("abox-issue43-", StringComparison.Ordinal)
    || Directory.Exists(workspace)
)
    throw new InvalidOperationException("Workspace must be new and task-owned.");
var abox = Path.GetFullPath(args[2]);
var hostPort = sandboxPort + 10000;
var hostUrl = $"http://localhost:{hostPort}";
if (
    IPGlobalProperties
        .GetIPGlobalProperties()
        .GetActiveTcpListeners()
        .Any(endpoint => endpoint.Port >= hostPort && endpoint.Port <= hostPort + 3)
)
{
    Console.Error.WriteLine(
        $"Probe requires free host ports {hostPort}..{hostPort + 3}. Existing listeners/mappings will not be changed; coordinate other verification or choose a different probe --port."
    );
    return 2;
}
var name = SandboxName.For(workspace);
var runner = new ProcessRunner();
var sbx = new SbxClient(runner, AboxPaths.For(workspace, workspace));
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
var token = timeout.Token;
Console.WriteLine($"Client: {await sbx.GetVersionAsync(token)}; disposable sandbox: {name}");
if (await sbx.FindSandboxAsync(name, token) is not null)
    throw new InvalidOperationException("Sandbox already exists; refusing to modify it.");
Directory.CreateDirectory(workspace);
var attemptedCreate = false;
var expectedHostPorts = new HashSet<int> { hostPort, hostPort + 1 };
var savedTargets = new Dictionary<int, int>();
try
{
    var publish = await runner.RunAsync(
        new ProcessRequest(
            "dotnet",
            [
                "publish",
                Path.GetFullPath("docs/verification/application-ports/SyntheticHttp"),
                "-c",
                "Debug",
                "-r",
                "linux-x64",
                "--self-contained",
                "true",
                "-o",
                Path.Combine(workspace, "payload"),
            ]
        ),
        token
    );
    Require(publish, "Synthetic .NET Debug publish");
    var properties = Path.Combine(workspace, "app", "Properties");
    Directory.CreateDirectory(properties);
    await File.WriteAllTextAsync(
        Path.Combine(properties, "launchSettings.json"),
        LaunchSettings(sandboxPort),
        token
    );
    attemptedCreate = true;
    Require(
        await sbx.CreateProbeAsync(workspace, name, args[1], token),
        "Create disposable sandbox"
    );
    var receiverSource = await File.ReadAllTextAsync(
        "cli/src/Abox/Sbx/application_ports.py",
        token
    );
    var receiverChecks = await File.ReadAllTextAsync(
        "docs/verification/application-ports/RuntimeProbe/ReceiverChecks.py",
        token
    );
    Require(
        await sbx.ProbePythonAsync(
            name,
            "RECEIVER = " + JsonSerializer.Serialize(receiverSource) + "\n" + receiverChecks,
            token
        ),
        "Receiver process/TCP checks"
    );
    await StartApplication();
    Require(
        await sbx.PublishDirectProbeMappingAsync(name, sandboxPort + 1, hostPort + 1, token),
        "Pre-existing unrelated native mapping"
    );
    Require(
        await sbx.PublishDirectProbeMappingAsync(name, sandboxPort, hostPort, token),
        "Pre-existing unproven direct mapping (must conflict, never upgrade automatically)"
    );
    var beforeConflict = await sbx.ProbePortsAsync(name, token);
    Require(beforeConflict, "Read original native bindings");
    var conflict = await RunPorts(1);
    var afterConflict = await sbx.ProbePortsAsync(name, token);
    Require(afterConflict, "Read preserved native bindings");
    if (
        beforeConflict.StandardOutput != afterConflict.StandardOutput
        || !conflict.StandardError.Contains("conflicting runtime mapping", StringComparison.Ordinal)
    )
        throw new InvalidOperationException(
            "Unproven direct mapping was changed or not diagnosed."
        );
    Require(
        await sbx.RemoveExactProbeMappingAsync(name, sandboxPort, hostPort, token),
        "Probe operator explicitly removes only its task-owned direct binding"
    );
    await RunPorts(0);
    var internalHttp = await sbx.ProbeExecAsync(
        name,
        $"\"$WORKSPACE_DIR/payload/SyntheticHttp\" --get {sandboxPort}",
        token
    );
    Require(internalHttp, "Sandbox-local .NET HTTP request");
    if (internalHttp.StandardOutput.Trim() != "abox-issue43-dotnet-http-marker")
        throw new InvalidOperationException(
            "The synthetic application was not serving inside the sandbox."
        );
    Console.WriteLine(
        $"Inside sandbox: localhost:{sandboxPort} returned synthetic .NET HTTP marker."
    );
    using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(10),
    };
    var body = await http.GetStringAsync(hostUrl, token);
    if (body != "abox-issue43-dotnet-http-marker")
        throw new InvalidOperationException("Unexpected HTTP response.");
    Console.WriteLine(
        $"After abox exited: localhost:{hostPort} returned synthetic .NET HTTP marker."
    );
    await RunPorts(0);
    await VerifyInventory();
    var listeners = IPGlobalProperties
        .GetIPGlobalProperties()
        .GetActiveTcpListeners()
        .Where(endpoint => endpoint.Port == hostPort || endpoint.Port == hostPort + 1)
        .ToArray();
    if (
        listeners.Length != 2
        || listeners.Any(endpoint => !endpoint.Address.Equals(IPAddress.Loopback))
    )
        throw new InvalidOperationException("Host listeners were not exclusively IPv4 loopback.");
    Console.WriteLine(
        $"Host listener inspection: 127.0.0.1:{hostPort} and 127.0.0.1:{hostPort + 1} only."
    );
    var stop = await sbx.StopSandboxAsync(name, token);
    if (!stop.Succeeded)
        throw new InvalidOperationException(stop.Output);
    await File.WriteAllTextAsync(
        Path.Combine(properties, "launchSettings.json"),
        LaunchSettings(sandboxPort, sandboxPort + 2),
        token
    );
    expectedHostPorts.Add(hostPort + 2);
    await RunPorts(0);
    await VerifyInventory();
    await StartApplication();
    if (await http.GetStringAsync(hostUrl, token) != "abox-issue43-dotnet-http-marker")
        throw new InvalidOperationException(
            "Restored relay could not reach the restarted application."
        );
    Console.WriteLine(
        "After sandbox stop/start and abox exit: restored relay reached restarted .NET app with unchanged binding."
    );
    using var occupied = new TcpListener(IPAddress.Loopback, hostPort + 3);
    occupied.Start();
    await File.WriteAllTextAsync(
        Path.Combine(properties, "launchSettings.json"),
        LaunchSettings(sandboxPort + 3),
        token
    );
    await RunPorts(1);
    await VerifyInventory();
    Console.WriteLine(
        "Verified repeat reuse, stopped-sandbox startup, unrelated mapping preservation and occupied-port failure. Inspect host listeners while reproducing if needed."
    );
    return 0;
}
finally
{
    if (attemptedCreate)
    {
        var removed = await sbx.RemoveSandboxAsync(name, CancellationToken.None);
        Console.WriteLine(
            $"Task-owned sandbox cleanup: exit {removed.ExitCode} {removed.Output.Trim()}"
        );
        if (!removed.Succeeded)
            Console.Error.WriteLine(
                $"CLEANUP REQUIRED for task-owned sandbox {name}; workspace retained at {workspace}."
            );
    }
    Console.WriteLine($"Synthetic workspace retained for inspection: {workspace}");
}

async Task StartApplication()
{
    Require(
        await sbx.ProbeExecAsync(
            name,
            "chmod +x \"$WORKSPACE_DIR/payload/SyntheticHttp\"; nohup \"$WORKSPACE_DIR/payload/SyntheticHttp\" "
                + $"'http://{(localhost ? "localhost" : "*")}:{sandboxPort}/' "
                + "</dev/null >/tmp/abox-issue43-http.log 2>&1 &",
            token
        ),
        "Start synthetic .NET HTTP app"
    );
    Require(
        await sbx.ProbeExecAsync(
            name,
            $"\"$WORKSPACE_DIR/payload/SyntheticHttp\" --get {sandboxPort}",
            token
        ),
        "Synthetic app readiness"
    );
}

async Task<ProcessResult> RunPorts(int expected)
{
    var result = await runner.RunAsync(
        new ProcessRequest(
            "dotnet",
            [abox, "ports", "--location", workspace],
            Environment: new Dictionary<string, string>
            {
                ["XDG_CONFIG_HOME"] = Path.Combine(workspace, "isolated-config"),
                ["XDG_STATE_HOME"] = Path.Combine(workspace, "isolated-state"),
            }
        ),
        token
    );
    Console.Write(result.StandardOutput);
    Console.Write(result.StandardError);
    if (result.ExitCode != expected)
        throw new InvalidOperationException(
            $"abox ports: expected exit {expected}, got {result.ExitCode}."
        );
    return result;
}

async Task VerifyInventory()
{
    var result = await sbx.ProbePortsAsync(name, token);
    Require(result, "Read runtime inventory");
    using var doc = JsonDocument.Parse(result.StandardOutput);
    var bindings = doc.RootElement.EnumerateArray().ToArray();
    if (
        bindings.Any(binding =>
            binding.GetProperty("host_ip").GetString() != "127.0.0.1"
            || binding.GetProperty("protocol").GetString() != "tcp4"
        )
        || bindings.Length != expectedHostPorts.Count
        || !bindings
            .Select(binding => binding.GetProperty("host_port").GetInt32())
            .ToHashSet()
            .SetEquals(expectedHostPorts)
        || bindings
            .Single(binding => binding.GetProperty("host_port").GetInt32() == hostPort + 1)
            .GetProperty("sandbox_port")
            .GetInt32()
            != sandboxPort + 1
        || bindings
            .Single(binding => binding.GetProperty("host_port").GetInt32() == hostPort)
            .GetProperty("sandbox_port")
            .GetInt32() < 55536
    )
        throw new InvalidOperationException("Unexpected bindings or lost unrelated mapping.");
    foreach (var binding in bindings)
    {
        var bindingHostPort = binding.GetProperty("host_port").GetInt32();
        var target = binding.GetProperty("sandbox_port").GetInt32();
        if (savedTargets.TryGetValue(bindingHostPort, out var previous) && previous != target)
            throw new InvalidOperationException(
                "A repeated/restored mapping changed its sandbox target."
            );
        savedTargets[bindingHostPort] = target;
    }
    Console.WriteLine(result.StandardOutput.Trim());
}

static string LaunchSettings(params int[] ports) =>
    JsonSerializer.Serialize(
        new
        {
            profiles = new
            {
                http = new
                {
                    applicationUrl = string.Join(
                        ';',
                        ports.Select(port => $"http://localhost:{port}")
                    ),
                },
            },
        }
    );

static void Require(ProcessResult result, string label)
{
    Console.Write(result.StandardOutput);
    Console.Write(result.StandardError);
    if (!result.Succeeded)
        throw new InvalidOperationException(
            $"{label}: {result.StandardError}{result.StandardOutput}"
        );
    Console.WriteLine($"{label}: exit 0");
}
