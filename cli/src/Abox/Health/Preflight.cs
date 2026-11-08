using Abox.Infrastructure;
using Abox.Sbx;

namespace Abox.Health;

/// <summary>
/// The checks every command runs before it does anything: sbx is installed, it is new
/// enough for v3 kits, and it is healthy and signed in.
/// </summary>
public sealed class Preflight(IConsole console, ISbxClient sbx, IHostPrograms hostPrograms)
{
    /// <summary>
    /// Runs the checks. Returns false when a check failed and the command must not go on.
    /// </summary>
    public async Task<bool> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (!await EnsureInstalledAsync(cancellationToken))
        {
            return false;
        }

        if (!await EnsureVersionAsync(cancellationToken))
        {
            return false;
        }

        return await EnsureHealthyAsync(cancellationToken);
    }

    private async Task<bool> EnsureInstalledAsync(CancellationToken cancellationToken)
    {
        if (hostPrograms.Exists("sbx"))
        {
            return true;
        }

        console.WriteError("sbx is not on PATH. abox needs it and never installs anything itself.");
        foreach (var line in InstallInstructions.ForCurrentSystem())
        {
            console.WriteError(line);
        }

        return false;
    }

    private async Task<bool> EnsureVersionAsync(CancellationToken cancellationToken)
    {
        Version version;
        try
        {
            version = await sbx.GetVersionAsync(cancellationToken);
        }
        catch (AboxException exception)
        {
            console.WriteError(exception.Message);
            return false;
        }

        if (version >= new Version(ISbxClient.MinimumVersion))
        {
            return true;
        }

        console.WriteError(
            $"sbx {version} is too old. abox needs {ISbxClient.MinimumVersion} or later."
        );
        console.WriteError("Upgrade sbx and try again.");
        return false;
    }

    private async Task<bool> EnsureHealthyAsync(CancellationToken cancellationToken)
    {
        DiagnoseReport report;
        try
        {
            report = await sbx.DiagnoseAsync(cancellationToken);
        }
        catch (AboxException exception)
        {
            console.WriteError(exception.Message);
            return false;
        }

        var authentication = report.Find("Authentication");
        if (authentication is { Failed: true })
        {
            if (!await SignInAsync(cancellationToken))
            {
                return false;
            }

            report = await sbx.DiagnoseAsync(cancellationToken);
        }

        var failures = report.Failures;
        if (failures.Count == 0)
        {
            return true;
        }

        foreach (var failure in failures)
        {
            console.WriteError($"sbx check '{failure.Name}' failed: {failure.Message}");
            if (!string.IsNullOrWhiteSpace(failure.Hint))
            {
                console.WriteError(failure.Hint);
            }
        }

        return false;
    }

    private async Task<bool> SignInAsync(CancellationToken cancellationToken)
    {
        if (!console.IsInteractive)
        {
            console.WriteError("sbx is not signed in. Run 'sbx login' and try again.");
            return false;
        }

        console.WriteLine("Signing in to Docker...");

        var result = await sbx.SignInAsync(cancellationToken);
        if (!result)
        {
            console.WriteError("Signing in failed. Run 'sbx login' and try again.");
            return false;
        }

        return true;
    }
}
