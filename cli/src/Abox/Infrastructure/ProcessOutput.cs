using System.Collections;
using System.Text.RegularExpressions;

namespace Abox.Infrastructure;

/// <summary>
/// Opt-in process diagnostics. Captured results stay untouched for their consumers.
/// </summary>
public sealed partial class ProcessOutput(IConsole console)
{
    public bool Verbose { get; set; }

    public void Write(ProcessRequest request, ProcessResult result)
    {
        if (!Verbose || request.AttachConsole)
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
            AddSecret(request.StandardInput);
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                if (SensitiveName().IsMatch((string)entry.Key))
                {
                    AddSecret(entry.Value as string);
                }
            }

            if (request.Environment is not null)
            {
                foreach (var (name, value) in request.Environment)
                {
                    if (SensitiveName().IsMatch(name))
                    {
                        AddSecret(value);
                    }
                }
            }

            for (var index = 0; index < request.Arguments.Count; index++)
            {
                var argument = request.Arguments[index];
                AddUriSecrets(argument);

                var separator = argument.IndexOf('=');
                if (separator > 0 && SensitiveName().IsMatch(argument[..separator]))
                {
                    AddSecret(argument[(separator + 1)..]);
                }
                else if (
                    argument.StartsWith('-')
                    && SensitiveName().IsMatch(argument)
                    && index + 1 < request.Arguments.Count
                )
                {
                    AddSecret(request.Arguments[index + 1]);
                }
            }
        }

        private void AddUriSecrets(string argument)
        {
            if (!Uri.TryCreate(argument, UriKind.Absolute, out var uri))
                return;

            // Uri normalizes some escapes; retain credentials exactly as supplied too.
            var authorityStart = argument.IndexOf("://", StringComparison.Ordinal);
            if (authorityStart >= 0)
            {
                var authority = argument[(authorityStart + 3)..].Split(['/', '?', '#'], 2)[0];
                var userInfoEnd = authority.LastIndexOf('@');
                if (userInfoEnd >= 0)
                {
                    var userInfo = authority[..userInfoEnd];
                    AddSecret(userInfo);
                    var colon = userInfo.IndexOf(':');
                    if (colon >= 0)
                        AddSecret(userInfo[(colon + 1)..]);
                }
            }
            AddSecret(uri.UserInfo);
            AddSecret(Uri.UnescapeDataString(uri.UserInfo));
            var password = uri.UserInfo.IndexOf(':');
            if (password >= 0)
            {
                AddSecret(uri.UserInfo[(password + 1)..]);
                AddSecret(Uri.UnescapeDataString(uri.UserInfo[(password + 1)..]));
            }
            foreach (var parameter in uri.Query.TrimStart('?').Split('&'))
            {
                var equals = parameter.IndexOf('=');
                if (equals > 0 && SensitiveName().IsMatch(parameter[..equals]))
                {
                    AddSecret(parameter[(equals + 1)..]);
                    AddSecret(Uri.UnescapeDataString(parameter[(equals + 1)..]));
                }
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

    [GeneratedRegex(
        "token|secret|password|passwd|credential|authorization|api[-_]?key|access[-_]?key|private[-_]?key",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex SensitiveName();
}
