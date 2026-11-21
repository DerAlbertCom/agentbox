using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

public sealed class OciRegistryTests
{
    private const string Reference = "registry.example/team/kit:latest";
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();

    public static TheoryData<string, bool, string[]> FailedOperations =>
        new()
        {
            { "probe", false, ["buildx", "version"] },
            {
                "push",
                true,
                [
                    "buildx",
                    "build",
                    "--builder",
                    "publisher",
                    "--file",
                    "source/workload.yaml",
                    "--tag",
                    Reference,
                    "--metadata-file",
                    "metadata.json",
                    "--provenance=mode=min",
                    "--push",
                    "source",
                ]
            },
            {
                "tag",
                true,
                [
                    "buildx",
                    "imagetools",
                    "--builder",
                    "publisher",
                    "create",
                    "--prefer-index=false",
                    "--tag",
                    Reference,
                    "candidate",
                ]
            },
        };

    [Theory]
    [MemberData(nameof(FailedOperations))]
    public async Task FailedDockerInvocationsReportTheCallersFailureWithCapturedOutput(
        string operation,
        bool attachConsole,
        string[] expected
    )
    {
        // Arrange
        var failure = new ProcessResult(17, "output", "error");
        _runner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>()).Returns(failure);

        // Act
        var action = () => InvokeAsync(operation);

        // Assert
        (await action.Should().ThrowAsync<AboxException>())
            .Which.Message.Should()
            .Be("Caller failure: 17." + Environment.NewLine + "erroroutput");
        await _runner
            .Received()
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "docker"
                    && request.AttachConsole == attachConsole
                    && request.Arguments.SequenceEqual(expected)
                ),
                CancellationToken.None
            );
    }

    private Task InvokeAsync(string operation)
    {
        var registry = new OciRegistry(_runner);
        var failure = new OciFailure(exitCode => $"Caller failure: {exitCode}.");
        return operation switch
        {
            "push" => registry.BuildAndPushAsync(
                new OciPushBuild(
                    "source",
                    "source/workload.yaml",
                    Reference,
                    "metadata.json",
                    "publisher",
                    false
                ),
                failure,
                CancellationToken.None
            ),
            "tag" => registry.TagAsync(
                new OciTag("candidate", Reference, "publisher"),
                failure,
                CancellationToken.None
            ),
            _ => registry.RequireBuildxAsync(failure, CancellationToken.None),
        };
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
            new OciImage(Reference, builder),
            raw,
            new OciFailure(exitCode => $"Inspection failed: {exitCode}."),
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
