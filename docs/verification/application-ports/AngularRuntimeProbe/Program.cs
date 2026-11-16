using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sbx;

var runner = new ProcessRunner();
var sbx = new SbxClient(
    runner,
    AboxPaths.For(Environment.CurrentDirectory, Environment.CurrentDirectory)
);
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
var token = timeout.Token;
if (args is ["--inventory"])
{
    Console.WriteLine($"Client: {await sbx.GetVersionAsync(token)}");
    foreach (var template in await sbx.ListTemplatesAsync(token))
        Console.WriteLine($"{template.Repository}:{template.Tag}");
    return 0;
}
if (args.Length != 4 || !int.TryParse(args[3], out var port) || port is < 1 or > 55534)
    throw new ArgumentException(
        "Pass a NEW workspace (leaf abox-issue44-...), existing local shell template, Debug abox.dll and sandbox port (1..55534)."
    );
var workspace = Path.GetFullPath(args[0]);
var abox = Path.GetFullPath(args[2]);
if (
    !Path.GetFileName(workspace).StartsWith("abox-issue44-", StringComparison.Ordinal)
    || Directory.Exists(workspace)
)
    throw new InvalidOperationException("Workspace must be new and task-owned.");
var hostPort = port + 10000;
if (
    new[] { 18203, 18204, 18205, 18206, 18213, 18223 }.Any(reserved =>
        reserved == hostPort || reserved == hostPort + 1
    )
    || IPGlobalProperties
        .GetIPGlobalProperties()
        .GetActiveTcpListeners()
        .Any(endpoint => endpoint.Port == hostPort || endpoint.Port == hostPort + 1)
)
    throw new InvalidOperationException(
        "Choose different free probe ports; protected ports and existing listeners will not be modified."
    );
var name = SandboxName.For(workspace);
Console.WriteLine(
    $"Client: {await sbx.GetVersionAsync(token)}; disposable sandbox: {name}; host ports: {hostPort}, {hostPort + 1}"
);
if (await sbx.FindSandboxAsync(name, token) is not null)
    throw new InvalidOperationException("Sandbox already exists; refusing to modify it.");
Directory.CreateDirectory(workspace);
var attemptedCreate = false;
try
{
    await File.WriteAllTextAsync(
        Path.Combine(workspace, "angular.json"),
        JsonSerializer.Serialize(
            new
            {
                projects = new
                {
                    synthetic = new
                    {
                        architect = new
                        {
                            serve = new
                            {
                                options = new { port },
                                configurations = new
                                {
                                    same = new { port },
                                    preview = new { port = port + 1 },
                                },
                            },
                        },
                    },
                },
            }
        ),
        token
    );
    await File.WriteAllTextAsync(
        Path.Combine(workspace, "package.json"),
        JsonSerializer.Serialize(new { scripts = new { start = $"ng serve --port={port}" } }),
        token
    );
    attemptedCreate = true;
    Require(
        await sbx.CreateProbeAsync(workspace, name, args[1], token),
        "Create disposable sandbox"
    );
    var prerequisites = await sbx.ProbeExecAsync(
        name,
        "printf 'ng: '; command -v ng || true; printf 'node: '; command -v node || true; python3 --version",
        token
    );
    Require(prerequisites, "Inspect existing Angular/Python prerequisites (no installation)");
    Console.WriteLine(prerequisites.StandardOutput.Trim());
    Require(
        await sbx.ProbePythonAsync(
            name,
            $$"""
            import os, subprocess, sys, time, urllib.request
            source = '''from http.server import BaseHTTPRequestHandler, HTTPServer
            class Handler(BaseHTTPRequestHandler):
                def do_GET(self):
                    self.send_response(200)
                    self.end_headers()
                    self.wfile.write(b"abox-issue44-synthetic-http-not-angular")
            HTTPServer(("127.0.0.1", {{port}}), Handler).serve_forever()
            '''
            with open(os.devnull, 'rb') as inp, open(os.devnull, 'wb') as out:
                subprocess.Popen([sys.executable, '-I', '-c', source], stdin=inp, stdout=out, stderr=out, start_new_session=True, close_fds=True)
            for attempt in range(100):
                try:
                    assert urllib.request.urlopen('http://127.0.0.1:{{port}}', timeout=1).read() == b'abox-issue44-synthetic-http-not-angular'
                    print('Synthetic Python HTTP app ready on sandbox localhost:{{port}} (NOT ng serve).')
                    break
                except OSError:
                    time.sleep(.1)
            else:
                raise RuntimeError('Synthetic HTTP app did not become ready')
            """,
            token
        ),
        "Start synthetic localhost-only HTTP listener"
    );
    using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(10),
    };
    for (var run = 1; run <= 2; run++)
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
        Require(result, $"Actual Debug abox ports run {run}");
        if (
            !result.StandardOutput.Contains("3 occurrences", StringComparison.Ordinal)
            || !result.StandardOutput.Contains("angular.json", StringComparison.Ordinal)
            || !result.StandardOutput.Contains("package.json", StringComparison.Ordinal)
        )
            throw new InvalidOperationException(
                "Angular source occurrences/duplicate warning were not reported."
            );
        var body = await http.GetStringAsync($"http://localhost:{hostPort}", token);
        if (body != "abox-issue44-synthetic-http-not-angular")
            throw new InvalidOperationException("Unexpected synthetic HTTP response.");
        Console.WriteLine(
            $"After abox run {run} exited: localhost:{hostPort} returned synthetic Python HTTP marker (NOT a real Angular app)."
        );
        var inventory = await sbx.ProbePortsAsync(name, token);
        Require(inventory, "Read task-owned mappings");
        using var document = JsonDocument.Parse(inventory.StandardOutput);
        var mappings = document.RootElement.EnumerateArray().ToArray();
        if (
            mappings.Length != 2
            || mappings.Any(mapping => mapping.GetProperty("host_ip").GetString() != "127.0.0.1")
            || mappings.Count(mapping => mapping.GetProperty("host_port").GetInt32() == hostPort)
                != 1
            || mappings.Count(mapping =>
                mapping.GetProperty("host_port").GetInt32() == hostPort + 1
            ) != 1
        )
            throw new InvalidOperationException("Expected exactly two distinct loopback mappings.");
    }
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
        "PASS: Angular discovery -> real sbx relay, command-exit HTTP persistence, duplicates, repeat reuse, IPv4-loopback-only listeners. No ng serve execution is claimed."
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

static void Require(ProcessResult result, string operation)
{
    Console.WriteLine(
        $"{operation}: exit {result.ExitCode}\n{result.StandardOutput.Trim()}\n{result.StandardError.Trim()}"
    );
    if (result.ExitCode != 0)
        throw new InvalidOperationException(operation + " failed.");
}
