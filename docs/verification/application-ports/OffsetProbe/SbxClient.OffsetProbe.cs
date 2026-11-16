using Abox.Infrastructure;

namespace Abox.Sbx;

// Verification only: never remove bindings outside the newly created probe sandbox.
public sealed partial class SbxClient
{
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
