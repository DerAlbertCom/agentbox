namespace Abox.Infrastructure;

/// <summary>
/// Opt-in process diagnostics. Captured results stay untouched for their consumers.
/// </summary>
public sealed class ProcessOutput(IConsole console)
{
    public bool Verbose { get; set; }

    public void Write(ProcessRequest request, ProcessResult result)
    {
        if (!Verbose || request.AttachConsole || request.OutputPolicy != ProcessOutputPolicy.Normal)
        {
            return;
        }

        var redactor = new SecretRedactor(request);
        var tool = Path.GetFileName(request.FileName);
        console.WriteError(redactor.Redact($"[tool: {tool}; exit code: {result.ExitCode}]"));
        WriteStream("stdout", result.StandardOutput, redactor);
        WriteStream("stderr", result.StandardError, redactor);
    }

    private void WriteStream(string name, string text, SecretRedactor redactor)
    {
        if (text.Length > 0)
        {
            console.WriteError($"[{name}]");
            console.WriteError(redactor.Redact(text));
        }
    }
}
