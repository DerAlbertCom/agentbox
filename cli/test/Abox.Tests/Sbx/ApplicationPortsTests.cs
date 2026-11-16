using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class ApplicationPortsTests : IDisposable
{
    private readonly TempDirectory root = new();
    private readonly IProcessRunner runner = Substitute.For<IProcessRunner>();
    private readonly List<ProcessRequest> calls = [];
    private string inventory = "[]";
    private ProcessResult? publishFailure;
    private bool acknowledgeWithoutPublishing;
    private readonly SbxClient client;

    public ApplicationPortsTests()
    {
        client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessRequest>();
                calls.Add(request);
                if (request.Arguments.Contains("--publish") && publishFailure is not null)
                    return publishFailure;
                if (request.Arguments.Contains("--publish") && !acknowledgeWithoutPublishing)
                    inventory = """
                        [{"host_ip":"127.0.0.1","host_port":18203,"sandbox_port":8203,"protocol":"tcp4"},
                         {"host_ip":"127.0.0.1","host_port":19000,"sandbox_port":9000,"protocol":"tcp4"}]
                        """;
                return new ProcessResult(0, request.Arguments[0] == "ports" ? inventory : "", "");
            });
    }

    [Fact]
    public async Task MissingPostPublishConfirmationIsNotReportedAsSuccess()
    {
        // Arrange
        acknowledgeWithoutPublishing = true;

        // Act
        var action = () =>
            client.EnsureApplicationPortsAsync(
                "actual",
                [new(8203, 18203)],
                CancellationToken.None
            );

        // Assert
        await action.Should().ThrowAsync<AboxException>().WithMessage("*did not confirm*retry*");
    }

    [Fact]
    public async Task PreCancelledPublishingMakesNoRuntimeCalls()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var action = () =>
            client.EnsureApplicationPortsAsync("actual", [new(8203, 18203)], cancellation.Token);

        // Assert
        await action.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData(
        "[{\"host_ip\":\"127.0.0.1\",\"host_port\":18203,\"sandbox_port\":9000,\"protocol\":\"tcp4\"}]"
    )]
    [InlineData(
        "[{\"host_ip\":\"0.0.0.0\",\"host_port\":18203,\"sandbox_port\":8203,\"protocol\":\"tcp4\"}]"
    )]
    [InlineData(
        "[{\"host_ip\":\"127.0.0.1\",\"host_port\":18203,\"sandbox_port\":8203,\"protocol\":\"unknown\"}]"
    )]
    public async Task UnknownOrConflictingInventoryCannotTriggerPublication(string json)
    {
        // Arrange
        inventory = json;

        // Act
        var action = () =>
            client.EnsureApplicationPortsAsync(
                "actual",
                [new(8203, 18203)],
                CancellationToken.None
            );

        // Assert
        await action.Should().ThrowAsync<AboxException>();
        calls
            .Should()
            .NotContain(call =>
                call.Arguments.Contains("--publish") || call.Arguments.Contains("--unpublish")
            );
    }

    [Theory]
    [InlineData(0, 18203)]
    [InlineData(65536, 18203)]
    [InlineData(8203, 0)]
    [InlineData(8203, 65536)]
    public async Task InvalidMappingsAreRejectedBeforeAnyRuntimeCall(int sandboxPort, int hostPort)
    {
        // Arrange
        var mappings = new ApplicationPortMapping[] { new(sandboxPort, hostPort) };

        // Act
        var action = () =>
            client.EnsureApplicationPortsAsync("actual", mappings, CancellationToken.None);

        // Assert
        await action.Should().ThrowAsync<AboxException>();
        calls.Should().BeEmpty();
    }

    [Fact]
    public async Task OccupiedHostPortsPreserveRuntimeDiagnosticsWithoutAttemptingRemoval()
    {
        // Arrange
        publishFailure = new ProcessResult(
            1,
            "",
            "host 127.0.0.1:18203 is already in use; choose a different host port"
        );

        // Act
        var action = () =>
            client.EnsureApplicationPortsAsync(
                "actual",
                [new(8203, 18203)],
                CancellationToken.None
            );

        // Assert
        await action.Should().ThrowAsync<AboxException>().WithMessage("*18203*already in use*");
        calls.Should().NotContain(call => call.Arguments.Contains("--unpublish"));
    }

    [Fact]
    public async Task StartsBeforeInventoryAndReusesMappingsWithoutRemovingUnrelatedOnes()
    {
        // Arrange
        inventory = """
            [{"host_ip":"127.0.0.1","host_port":19000,"sandbox_port":9000,"protocol":"tcp4"}]
            """;

        // Act
        await client.EnsureApplicationPortsAsync(
            "actual-legacy-name",
            [new(8203, 18203), new(8203, 18203)],
            CancellationToken.None
        );
        await client.EnsureApplicationPortsAsync(
            "actual-legacy-name",
            [new(8203, 18203)],
            CancellationToken.None
        );

        // Assert
        calls[0].Arguments.Should().Equal("exec", "actual-legacy-name", "true");
        calls[0].AttachConsole.Should().BeFalse();
        calls[0].StandardInput.Should().Be("");
        calls
            .Where(call => call.Arguments.Contains("--publish"))
            .Should()
            .ContainSingle()
            .Which.Arguments.Should()
            .Equal(
                "ports",
                "actual-legacy-name",
                "--publish",
                "127.0.0.1:18203:8203/tcp4",
                "--json"
            );
        calls
            .Should()
            .OnlyContain(call => call.FileName == "sbx" && !call.Arguments.Contains("--unpublish"));
    }

    public void Dispose() => root.Dispose();
}
