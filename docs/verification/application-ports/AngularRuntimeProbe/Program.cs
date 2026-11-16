using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Nodes;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sbx;

var runner = new ProcessRunner();
var sbx = new SbxClient(
    runner,
    AboxPaths.For(Environment.CurrentDirectory, Environment.CurrentDirectory)
);
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
var token = timeout.Token;
if (args is ["--inventory"])
{
    Console.WriteLine($"Client: {await sbx.GetVersionAsync(token)}");
    foreach (var template in await sbx.ListTemplatesAsync(token))
        Console.WriteLine($"{template.Repository}:{template.Tag}");
    return 0;
}
if (args.Length != 5 || !int.TryParse(args[3], out var port) || port is < 1 or > 55534)
    throw new ArgumentException(
        "Pass a NEW workspace (leaf abox-issue44-...), existing local Linux x64 shell template, Debug abox.dll, sandbox port (1..55534), and existing host npm-cli.js path."
    );
var workspace = Path.GetFullPath(args[0]);
var abox = Path.GetFullPath(args[2]);
var npmCli = Path.GetFullPath(args[4]);
if (!File.Exists(npmCli))
    throw new ArgumentException(
        "Pass the existing host npm-cli.js; no global installation is performed."
    );
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
    var fixture = Path.Combine(AppContext.BaseDirectory, "fixture");
    foreach (var source in Directory.EnumerateFiles(fixture, "*", SearchOption.AllDirectories))
    {
        var target = Path.Combine(workspace, Path.GetRelativePath(fixture, source));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target);
    }
    var angularPath = Path.Combine(workspace, "angular.json");
    var angular = JsonNode.Parse(await File.ReadAllTextAsync(angularPath, token))!;
    var serve = angular["projects"]!["synthetic"]!["architect"]!["serve"]!;
    serve["options"]!["port"] = port;
    serve["configurations"]!["same"]!["port"] = port;
    serve["configurations"]!["preview"]!["port"] = port + 1;
    await File.WriteAllTextAsync(angularPath, angular.ToJsonString(), token);
    var packagePath = Path.Combine(workspace, "package.json");
    var package = JsonNode.Parse(await File.ReadAllTextAsync(packagePath, token))!;
    package["scripts"]!["start"] = $"ng serve --port={port}";
    await File.WriteAllTextAsync(packagePath, package.ToJsonString(), token);
    var originalAngular = await File.ReadAllTextAsync(angularPath, token);
    var originalPackage = await File.ReadAllTextAsync(packagePath, token);
    attemptedCreate = true;
    Require(
        await sbx.CreateProbeAsync(workspace, name, args[1], token),
        "Create disposable sandbox"
    );
    var prerequisites = await sbx.ProbeExecAsync(
        name,
        "node --version && npm --version && python3 --version",
        token
    );
    Require(
        prerequisites,
        "Inspect existing Node/npm/Python prerequisites (no global installation)"
    );
    await RunAboxAsync(0);
    var originalMappings = await ReadMappingsAsync();
    Require(
        await sbx.ProbePythonAsync(
            name,
            $$"""
            import os, socket
            from pathlib import Path
            os.chdir(os.environ['WORKSPACE_DIR'])
            assert not Path('DISCOVERY-EXECUTED-SCRIPT').exists(), 'Discovery ran npm prestart'
            assert not Path('node_modules').exists(), 'Discovery installed dependencies'
            for family, address in [(socket.AF_INET, '127.0.0.1'), (socket.AF_INET6, '::1')]:
                with socket.socket(family) as connection:
                    connection.settimeout(1)
                    assert connection.connect_ex((address, {{port}})) != 0, 'Discovery started an app'
            print('PASS: discovery exited before npm install; no script sentinel, dependencies or app listener.')
            """,
            token
        ),
        "Verify discovery did not execute package scripts or start Angular"
    );
    Require(
        await runner.RunAsync(new ProcessRequest("node", ["--version"]), token),
        "Existing host Node version"
    );
    Require(
        await runner.RunAsync(new ProcessRequest("node", [npmCli, "--version"]), token),
        "Existing host npm version"
    );
    Require(
        await runner.RunAsync(
            new ProcessRequest(
                "node",
                [
                    npmCli,
                    "ci",
                    "--os=linux",
                    "--cpu=x64",
                    "--libc=glibc",
                    "--ignore-scripts",
                    "--no-audit",
                    "--no-fund",
                    "--registry=https://registry.npmjs.org",
                    "--cache=.abox/npm-cache",
                ],
                WorkingDirectory: workspace,
                StandardInput: ""
            ),
            token
        ),
        "Host npm ci: locked Linux x64 dependencies in the disposable mounted fixture only"
    );
    Require(
        await sbx.ProbePythonAsync(
            name,
            $$"""
            import os, subprocess, sys, time, urllib.request
            from pathlib import Path
            os.chdir(os.environ['WORKSPACE_DIR'])
            env = dict(os.environ, CI='true', NG_CLI_ANALYTICS='false')
            subprocess.run(['node', 'node_modules/@angular/cli/bin/ng.js', 'version'], env=env, check=True)
            assert not Path('DISCOVERY-EXECUTED-SCRIPT').exists()
            log = Path('.abox/ng-serve.log')
            with open(os.devnull, 'rb') as inp, log.open('wb') as out:
                app = subprocess.Popen(['node', 'node_modules/@angular/cli/bin/ng.js', 'serve', '--port={{port}}'],
                                       env=env, stdin=inp, stdout=out, stderr=out,
                                       start_new_session=True, close_fds=True)
            Path('.abox/ng-serve.pid').write_text(str(app.pid))
            for attempt in range(360):
                try:
                    body = urllib.request.urlopen('http://localhost:{{port}}', timeout=1).read()
                    assert b'abox-issue44-angular-index' in body and b'/@vite/client' in body
                    print('Actual local Angular CLI ng serve ready on unchanged localhost:{{port}}; PID', app.pid)
                    break
                except OSError:
                    if app.poll() is not None:
                        raise RuntimeError(log.read_text())
                    time.sleep(.5)
            else:
                raise RuntimeError('Angular did not become ready: ' + log.read_text())
            print(log.read_text())
            """,
            token
        ),
        "Start actual Angular ng serve using the task-local locked dependencies"
    );
    using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(10),
    };
    for (var run = 1; run <= 2; run++)
    {
        await RunAboxAsync(run);
        var body = await http.GetStringAsync($"http://localhost:{hostPort}", token);
        if (
            !body.Contains("abox-issue44-angular-index", StringComparison.Ordinal)
            || !body.Contains("/@vite/client", StringComparison.Ordinal)
        )
            throw new InvalidOperationException(
                "Expected HTML from the real Angular dev server, not a stand-in."
            );
        var javascript = await http.GetStringAsync($"http://localhost:{hostPort}/main.js", token);
        if (!javascript.Contains("abox-issue44-real-angular-component", StringComparison.Ordinal))
            throw new InvalidOperationException("Angular did not serve the compiled component.");
        Console.WriteLine(
            $"After abox run {run} exited: localhost:{hostPort} returned Angular/Vite HTML and the compiled component marker in /main.js."
        );
        if (await ReadMappingsAsync() != originalMappings)
            throw new InvalidOperationException(
                "Repeat invocation changed the existing relay mappings."
            );
    }
    Require(
        await sbx.ProbePythonAsync(
            name,
            $$"""
            import os
            from pathlib import Path
            os.chdir(os.environ['WORKSPACE_DIR'])
            pid = int(Path('.abox/ng-serve.pid').read_text())
            assert Path('/proc/%d/exe' % pid).resolve().name == 'node'
            command = Path('/proc/%d/cmdline' % pid).read_bytes().replace(b'\0', b' ')
            print('Live Node command:', command.decode())
            listeners = []
            for table, allowed in [('tcp', '0100007F'), ('tcp6', '00000000000000000000000001000000')]:
                for line in Path('/proc/net/' + table).read_text().splitlines()[1:]:
                    fields = line.split()
                    address, hexport = fields[1].split(':')
                    if int(hexport, 16) == {{port}} and fields[3] == '0A':
                        assert address == allowed, 'App has a non-loopback listener: ' + line
                        listeners.append(table + ':' + fields[1])
            assert listeners, 'No Angular loopback listener'
            assert not Path('DISCOVERY-EXECUTED-SCRIPT').exists()
            print('PASS: real Angular remains localhost-only, with no --host override:', listeners)
            """,
            token
        ),
        "Inspect actual Angular Node process and sandbox listener addresses"
    );
    if (
        await File.ReadAllTextAsync(angularPath, token) != originalAngular
        || await File.ReadAllTextAsync(packagePath, token) != originalPackage
    )
        throw new InvalidOperationException(
            "Application config was modified during discovery/publishing."
        );
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
        "PASS: actual Angular ng serve -> real sbx relay, command-exit HTML/compiled component, no discovery execution, duplicates, unchanged mapping reuse, sandbox localhost-only and host IPv4-loopback-only listeners. Browser rendering/HMR not tested."
    );
    return 0;

    async Task RunAboxAsync(int run)
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
    }

    async Task<string> ReadMappingsAsync()
    {
        var inventory = await sbx.ProbePortsAsync(name, token);
        Require(inventory, "Read task-owned mappings");
        using var document = JsonDocument.Parse(inventory.StandardOutput);
        var mappings = document.RootElement.EnumerateArray().ToArray();
        if (
            mappings.Length != 2
            || mappings.Any(mapping =>
                mapping.GetProperty("host_ip").GetString() != "127.0.0.1"
                || mapping.GetProperty("protocol").GetString() != "tcp4"
            )
            || mappings.Count(mapping => mapping.GetProperty("host_port").GetInt32() == hostPort)
                != 1
            || mappings.Count(mapping =>
                mapping.GetProperty("host_port").GetInt32() == hostPort + 1
            ) != 1
        )
            throw new InvalidOperationException(
                "Expected exactly two distinct loopback TCP mappings."
            );
        return string.Join(
            ",",
            mappings
                .OrderBy(mapping => mapping.GetProperty("host_port").GetInt32())
                .Select(mapping =>
                    $"{mapping.GetProperty("host_port")}:{mapping.GetProperty("sandbox_port")}"
                )
        );
    }
}
finally
{
    var canRemoveWorkspace = !attemptedCreate;
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
        canRemoveWorkspace = removed.Succeeded;
    }
    if (canRemoveWorkspace)
    {
        Directory.Delete(workspace, recursive: true);
        Console.WriteLine($"Removed exact task-owned synthetic workspace: {workspace}");
    }
}

static void Require(ProcessResult result, string operation)
{
    Console.WriteLine(
        $"{operation}: exit {result.ExitCode}\n{result.StandardOutput.Trim()}\n{result.StandardError.Trim()}"
    );
    if (result.ExitCode != 0)
        throw new InvalidOperationException(operation + " failed.");
}
