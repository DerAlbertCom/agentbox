using Abox.Infrastructure;
using Abox.LocalState;

namespace Abox.Tests;

internal sealed class LocalWorkloadImagesFixture : IDisposable
{
    internal TempDirectory Root { get; } = new();
    internal IProcessRunner Runner { get; } = LocalWorkloadFactory.Runner();

    internal Task<LocalWorkloadImage> BuildAsync(string runtime)
    {
        Root.File(
            "source/workload.yaml",
            "# syntax=docker/sandbox-kit:3\nschemaVersion: \"3\"\nkind: workload\n"
        );
        Root.File("source/workload.dockerfile", "FROM example.com/base:1\nRUN install-tools\n");
        return LocalWorkloadFactory
            .Images(Runner)
            .BuildAsync(
                Path.Combine(Root.Path, "source"),
                Path.Combine(Root.Path, runtime),
                CancellationToken.None
            );
    }

    public void Dispose() => Root.Dispose();
}
