namespace Abox.Cli;

/// <summary>
/// The arguments after the "--" of the command line. They belong to the launcher or to
/// the command that runs in the sandbox, not to abox.
/// </summary>
public sealed class TrailingArguments
{
    /// <summary>The arguments after the "--".</summary>
    public IReadOnlyList<string> Values { get; set; } = [];
}
