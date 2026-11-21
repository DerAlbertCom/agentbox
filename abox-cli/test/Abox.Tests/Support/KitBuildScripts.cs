namespace Abox.Tests;

/// <summary>Writes the executable <c>build</c> file of a local kit.</summary>
internal static class KitBuildScripts
{
    internal static string Write(TempDirectory root, string kit, string body)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException(
                Xunit.v3.DynamicSkipToken.Value + "Kit build steps need executable files."
            );
        }

        var path = root.File($"kits/{kit}/build", "#!/bin/sh\nset -e\n" + body);
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        );
        return path;
    }
}
