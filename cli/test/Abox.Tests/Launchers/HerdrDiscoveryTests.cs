using Abox.Infrastructure;
using Abox.Launchers;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class HerdrDiscoveryTests
{
    [Theory]
    [InlineData("{\"machines\":[{\"name\":\"actual-other\"}]}")]
    [InlineData("{\"machines\":[{\"name\":\"ACTUAL\"}]}")]
    [InlineData("{\"machines\":[{\"name\":\"other\",\"target\":\"actual\"}]}")]
    [InlineData("{\"machines\":[]}")]
    public async Task OnlyAnExactMachineNamePreventsRegistration(string listing)
    {
        // Arrange
        var fixture = new HerdrProtocolFixture(listing);

        // Act
        await fixture.LaunchAsync();

        // Assert
        fixture.Calls.Should().Contain(r => r.FileName == "hc" && r.Arguments.Contains("add"));
    }

    [Theory]
    [InlineData(7, "{\"machines\":[]}")]
    [InlineData(0, "not json")]
    [InlineData(0, "[]")]
    [InlineData(0, "{}")]
    [InlineData(0, "{\"machines\":null}")]
    [InlineData(0, "{\"machines\":{}}")]
    [InlineData(0, "{\"machines\":[null]}")]
    [InlineData(0, "{\"machines\":[{\"name\":123}]}")]
    [InlineData(0, "{\"machines\":[{\"name\":\"actual\"},{}]}")]
    public async Task FailedOrMalformedDiscoveryNeverAddsAMachine(int exitCode, string listing)
    {
        // Arrange
        var fixture = new HerdrProtocolFixture(listing, exitCode);

        // Act
        var action = fixture.LaunchAsync;

        // Assert
        (await action.Should().ThrowAsync<AboxException>())
            .Which.Message.Should()
            .Contain("hc machine ls");
        fixture
            .Calls.Should()
            .NotContain(r => r.Arguments.Contains("add") || r.FileName == "herdr-app");
    }
}

internal sealed class HerdrProtocolFixture
{
    internal IProcessRunner Runner { get; } = Substitute.For<IProcessRunner>();
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
        var sbx = Substitute.For<ISbxClient>();
        Context = new LauncherContext(
            "folder",
            "actual",
            "/runtime/workspace",
            [],
            sbx,
            new TestConsole()
        );
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

    internal Task<int> LaunchAsync() =>
        new HerdrAppLauncher(Runner).RunAsync(Context, CancellationToken.None);
}
