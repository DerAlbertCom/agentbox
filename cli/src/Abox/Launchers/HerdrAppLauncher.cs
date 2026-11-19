using System.Text.Json;
using Abox.Infrastructure;
using Abox.Sbx;

namespace Abox.Launchers;

/// <summary>
/// The launcher that opens a space in the Herdr desktop app. The Herdr app runs the agents
/// in terminals that stay alive in <c>hc</c>, so they keep running when the window closes.
/// </summary>
public sealed class HerdrAppLauncher(IProcessRunner runner, ISbxClient sbx, IConsole console)
    : ILauncher
{
    private const string AppProgram = "herdr-app";
    private const string CoreProgram = "hc";

    public string Id => "herdr-app";

    public string Summary => "A space in the Herdr desktop app";

    public IReadOnlyList<HostRequirement> HostRequirements =>
        [
            new(AppProgram, "Install the Herdr desktop app from https://herdr.dev."),
            new(CoreProgram, "Install herdr-core: it comes with the Herdr desktop app."),
            new(
                "abox",
                "abox must be on PATH: the Herdr app calls 'abox pipe' to reach the sandbox."
            ),
        ];

    public IReadOnlyList<string> SandboxRequirements => [CoreProgram];

    public async Task<int> RunAsync(LauncherContext context, CancellationToken cancellationToken)
    {
        // A newly initialized pipe cannot repair an already-running remote core.
        // Cross the shared bootstrap before the desktop can report launch success.
        if (
            await sbx.ExecAsync(
                new SbxExecRequest(context.SandboxName, ["true"]),
                cancellationToken
            ) != 0
        )
            throw new AboxException(
                "Herdr was not opened: sandbox HTTPS launch readiness failed. Exit stale agents and explicitly initialize fresh core/service parents through abox after saving work; rerun 'abox ports' for recovery guidance. No core or app was restarted."
            );
        if (!await MachineExistsAsync(context.SandboxName, cancellationToken))
        {
            await AddMachineAsync(context, cancellationToken);
        }

        await StartAppAsync(cancellationToken);

        var arguments = new List<string>
        {
            "do",
            "space.new",
            "--name",
            Path.GetFileName(
                context.Location.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                )
            ),
            "--folder",
            context.WorkspacePath,
            "--machine",
            context.SandboxName,
        };

        if (context.Argv.Count > 0)
        {
            arguments.Add("--argv");
            arguments.Add(JsonSerializer.Serialize(context.Argv));
        }

        var result = await runner.RunAsync(
            new ProcessRequest(AppProgram, arguments, AttachConsole: true),
            cancellationToken
        );

        if (!result.Succeeded)
        {
            throw new AboxException(
                $"'{AppProgram} do space.new' failed with exit code {result.ExitCode}."
            );
        }

        return 0;
    }

    private async Task<bool> MachineExistsAsync(
        string sandboxName,
        CancellationToken cancellationToken
    )
    {
        var result = await runner.RunAsync(
            new ProcessRequest(CoreProgram, ["machine", "ls", "--json"]),
            cancellationToken
        );

        if (!result.Succeeded)
        {
            throw new AboxException(
                $"'hc machine ls --json' failed with exit code {result.ExitCode}. Check 'hc machine ls' before retrying; no machine was added."
            );
        }

        return HerdrResponses.ContainsMachine(result.StandardOutput, sandboxName);
    }

    private async Task AddMachineAsync(LauncherContext context, CancellationToken cancellationToken)
    {
        var via = $"abox pipe {context.SandboxName} -- sh -c";
        var result = await runner.RunAsync(
            new ProcessRequest(
                CoreProgram,
                ["machine", "add", "--via", via, "--name", context.SandboxName]
            ),
            cancellationToken
        );

        if (!result.Succeeded)
        {
            var message = result.StandardError.Trim();
            throw new AboxException(
                $"'{CoreProgram} machine add' failed with exit code {result.ExitCode}."
                    + (message.Length > 0 ? $" {message}" : string.Empty)
            );
        }
    }

    private async Task StartAppAsync(CancellationToken cancellationToken)
    {
        // The released status client can itself wait 35 seconds. Bound the whole
        // discovery/start/readiness exchange, not just the delay between probes.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            if (await AppIsRunningAsync(deadline.Token))
                return;

            var result = await runner.RunUntilReadyAsync(
                new ProcessRequest(AppProgram, [], AttachConsole: true),
                WaitForAppAsync,
                deadline.Token
            );
            if (!result.Succeeded)
                throw new AboxException(
                    $"'herdr-app' startup failed with exit code {result.ExitCode}. Run 'herdr-app' and inspect 'herdr-app status' before retrying. Herdr space was not opened."
                );
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AboxException(
                "Herdr app readiness timed out after 45 seconds. Run 'herdr-app' and inspect 'herdr-app status' before retrying. Herdr space was not opened; no existing app or core was restarted."
            );
        }
    }

    private async Task WaitForAppAsync(CancellationToken cancellationToken)
    {
        while (!await AppIsRunningAsync(cancellationToken))
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
    }

    private async Task<bool> AppIsRunningAsync(CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(
            new ProcessRequest(AppProgram, ["status"]),
            cancellationToken
        );

        if (!result.Succeeded)
        {
            // Released control client uses exit 2 and this diagnostic only on
            // connection failure. Other control failures do not prove absence.
            if (
                result.ExitCode == 2
                && string.IsNullOrWhiteSpace(result.StandardOutput)
                && result.StandardError.StartsWith(
                    "herdr-app: no Herdr window at ",
                    StringComparison.Ordinal
                )
            )
                return false;
            throw new AboxException(
                $"'herdr-app status' failed with exit code {result.ExitCode}. Inspect the app and run 'herdr-app status' before retrying. No app or core was restarted."
            );
        }

        if (HerdrResponses.ReadConfigurationDiagnosticCount(result.StandardOutput) > 0)
            // External diagnostics may contain secrets or terminal controls. Report
            // their presence, never their contents, through the launcher's console.
            console.WriteError(
                "Warning: Herdr reports configuration diagnostics. Inspect 'herdr-app status' for details; the responsive window will still be used."
            );
        return true;
    }
}
