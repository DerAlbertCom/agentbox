namespace Abox.Sbx;

/// <summary>Everything <c>sbx exec</c> needs.</summary>
/// <param name="Sandbox">The sandbox to run in.</param>
/// <param name="Command">The command and its arguments.</param>
/// <param name="AllocateTty">Whether the command gets a pseudo terminal.</param>
/// <param name="KeepStandardInputOpen">Whether standard input stays open.</param>
/// <param name="WorkingDirectory">The directory to run in, or null for the image's own.</param>
/// <param name="User">The user to run as, or null for the image's own.</param>
public sealed record SbxExecRequest(
    string Sandbox,
    IReadOnlyList<string> Command,
    bool AllocateTty = false,
    bool KeepStandardInputOpen = false,
    string? WorkingDirectory = null,
    string? User = null
);
