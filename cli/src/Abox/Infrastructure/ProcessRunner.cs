using System.Diagnostics;
using System.Text;

namespace Abox.Infrastructure;

/// <summary>
/// <see cref="IProcessRunner"/> on top of <see cref="Process"/>.
/// </summary>
public sealed class ProcessRunner(ProcessOutput? output = null) : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        ProcessRequest request,
        CancellationToken cancellationToken
    )
    {
        var startInfo = new ProcessStartInfo(request.FileName)
        {
            UseShellExecute = false,
            WorkingDirectory = request.WorkingDirectory ?? Environment.CurrentDirectory,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.Environment is not null)
        {
            foreach (var (name, value) in request.Environment)
            {
                startInfo.Environment[name] = value;
            }
        }

        if (!request.AttachConsole)
        {
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
        }

        if (request.StandardInput is not null)
        {
            startInfo.RedirectStandardInput = true;
        }

        using var process = new Process { StartInfo = startInfo };

        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();

        if (!request.AttachConsole)
        {
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    standardOutput.AppendLine(e.Data);
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    standardError.AppendLine(e.Data);
                }
            };
        }

        try
        {
            process.Start();
        }
        catch (Exception exception)
        {
            throw new AboxException(
                $"Cannot start '{request.FileName}'. {exception.Message}",
                exception
            );
        }

        try
        {
            if (!request.AttachConsole)
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            if (request.StandardInput is not null)
            {
                await process.StandardInput.WriteAsync(
                    request.StandardInput.AsMemory(),
                    cancellationToken
                );
                await process.StandardInput.FlushAsync(cancellationToken);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation during input or exit must not leave children or their workers running.
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // The process already exited.
            }

            throw;
        }

        var result = new ProcessResult(
            process.ExitCode,
            standardOutput.ToString(),
            standardError.ToString()
        );
        output?.Write(request, result);
        return result;
    }
}
