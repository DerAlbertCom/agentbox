using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sbx;

if (args.Length is < 3 or > 4 || (args.Length == 4 && args[3] != "--sandbox-localhost"))
{
    Console.Error.WriteLine(
        "Pass a NEW task-owned workspace (leaf abox-issue43-...), an existing local shell template, and Debug abox.dll; optionally --sandbox-localhost."
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
        """{"profiles":{"http":{"applicationUrl":"http://localhost:8203"}}}""",
        token
    );
    attemptedCreate = true;
    Require(
        await sbx.CreateProbeAsync(workspace, name, args[1], token),
        "Create disposable sandbox"
    );
    Require(
        await sbx.ProbeExecAsync(
            name,
            "chmod +x \"$WORKSPACE_DIR/payload/SyntheticHttp\"; nohup \"$WORKSPACE_DIR/payload/SyntheticHttp\" "
                + (args.Length == 4 ? "'http://localhost:8203/' " : "")
                + "</dev/null >/tmp/abox-issue43-http.log 2>&1 &",
            token
        ),
        "Start synthetic .NET HTTP app"
    );
    await sbx.EnsureApplicationPortsAsync(name, [new(8204, 18204)], token);
    await RunPorts(0);
    var internalHttp = await sbx.ProbeExecAsync(
        name,
        "\"$WORKSPACE_DIR/payload/SyntheticHttp\" --get",
        token
    );
    Require(internalHttp, "Sandbox-local .NET HTTP request");
    if (internalHttp.StandardOutput.Trim() != "abox-issue43-dotnet-http-marker")
        throw new InvalidOperationException(
            "The synthetic application was not serving inside the sandbox."
        );
    Console.WriteLine("Inside sandbox: localhost:8203 returned synthetic .NET HTTP marker.");
    using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(10),
    };
    var body = await http.GetStringAsync("http://localhost:18203", token);
    if (body != "abox-issue43-dotnet-http-marker")
        throw new InvalidOperationException("Unexpected HTTP response.");
    Console.WriteLine("After abox exited: localhost:18203 returned synthetic .NET HTTP marker.");
    await RunPorts(0);
    await VerifyInventory();
    var listeners = IPGlobalProperties
        .GetIPGlobalProperties()
        .GetActiveTcpListeners()
        .Where(endpoint => endpoint.Port is 18203 or 18204)
        .ToArray();
    if (
        listeners.Length != 2
        || listeners.Any(endpoint => !endpoint.Address.Equals(IPAddress.Loopback))
    )
        throw new InvalidOperationException("Host listeners were not exclusively IPv4 loopback.");
    Console.WriteLine("Host listener inspection: 127.0.0.1:18203 and 127.0.0.1:18204 only.");
    var stop = await sbx.StopSandboxAsync(name, token);
    if (!stop.Succeeded)
        throw new InvalidOperationException(stop.Output);
    await File.WriteAllTextAsync(
        Path.Combine(properties, "launchSettings.json"),
        """{"profiles":{"http":{"applicationUrl":"http://localhost:8203;http://localhost:8205"}}}""",
        token
    );
    await RunPorts(0);
    await VerifyInventory();
    using var occupied = new TcpListener(IPAddress.Loopback, 18206);
    occupied.Start();
    await File.WriteAllTextAsync(
        Path.Combine(properties, "launchSettings.json"),
        """{"profiles":{"http":{"applicationUrl":"http://localhost:8206"}}}""",
        token
    );
    await RunPorts(1);
    await VerifyInventory();
    Console.WriteLine(
        "Verified repeat reuse, stopped-sandbox startup, unrelated mapping preservation and occupied-port failure. Inspect host listeners while reproducing if needed."
    );
    return 0;
}
catch (HttpRequestException) when (args.Length == 4)
{
    Console.Error.WriteLine(
        "NEGATIVE PROBE: sandbox-local HTTP succeeded, but host HTTP could not reach the localhost-only application. Use a sandbox-reachable IPv4 bind; sbx publication alone does not change application binding."
    );
    return 1;
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

async Task RunPorts(int expected)
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
        || bindings.Count(binding => binding.GetProperty("host_port").GetInt32() == 18203) != 1
        || bindings.Count(binding => binding.GetProperty("host_port").GetInt32() == 18204) != 1
    )
        throw new InvalidOperationException("Unexpected bindings or lost unrelated mapping.");
    Console.WriteLine(result.StandardOutput.Trim());
}

static void Require(ProcessResult result, string label)
{
    if (!result.Succeeded)
        throw new InvalidOperationException(
            $"{label}: {result.StandardError}{result.StandardOutput}"
        );
    Console.WriteLine($"{label}: exit 0");
}
