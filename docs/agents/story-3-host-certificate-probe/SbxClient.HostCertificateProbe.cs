using Abox.Infrastructure;

namespace Abox.Sbx;

// Verification-only source-linked partial. No extra product sbx entry point.
public sealed partial class SbxClient
{
    internal Task<ProcessResult> HostCertificateProbeAsync(
        string source,
        string mode,
        string? frame,
        CancellationToken token
    ) =>
        runner.RunAsync(
            new ProcessRequest(
                ExecutableName,
                frame is null
                    ? ["exec", "rwe-ai-sbx-41bc30", "python3", "-I", "-c", source, mode]
                    : ["exec", "-i", "rwe-ai-sbx-41bc30", "python3", "-I", "-c", source, mode],
                StandardInput: frame ?? "",
                OutputPolicy: mode == "receive"
                    ? ProcessOutputPolicy.Sensitive
                    : ProcessOutputPolicy.CapturedSensitive,
                UseUtf8StandardInput: true
            ),
            token
        );

    internal Task<ProcessResult> HostCertificateProbePortsAsync(CancellationToken token) =>
        runner.RunAsync(
            new ProcessRequest(
                ExecutableName,
                ["ports", "rwe-ai-sbx-41bc30", "--json"],
                StandardInput: "",
                OutputPolicy: ProcessOutputPolicy.CapturedSensitive
            ),
            token
        );

    internal Task<ProcessResult> HostCertificateTrustAsync(
        string source,
        string mode,
        string frame,
        CancellationToken token
    ) =>
        runner.RunAsync(
            new ProcessRequest(
                ExecutableName,
                [
                    "exec",
                    "-i",
                    "-u",
                    "root",
                    "rwe-ai-sbx-41bc30",
                    "python3",
                    "-I",
                    "-c",
                    source,
                    mode,
                ],
                StandardInput: frame,
                OutputPolicy: ProcessOutputPolicy.CapturedSensitive,
                UseUtf8StandardInput: true
            ),
            token
        );
}
