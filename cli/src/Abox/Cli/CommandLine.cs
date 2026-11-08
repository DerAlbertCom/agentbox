namespace Abox.Cli;

/// <summary>
/// The command line of abox.
/// </summary>
public static class CommandLine
{
    /// <summary>
    /// Splits the command line at the first "--". Everything after it belongs to the
    /// launcher or to the command that runs in the sandbox.
    /// </summary>
    public static (IReadOnlyList<string> Arguments, IReadOnlyList<string> Trailing) Split(
        IReadOnlyList<string> args
    )
    {
        var separator = args.ToList().IndexOf("--");
        if (separator < 0)
        {
            return (args, []);
        }

        return (args.Take(separator).ToList(), args.Skip(separator + 1).ToList());
    }
}
