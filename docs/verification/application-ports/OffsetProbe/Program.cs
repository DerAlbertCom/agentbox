using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sbx;

var runner = new ProcessRunner();
if (args is ["--templates"])
{
    var inventory = new SbxClient(
        runner,
        AboxPaths.For(Environment.CurrentDirectory, Environment.CurrentDirectory)
    );
    foreach (var template in await inventory.ListTemplatesAsync(CancellationToken.None))
        Console.WriteLine(template);
    return 0;
}
if (args.Length is < 3 or > 4 || (args.Length == 4 && args[3] != "--receiver-only"))
    throw new ArgumentException(
        "Pass a NEW abox-issue45-... workspace, an existing local template, Debug abox.dll, optionally --receiver-only."
    );
var workspace = Path.GetFullPath(args[0]);
if (
    !Path.GetFileName(workspace).StartsWith("abox-issue45-", StringComparison.Ordinal)
    || Directory.Exists(workspace)
)
    throw new InvalidOperationException("Workspace must be new and task-owned.");
var abox = Path.GetFullPath(args[2]);

// Deliberately disjoint from #43, Angular and the live Deals verification ports.
const int folderPort = 28345;
const int overridePort = 38345;
const int unrelatedPort = 48345;
if (
    IPGlobalProperties
        .GetIPGlobalProperties()
        .GetActiveTcpListeners()
        .Any(endpoint => new[] { folderPort, overridePort, unrelatedPort }.Contains(endpoint.Port))
)
    throw new InvalidOperationException(
        "Probe host ports must be free; existing listeners are never changed."
    );
var name = SandboxName.For(workspace);
var sbx = new SbxClient(runner, AboxPaths.For(workspace, workspace));
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
var token = timeout.Token;
Console.WriteLine($"Client: {await sbx.GetVersionAsync(token)}; disposable sandbox: {name}");
if (await sbx.FindSandboxAsync(name, token) is not null)
    throw new InvalidOperationException("Sandbox already exists; refusing to modify it.");
Directory.CreateDirectory(workspace);
var attemptedCreate = false;
try
{
    attemptedCreate = true;
    Require(
        await sbx.CreateProbeAsync(workspace, name, args[1], token),
        "Create disposable sandbox"
    );
    var receiver = await File.ReadAllTextAsync("cli/src/Abox/Sbx/application_ports.py", token);
    var checks = await File.ReadAllTextAsync(
        "docs/verification/application-ports/RuntimeProbe/ReceiverChecks.py",
        token
    );
    Require(
        await sbx.ProbePythonAsync(
            name,
            "RECEIVER = " + JsonSerializer.Serialize(receiver) + "\n" + checks,
            token
        ),
        "Receiver process checks"
    );
    if (args.Length == 4)
        return 0;

    var properties = Path.Combine(workspace, "app", "Properties");
    Directory.CreateDirectory(properties);
    await File.WriteAllTextAsync(
        Path.Combine(properties, "launchSettings.json"),
        """{"profiles":{"http":{"applicationUrl":"http://localhost:8345"}}}""",
        token
    );
    var configPath = Path.Combine(workspace, "abox.json");
    const string configuration = """{"portOffset":20000}""";
    await File.WriteAllTextAsync(configPath, configuration, token);
    Require(
        await sbx.ProbeExecAsync(
            name,
            "nohup python3 -m http.server 8345 --bind 127.0.0.1 --directory \"$WORKSPACE_DIR\" </dev/null >/tmp/abox-issue45-http.log 2>&1 &",
            token
        ),
        "Start synthetic localhost-only HTTP app"
    );
    await File.WriteAllTextAsync(
        Path.Combine(workspace, "marker.txt"),
        "abox-issue45-offset-marker",
        token
    );
    Require(
        await sbx.PublishDirectProbeMappingAsync(name, 9345, unrelatedPort, token),
        "Unrelated native binding"
    );
    await RunPorts(0);
    using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(10),
    };
    await Reach(folderPort);
    await RunPorts(0);
    var before = await Bindings();
    await RunPorts(1, "--offset", "30000");
    if ((await Bindings()) != before)
        throw new InvalidOperationException("Rejected offset change modified runtime bindings.");
    await Reach(folderPort);
    var binding = JsonDocument
        .Parse(before)
        .RootElement.EnumerateArray()
        .Single(binding => binding.GetProperty("host_port").GetInt32() == folderPort);
    var relay = binding.GetProperty("sandbox_port").GetInt32();
    Require(
        await sbx.RemoveExactProbeMappingAsync(name, relay, folderPort, token),
        "Explicitly remove only task-owned old mapping"
    );
    await RunPorts(0, "--offset", "30000");
    await Reach(overridePort);
    await RunPorts(0, "--offset", "30000");
    var final = await Bindings();
    using var document = JsonDocument.Parse(final);
    var bindings = document.RootElement.EnumerateArray().ToArray();
    if (
        bindings.Length != 2
        || !bindings.Any(binding =>
            binding.GetProperty("host_port").GetInt32() == overridePort
            && binding.GetProperty("sandbox_port").GetInt32() == relay
        )
        || !bindings.Any(binding =>
            binding.GetProperty("host_port").GetInt32() == unrelatedPort
            && binding.GetProperty("sandbox_port").GetInt32() == 9345
        )
    )
        throw new InvalidOperationException("Unexpected final mappings or lost unrelated mapping.");
    if (await File.ReadAllTextAsync(configPath, token) != configuration)
        throw new InvalidOperationException("Override changed folder configuration.");
    if (
        IPGlobalProperties
            .GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Where(endpoint => endpoint.Port == overridePort || endpoint.Port == unrelatedPort)
            .Any(endpoint => !endpoint.Address.Equals(IPAddress.Loopback))
    )
        throw new InvalidOperationException("Non-loopback host listener.");
    Console.WriteLine(
        $"Verified folder 20000 -> {folderPort}, override 30000 -> {overridePort}, safe conflict, explicit recovery, reruns, unchanged config and unrelated {unrelatedPort} -> 9345.\n{final}"
    );
    return 0;

    async Task Reach(int port)
    {
        if (
            await http.GetStringAsync($"http://localhost:{port}/marker.txt", token)
            != "abox-issue45-offset-marker"
        )
            throw new InvalidOperationException("Unexpected synthetic HTTP response.");
        Console.WriteLine($"After abox exited: localhost:{port} returned synthetic marker.");
    }
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
            Console.Error.WriteLine($"CLEANUP REQUIRED only for {name}.");
    }
    Console.WriteLine($"Synthetic workspace retained: {workspace}");
}

async Task<string> Bindings()
{
    var result = await sbx.ProbePortsAsync(name, token);
    Require(result, "Read inventory");
    return result.StandardOutput;
}

async Task RunPorts(int expected, params string[] extra)
{
    var result = await runner.RunAsync(
        new ProcessRequest(
            "dotnet",
            [abox, "ports", "--location", workspace, .. extra],
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
            $"abox ports: expected {expected}, got {result.ExitCode}."
        );
    if (expected == 1 && !result.StandardError.Contains("offset change", StringComparison.Ordinal))
        throw new InvalidOperationException("Expected explicit offset-change diagnostic.");
}

static void Require(ProcessResult result, string operation)
{
    Console.Write(result.StandardOutput);
    Console.Write(result.StandardError);
    if (!result.Succeeded)
        throw new InvalidOperationException($"{operation} failed: {result.ExitCode}.");
}
