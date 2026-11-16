using Abox.LocalState;

namespace Abox.Tests;

public sealed class BuildSourcesTests
{
    [Fact]
    public void PublicationSnapshotsPreserveFingerprintBytesEmptyDirectoriesAndExecutableModes()
    {
        // Arrange
        using var root = new TempDirectory();
        var manifest = ExecutableWorkload(root);
        manifest.SourceHashes = BuildSources.Capture(manifest);

        // Act
        var snapshot = BuildSources.Snapshot(manifest, Path.Combine(root.Path, "snapshot"));
        var destination = snapshot[manifest.WorkloadKitDirectory];
        var copied = new BuildManifest { WorkloadKitDirectory = destination };
        var copiedHashes = BuildSources.Capture(copied);

        // Assert: these fixed digests describe the existing fingerprint format, not a new one.
        var expectedHash = OperatingSystem.IsWindows()
            ? "BDF24390343906703F41A68ECA7BFF9B3F509D7D4A2C6398E8D2DEDA0180C88E"
            : "E18E2ED65C273ED5C1E361D4CB430716C7B61809BE29C4CFA133DA649777A811";
        copiedHashes.Should().ContainSingle().Which.Value.Should().Be(expectedHash);
        snapshot
            .Should()
            .ContainSingle()
            .Which.Value.Should()
            .Be(Path.Combine(root.Path, "snapshot", "0", "workload"));
        File.ReadAllText(Path.Combine(destination, "install.sh"))
            .Should()
            .Be("#!/bin/sh\nexit 0\n");
        Directory.Exists(Path.Combine(destination, "empty")).Should().BeTrue();
    }

    private static BuildManifest ExecutableWorkload(TempDirectory root)
    {
        var source = root.Directory_("workload");
        var empty = root.Directory_("workload/empty");
        var script = root.File("workload/install.sh", "#!/bin/sh\nexit 0\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                empty,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
            File.SetUnixFileMode(
                script,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
            );
        }
        return new BuildManifest
        {
            WorkloadKitDirectory = source,
            Kits = [new ManifestKit { Reference = source, IsLocal = true }],
        };
    }
}
