using Abox.Infrastructure;

namespace Abox.Sbx;

// Verification-only source-linked partial, following the #43 RuntimeProbe pattern.
public sealed partial class SbxClient
{
    public Task<ProcessResult> CertificateProbeCreateAsync(
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
                ],
                OutputPolicy: ProcessOutputPolicy.CapturedSensitive
            ),
            token
        );

    public Task<ProcessResult> CertificateProbeExecAsync(
        string name,
        IReadOnlyList<string> argv,
        CancellationToken token
    ) =>
        runner.RunAsync(
            new ProcessRequest(
                ExecutableName,
                ["exec", name, .. argv],
                StandardInput: "",
                OutputPolicy: ProcessOutputPolicy.CapturedSensitive
            ),
            token
        );
}
