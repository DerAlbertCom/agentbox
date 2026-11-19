using System.Text.Json;
using Abox.Infrastructure;

namespace Abox.Sbx;

internal sealed class SbxInvoker(IProcessRunner runner, string executableName)
{
    internal string ExecutableName => executableName;

    internal Task<ProcessResult> RunAsync(
        ProcessRequest request,
        CancellationToken cancellationToken
    ) => runner.RunAsync(request, cancellationToken);

    internal async Task<SbxOutcome> RunOutcomeAsync(
        ProcessRequest request,
        CancellationToken cancellationToken
    )
    {
        var result = await RunAsync(request, cancellationToken);
        return new SbxOutcome(result.ExitCode, result.StandardOutput + result.StandardError);
    }

    internal async Task<ProcessResult> RunOrThrowAsync(
        ProcessRequest request,
        string command,
        CancellationToken cancellationToken
    )
    {
        var result = await RunAsync(request, cancellationToken);
        if (!result.Succeeded)
            throw new AboxException($"'{command}' failed: {Describe(result)}");
        return result;
    }

    internal async Task<T> RunJsonAsync<T>(
        IReadOnlyList<string> arguments,
        string command,
        CancellationToken cancellationToken
    )
        where T : class
    {
        var result = await RunOrThrowAsync(
            new ProcessRequest(ExecutableName, arguments),
            command,
            cancellationToken
        );
        return Deserialize<T>(result.StandardOutput, command);
    }

    internal static T Deserialize<T>(string json, string command)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new AboxException($"'{command}' wrote no output.");
        try
        {
            return JsonSerializer.Deserialize<T>(json)
                ?? throw new AboxException($"'{command}' wrote no output.");
        }
        catch (JsonException exception)
        {
            throw new AboxException(
                $"Cannot read the output of '{command}': {exception.Message}",
                exception
            );
        }
    }

    internal static string Describe(ProcessResult result)
    {
        var text = (result.StandardError + result.StandardOutput).Trim();
        return string.IsNullOrEmpty(text) ? $"exit code {result.ExitCode}" : text;
    }
}
