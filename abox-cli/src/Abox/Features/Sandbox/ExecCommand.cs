using System.CommandLine;
using Abox.Cli;
using Abox.Infrastructure;
using Abox.Sbx;

namespace Abox.Features.Sandbox;

/// <summary>
/// <c>abox exec</c>: runs a command in a sandbox, starting the sandbox when it is stopped.
/// It is not a user command and stays out of the help.
/// </summary>
public sealed class ExecCommand : Command
{
    public ExecCommand(ISbxClient sbx, TrailingArguments trailing, IConsole console)
        : base("exec", "Run a command in a sandbox.") =>
        SandboxCommand.Configure(this, sbx, trailing, console);
}
