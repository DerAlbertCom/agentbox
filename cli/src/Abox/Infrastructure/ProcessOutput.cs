namespace Abox.Infrastructure;

/// <summary>
/// Opt-in process diagnostics. Captured results stay untouched for their consumers.
/// </summary>
public sealed partial class ProcessOutput(IConsole console)
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

    private sealed class SecretRedactor
    {
        private readonly HashSet<string> secrets = new(StringComparer.Ordinal);

        public SecretRedactor(ProcessRequest request)
        {
            foreach (var credential in CollectCredentials(request))
            {
                AddSecret(credential);
            }
        }

        private void AddSecret(string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                secrets.Add(value);
                foreach (
                    var line in value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                )
                {
                    secrets.Add(line);
                    if (
                        line.Length > 7
                        && line.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        secrets.Add(line[7..]);
                    }
                }
            }
        }

        public string Redact(string value)
        {
            foreach (var secret in secrets.OrderByDescending(secret => secret.Length))
            {
                value = value.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
            }

            return value;
        }
    }
}
