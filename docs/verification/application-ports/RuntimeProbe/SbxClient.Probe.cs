using Abox.Infrastructure;

namespace Abox.Sbx;

// Verification-only source-linked partial: no extra sbx entry point ships in abox.
public sealed partial class SbxClient
{
    public Task<ProcessResult> CreateProbeAsync(
        string location,
        string name,
        string template,
        CancellationToken token
    ) =>
        runner.RunAsync(
            new ProcessRequest(
                ExecutableName,
                [
                    "create",
                    "shell",
                    location,
                    "--name",
                    name,
                    "--template",
                    template,
                    "--pull",
                    "never",
                    "--static-mcp",
                    "none",
                    "--no-progress",
                    "--no-input",
                ]
            ),
            token
        );

    public Task<ProcessResult> ProbeExecAsync(
        string name,
        string script,
        CancellationToken token
    ) =>
        runner.RunAsync(
            new ProcessRequest(
                ExecutableName,
                ["exec", name, "sh", "-c", script],
                StandardInput: ""
            ),
            token
        );

    public Task<ProcessResult> ProbePortsAsync(string name, CancellationToken token) =>
        runner.RunAsync(new ProcessRequest(ExecutableName, ["ports", name, "--json"]), token);

    public Task<ProcessResult> PublishDirectProbeMappingAsync(
        string name,
        int sandboxPort,
        int hostPort,
        CancellationToken token
    ) =>
        runner.RunAsync(
            new ProcessRequest(
                ExecutableName,
                ["ports", name, "--publish", $"127.0.0.1:{hostPort}:{sandboxPort}/tcp4", "--json"]
            ),
            token
        );

    public Task<ProcessResult> ProbePythonAsync(
        string name,
        string source,
        CancellationToken token
    ) =>
        runner.RunAsync(
            new ProcessRequest(
                ExecutableName,
                ["exec", "-i", name, "python3", "-c", "import sys; exec(sys.stdin.read())"],
                StandardInput: source,
                UseUtf8StandardInput: true
            ),
            token
        );

    // Explicit probe-operator consent only, never automatic application-port cleanup.
    public Task<ProcessResult> RemoveExactProbeMappingAsync(
        string name,
        int target,
        int host,
        CancellationToken token
    ) =>
        runner.RunAsync(
            new ProcessRequest(
                ExecutableName,
                ["ports", name, "--unpublish", $"127.0.0.1:{host}:{target}/tcp4", "--json"]
            ),
            token
        );
}
