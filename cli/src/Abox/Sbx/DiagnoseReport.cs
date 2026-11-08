namespace Abox.Sbx;

/// <summary>The report of <c>sbx diagnose</c>.</summary>
public sealed record DiagnoseReport(IReadOnlyList<DiagnosticCheck> Checks)
{
    public DiagnosticCheck? Find(string name) =>
        Checks.FirstOrDefault(check => check.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<DiagnosticCheck> Failures =>
        Checks.Where(check => check.Failed).ToList();
}
