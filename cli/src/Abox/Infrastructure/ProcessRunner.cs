using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Abox.Infrastructure;

/// <summary>
/// <see cref="IProcessRunner"/> on top of <see cref="Process"/>.
/// </summary>
public sealed class ProcessRunner(ProcessOutput? output = null) : IProcessRunner
{
    private readonly Action<Process> kill = process => process.Kill(entireProcessTree: true);

    internal ProcessRunner(ProcessOutput? output, Action<Process> kill)
        : this(output) => this.kill = kill;

    public async Task<ProcessResult> RunAsync(
        ProcessRequest request,
        CancellationToken cancellationToken
    )
    {
        using var process = new Process { StartInfo = StartInfo(request) };
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();
        if (!request.AttachConsole)
            CaptureOutput(process, standardOutput, standardError);

        Start(process, request.FileName);
        await CompleteAsync(process, request, cancellationToken);

        var result = new ProcessResult(
            process.ExitCode,
            standardOutput.ToString(),
            standardError.ToString()
        );
        output?.Write(request, result);
        return result;
    }

    private static ProcessStartInfo StartInfo(ProcessRequest request)
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

        return startInfo;
    }

    private static void CaptureOutput(
        Process process,
        StringBuilder standardOutput,
        StringBuilder standardError
    )
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

    private static void Start(Process process, string fileName)
    {
        try
        {
            process.Start();
        }
        catch (Exception exception)
        {
            throw new AboxException($"Cannot start '{fileName}'. {exception.Message}", exception);
        }
    }

    private async Task CompleteAsync(
        Process process,
        ProcessRequest request,
        CancellationToken cancellationToken
    )
    {
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
                kill(process);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
                when (exception
                        is InvalidOperationException
                            or Win32Exception
                            or AggregateException
                            or OperationCanceledException
                )
            {
                // Cleanup is best effort; preserve cancellation even if children survive.
            }

            throw;
        }
    }
}
