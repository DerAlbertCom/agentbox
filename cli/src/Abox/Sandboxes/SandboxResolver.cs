using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sbx;

namespace Abox.Sandboxes;

/// <summary>Finds a folder's existing sandbox, preferring current names over exact legacy names.</summary>
public sealed class SandboxResolver(ISbxClient sbx, IProcessRunner runner)
{
    /// <summary>Returns the actual identity, or null only when neither candidate exists. Discovery errors propagate.</summary>
    public async Task<SandboxInfo?> FindAsync(string location, CancellationToken cancellationToken)
    {
        var current = await sbx.FindSandboxAsync(SandboxName.For(location), cancellationToken);
        if (current is not null)
            return current;
        var legacy = await LegacySandboxName.ForAsync(location, runner, cancellationToken);
        return await sbx.FindSandboxAsync(legacy, cancellationToken);
    }
}
