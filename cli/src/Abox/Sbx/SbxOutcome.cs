namespace Abox.Sbx;

/// <summary>
/// What sbx said when it was done.
/// </summary>
/// <param name="ExitCode">The exit code of sbx.</param>
/// <param name="Output">What sbx wrote, so a failure can be shown to the user.</param>
public sealed record SbxOutcome(int ExitCode, string Output)
{
    public bool Succeeded => ExitCode == 0;
}
