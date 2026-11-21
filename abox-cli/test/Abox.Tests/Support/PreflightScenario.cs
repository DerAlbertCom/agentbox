using Abox.Health;
using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

/// <summary>Synthetic sign-in recovery shared by Health and composed command tests.</summary>
internal sealed class PreflightScenario
{
    public PreflightScenario(bool interactive = true)
    {
        Console = new TestConsole(interactive);
        Programs.Exists("sbx").Returns(true);
        Sbx.GetVersionAsync(Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                CommandToken = call.Arg<CancellationToken>();
                return new Version(0, 51, 0);
            });
        Sbx.DiagnoseAsync(Arg.Any<CancellationToken>()).Returns(SignedOut(), Healthy());
        Sbx.SignInAsync(Arg.Any<CancellationToken>()).Returns(true);
    }

    public TestConsole Console { get; }
    public ISbxClient Sbx { get; } = Substitute.For<ISbxClient>();
    public IHostPrograms Programs { get; } = Substitute.For<IHostPrograms>();
    public CancellationToken CommandToken { get; private set; }
    public Preflight Preflight => new(Console, Sbx, Programs);

    public IEnumerable<string> SbxCalls =>
        Sbx.ReceivedCalls().Select(call => call.GetMethodInfo().Name);

    public static DiagnoseReport SignedOut() =>
        new([
            new DiagnosticCheck(
                "Authentication",
                "fail",
                "credential-sentinel: expired synthetic credential",
                "credential-sentinel: raw authentication diagnostic"
            ),
        ]);

    public static DiagnoseReport Healthy() =>
        new([
            new DiagnosticCheck("Authentication", "pass", "authenticated", ""),
            new DiagnosticCheck("Daemon", "pass", "healthy", ""),
        ]);
}
