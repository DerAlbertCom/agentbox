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
}
