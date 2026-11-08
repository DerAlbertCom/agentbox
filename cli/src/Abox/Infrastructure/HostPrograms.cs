namespace Abox.Infrastructure;

/// <summary>
/// Finds programs on the host's PATH.
/// </summary>
public interface IHostPrograms
{
    bool Exists(string program);
}

public sealed class HostPrograms(string pathDirectories) : IHostPrograms
{
    public HostPrograms()
        : this(Environment.GetEnvironmentVariable("PATH") ?? string.Empty) { }

    public bool Exists(string program)
    {
        if (string.IsNullOrWhiteSpace(program))
        {
            return false;
        }

        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries
            )
            : [string.Empty];

        foreach (
            var directory in pathDirectories.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries
            )
        )
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, program + extension);
                if (File.Exists(candidate))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
