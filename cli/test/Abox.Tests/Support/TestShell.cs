namespace Abox.Tests;

internal static class TestShell
{
    internal static IEnumerable<string> GitDirectories(
        StringSplitOptions splitOptions = StringSplitOptions.None
    ) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, splitOptions)
            .Where(directory => File.Exists(Path.Combine(directory, "git.exe")));

    internal static string Require(string unavailableMessage) =>
        OperatingSystem.IsWindows()
            ? GitDirectories()
                .Select(directory =>
                    Path.GetFullPath(Path.Combine(directory, "..", "usr", "bin", "sh.exe"))
                )
                .FirstOrDefault(File.Exists)
                ?? throw new ShellUnavailableException(unavailableMessage)
            : "sh";
}
