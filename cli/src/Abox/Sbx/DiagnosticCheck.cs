namespace Abox.Sbx;

/// <summary>One check of <c>sbx diagnose</c>.</summary>
/// <param name="Name">The name of the check.</param>
/// <param name="Status">"pass", "fail" or "skip".</param>
/// <param name="Message">What the check found.</param>
/// <param name="Hint">What to do about it, when the check failed.</param>
public sealed record DiagnosticCheck(string Name, string Status, string Message, string Hint)
{
    public bool Failed => Status.Equals("fail", StringComparison.OrdinalIgnoreCase);
}
