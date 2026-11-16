using Abox.Infrastructure;

namespace Abox.Sbx;

public sealed partial class SbxClient
{
    // Verification-only source-linked adapter: synthetic fixture operations still use SbxClient argv.
    public Task<ProcessResult> TransferProbeCheckAsync(
        string sandbox,
        string script,
        string input,
        CancellationToken token,
        bool root = false
    ) =>
        runner.RunAsync(
            new ProcessRequest(
                ExecutableName,
                root
                    ? ["exec", "-i", "-u", "root", sandbox, "python3", "-I", "-c", script]
                    : ["exec", "-i", sandbox, "python3", "-I", "-c", script],
                StandardInput: input,
                OutputPolicy: ProcessOutputPolicy.Sensitive
            ),
            token
        );
}
