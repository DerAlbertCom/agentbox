using Abox.Infrastructure;
using Abox.Kits;

namespace Abox.Features.Build;

/// <summary>
/// The kit build step: a local kit with a <c>build</c> file is copied into the build's
/// staging folder and built there, and the staged copy replaces the kit's folder. Kits
/// without a <c>build</c> file pass through unchanged; a <c>build</c> file that cannot run
/// (not executable, or on Windows) fails the build.
/// </summary>
public sealed class KitBuildStep(IProcessRunner runner, IConsole console)
{
    /// <summary>The executable file that marks a kit with a build step.</summary>
    public const string BuildFileName = "build";

    /// <summary>The folder of the build directory that holds the staged kits.</summary>
    public const string StagingFolderName = "kits";

    /// <summary>Builds the kits that have a build step and returns the kits sbx is given.</summary>
    public async Task<IReadOnlyList<KitSource>> BuildAsync(
        IReadOnlyList<KitSource> kits,
        string buildDirectory,
        CancellationToken cancellationToken
    )
    {
        var staging = Path.Combine(buildDirectory, StagingFolderName);
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        var result = new List<KitSource>();
        foreach (var kit in kits)
        {
            result.Add(await BuildAsync(kit, staging, cancellationToken));
        }

        return result;
    }

    private async Task<KitSource> BuildAsync(
        KitSource kit,
        string staging,
        CancellationToken cancellationToken
    )
    {
        if (kit.Directory is not { } source || !File.Exists(Path.Combine(source, BuildFileName)))
        {
            return kit;
        }

        EnsureRunnable(kit.Name, Path.Combine(source, BuildFileName));

        var staged = Path.Combine(
            staging,
            Path.GetFileName(Path.TrimEndingDirectorySeparator(source))
        );
        CopyDirectory(source, staged);

        console.WriteLine($"Building kit '{kit.Name}'...");
        var result = await runner.RunAsync(
            new ProcessRequest(
                Path.Combine(source, BuildFileName),
                [staged],
                WorkingDirectory: source
            ),
            cancellationToken
        );
        if (!result.Succeeded)
        {
            throw new AboxException(
                $"The build of kit '{kit.Name}' failed with exit code {result.ExitCode}:{Environment.NewLine}{(result.StandardOutput + result.StandardError).Trim()}"
            );
        }

        return kit with
        {
            Directory = staged,
        };
    }

    private static void EnsureRunnable(string kit, string build)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new AboxException(
                $"The kit '{kit}' has a build step ({build}), and kit build steps run only on Linux or macOS. Build this kit there."
            );
        }

        if (!File.GetUnixFileMode(build).HasFlag(UnixFileMode.UserExecute))
        {
            throw new AboxException(
                $"The build file of kit '{kit}' is not executable: {build}. Make it executable with 'chmod +x', or remove it if the kit has no build step."
            );
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var copied = Path.Combine(destination, Path.GetFileName(entry));
            var info = new FileInfo(entry);
            if (info.LinkTarget is { } target)
            {
                File.CreateSymbolicLink(copied, target);
            }
            else if (Directory.Exists(entry))
            {
                CopyDirectory(entry, copied);
            }
            else
            {
                File.Copy(entry, copied);
            }

            if (info.LinkTarget is null && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(copied, File.GetUnixFileMode(entry));
            }
        }
    }
}
