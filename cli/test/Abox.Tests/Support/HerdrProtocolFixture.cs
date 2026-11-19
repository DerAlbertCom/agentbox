using Abox.Infrastructure;
using Abox.Launchers;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

internal sealed class HerdrProtocolFixture
{
    internal IProcessRunner Runner { get; } = Substitute.For<IProcessRunner>();
    internal ISbxClient Sbx { get; } = Substitute.For<ISbxClient>();
    internal TestConsole Console { get; } = new();
    internal HerdrAppLauncher Launcher => new(Runner, Sbx, Console);
    internal List<ProcessRequest> Calls { get; } = [];
    internal LauncherContext Context { get; }
    internal Queue<ProcessResult> Statuses { get; } = new();
    internal ProcessResult StartupResult { get; set; } = new(0, "", "");
    internal ProcessResult AddResult { get; set; } = new(0, "", "");
    internal ProcessResult SpaceResult { get; set; } = new(0, "", "");

    internal HerdrProtocolFixture(
        string listing = "{\"machines\":[{\"name\":\"actual\"}]}",
        int listingExit = 0
    )
    {
        Context = new LauncherContext("folder", "actual", "/runtime/workspace", []);
        Runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessRequest>();
                Calls.Add(request);
                if (request.FileName == "hc" && request.Arguments.Contains("ls"))
                    return new ProcessResult(listingExit, listing, "listing failed");
                if (request.Arguments.SequenceEqual(["status"]))
                    return Statuses.Count > 0
                        ? Statuses.Dequeue()
                        : new ProcessResult(0, "{\"errors\":[]}", "");
                if (request.Arguments.Contains("add"))
                    return AddResult;
                if (request.Arguments.Contains("space.new"))
                    return SpaceResult;
                return new ProcessResult(0, "", "");
            });
        Runner
            .RunUntilReadyAsync(
                Arg.Any<ProcessRequest>(),
                Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(async call =>
            {
                Calls.Add(call.Arg<ProcessRequest>());
                if (StartupResult.Succeeded)
                    await call.Arg<Func<CancellationToken, Task>>()(call.Arg<CancellationToken>());
                return StartupResult;
            });
    }

    internal Task<int> LaunchAsync() => Launcher.RunAsync(Context, CancellationToken.None);
}
