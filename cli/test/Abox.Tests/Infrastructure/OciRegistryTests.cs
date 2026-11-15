using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

public sealed class OciRegistryTests
{
    private const string Reference = "registry.example/team/kit:latest";
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();

    [Fact]
    public async Task FailedDockerInvocationsReturnTheUnmodifiedResult()
    {
        // Arrange
        var failure = new ProcessResult(17, "output", "error");
        _runner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>()).Returns(failure);

        // Act
        var result = await new OciRegistry(_runner).RunAsync(
            ["buildx", "version"],
            true,
            CancellationToken.None
        );

        // Assert
        result.Should().BeSameAs(failure);
        await _runner
            .Received()
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "docker"
                    && request.AttachConsole
                    && request.Arguments.SequenceEqual(new[] { "buildx", "version" })
                ),
                CancellationToken.None
            );
    }

    [Theory]
    [InlineData(true, null, new[] { "buildx", "imagetools", "inspect", "--raw", Reference })]
    [InlineData(
        false,
        "publisher",
        new[]
        {
            "buildx",
            "imagetools",
            "--builder",
            "publisher",
            "inspect",
            "--format",
            "{{json .Manifest}}",
            Reference,
        }
    )]
    public async Task InspectionPreservesResultsAndRoutesRawOrIdentityRequests(
        bool raw,
        string? builder,
        string[] arguments
    )
    {
        // Arrange
        var response = new ProcessResult(0, "not parsed by infrastructure", "");
        _runner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>()).Returns(response);

        // Act
        var result = await new OciRegistry(_runner).InspectAsync(
            Reference,
            raw,
            builder,
            CancellationToken.None
        );

        // Assert
        result.Should().BeSameAs(response);
        await _runner
            .Received()
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "docker"
                    && request.Arguments.SequenceEqual(arguments)
                    && !request.AttachConsole
                ),
                CancellationToken.None
            );
    }
}
