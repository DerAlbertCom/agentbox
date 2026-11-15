using System.Text.Json;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class SbxAllowedSourcesTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly AboxPaths _paths;
    private const string Prefix = "rwest.jfrog.io/mtiae-docker-release/";
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();
    private readonly SbxClient _client;
    private readonly List<ProcessRequest> _calls = [];
    private string[] _sources = ["docker.io/", "dhi.io/", "ghcr.io/approved/", "docker.io/"];

    public SbxAllowedSourcesTests()
    {
        _paths = AboxPaths.For(_root.Path, Path.Combine(_root.Path, "state"));
        _client = new SbxClient(_runner, _paths);
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessRequest>();
                _calls.Add(request);
                if (request.Arguments[1] == "set")
                {
                    _sources = JsonSerializer.Deserialize<string[]>(request.Arguments[3])!;
                    return new ProcessResult(0, "Setting updated.", "");
                }

                return new ProcessResult(
                    0,
                    JsonSerializer.Serialize(
                        new
                        {
                            key = "kit.allowedSources",
                            type = "json",
                            source = "default",
                            value = _sources,
                        }
                    ),
                    ""
                );
            });
    }

    public void Dispose() => _root.Dispose();

    [Fact]
    public async Task SettingsLockUsesTheInjectedStateDirectory()
    {
        // Arrange: the fixture injects an isolated state directory.

        // Act
        await _client.EnsureKitSourceAllowedAsync(Prefix, CancellationToken.None);

        // Assert
        File.Exists(Path.Combine(_paths.StateDirectory, "locks", "sbx-kit-allowedSources.lock"))
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task RemoteRepositoryIsAppendedWithoutDroppingOrChangingExistingEntries()
    {
        // Arrange: the fixture includes ordered, duplicate existing sources.

        // Act
        await _client.EnsureKitSourceAllowedAsync(Prefix, CancellationToken.None);

        // Assert
        _calls[0].FileName.Should().Be("sbx");
        _calls[0].Arguments.Should().Equal("settings", "get", "kit.allowedSources", "--json");
        var write = _calls.Should().ContainSingle(call => call.Arguments[1] == "set").Which;
        write
            .Arguments.Should()
            .Equal(
                "settings",
                "set",
                "kit.allowedSources",
                "[\"docker.io/\",\"dhi.io/\",\"ghcr.io/approved/\",\"docker.io/\",\"rwest.jfrog.io/mtiae-docker-release/\"]"
            );
        write.AttachConsole.Should().BeFalse();
        write.StandardInput.Should().BeNull();
        _sources.Should().Equal("docker.io/", "dhi.io/", "ghcr.io/approved/", "docker.io/", Prefix);
    }

    [Theory]
    [InlineData("rwest.jfrog.io/mtiae-docker-release/")]
    [InlineData("rwest.jfrog.io/mtiae-docker-release")]
    [InlineData("rwest.jfrog.io")]
    [InlineData("rwest.jfrog.io/")]
    [InlineData("*")]
    public async Task ExistingCoverageDoesNotWriteOrNarrowTheAllowlist(string existing)
    {
        // Arrange
        _sources = ["docker.io/", existing, "dhi.io/"];

        // Act
        await _client.EnsureKitSourceAllowedAsync(Prefix, CancellationToken.None);

        // Assert
        _calls.Should().ContainSingle().Which.Arguments[1].Should().Be("get");
        _sources.Should().Equal("docker.io/", existing, "dhi.io/");
    }

    [Fact]
    public async Task ASecondCallDoesNotWriteAgain()
    {
        // Arrange
        await _client.EnsureKitSourceAllowedAsync(Prefix, CancellationToken.None);
        _calls.Clear();

        // Act
        await _client.EnsureKitSourceAllowedAsync(Prefix, CancellationToken.None);

        // Assert
        _calls.Should().ContainSingle().Which.Arguments[1].Should().Be("get");
    }

    [Theory]
    [InlineData("rwest.jfrog.io/mtiae-docker-release/specific-team/")]
    [InlineData("rwest.jfrog.io/mtiae-docker-release-other/")]
    [InlineData("rwest.jfrog.io/mtiae-docker-dev/")]
    [InlineData("rwest.jfrog.io/mtiae")]
    [InlineData("rwest.jfrog.io/mtiae-docker")]
    public async Task NarrowerOrDifferentRepositoriesArePreservedButDoNotCoverTheRequestedRepository(
        string existing
    )
    {
        // Arrange
        _sources = [existing];

        // Act
        await _client.EnsureKitSourceAllowedAsync(Prefix, CancellationToken.None);

        // Assert
        _sources.Should().Equal(existing, Prefix);
        _calls.Should().ContainSingle(call => call.Arguments[1] == "set");
    }

    [Fact]
    public async Task AnEmptyAllowlistGetsOnlyTheConfiguredRepository()
    {
        // Arrange
        _sources = [];

        // Act
        await _client.EnsureKitSourceAllowedAsync(
            "rwest.jfrog.io/team-docker-dev/",
            CancellationToken.None
        );

        // Assert
        _sources.Should().Equal("rwest.jfrog.io/team-docker-dev/");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"key\":\"kit.allowedSources\",\"type\":\"json\",\"value\":[]}")]
    [InlineData("{\"key\":\"kit.allowedSources\",\"type\":\"json\",\"source\":42,\"value\":[]}")]
    [InlineData(
        "{\"key\":\"kit.allowedSources\",\"type\":\"json\",\"source\":\"default\",\"value\":null}"
    )]
    [InlineData(
        "{\"key\":\"kit.allowedSources\",\"type\":\"json\",\"source\":\"default\",\"value\":\"*\"}"
    )]
    [InlineData(
        "{\"key\":\"kit.allowedSources\",\"type\":\"json\",\"source\":\"default\",\"value\":[\"docker.io/\",null]}"
    )]
    [InlineData(
        "{\"key\":\"kit.allowedSources\",\"type\":\"json\",\"source\":\"default\",\"value\":[42]}"
    )]
    [InlineData(
        "{\"key\":\"kit.allowedSources\",\"type\":\"json\",\"source\":\"default\",\"value\":[\"\"]}"
    )]
    [InlineData(
        "{\"key\":\"other.setting\",\"type\":\"json\",\"source\":\"default\",\"value\":[]}"
    )]
    [InlineData(
        "{\"key\":\"kit.allowedSources\",\"type\":\"string\",\"source\":\"default\",\"value\":[]}"
    )]
    public async Task InvalidSettingsFailWithoutAnyWrite(string json)
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, json, ""));

        // Act
        var action = () => _client.EnsureKitSourceAllowedAsync(Prefix, CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*kit.allowedSources*");
        await _runner
            .DidNotReceive()
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments[1] == "set"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task FailedSettingsReadsAreReportedWithoutWriting()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(1, "", "administrator policy denies settings access"));

        // Act
        var action = () => _client.EnsureKitSourceAllowedAsync(Prefix, CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*sbx settings get kit.allowedSources*administrator policy*"
        );
        await _runner.Received(1).RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdministratorDenialsAreReportedWithoutChangingOtherPolicies()
    {
        // Arrange
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments[1] == "set"),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ProcessResult(1, "", "rejected by administrator constraint"));

        // Act
        var action = () => _client.EnsureKitSourceAllowedAsync(Prefix, CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*kit.allowedSources*administrator constraint*no sandbox was replaced*"
        );
        _sources.Should().Equal("docker.io/", "dhi.io/", "ghcr.io/approved/", "docker.io/");
        _runner.ReceivedCalls().Should().HaveCount(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnvironmentControlledTrustIsNeverOverridden(bool alreadyAllowed)
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ProcessResult(
                    0,
                    JsonSerializer.Serialize(
                        new
                        {
                            key = "kit.allowedSources",
                            type = "json",
                            source = "envvar",
                            value = alreadyAllowed ? new[] { Prefix } : new[] { "docker.io/" },
                        }
                    ),
                    ""
                )
            );

        // Act
        var action = () => _client.EnsureKitSourceAllowedAsync(Prefix, CancellationToken.None);

        // Assert
        if (alreadyAllowed)
        {
            await action();
        }
        else
        {
            (await action.Should().ThrowAsync<AboxException>()).WithMessage(
                "*environment variable*administrator*no sandbox was replaced*"
            );
        }
        await _runner.Received(1).RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASuccessfulSetThatDoesNotTakeEffectIsNotSilentlyAccepted()
    {
        // Arrange
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments[1] == "set"),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ProcessResult(0, "Override saved.", ""));

        // Act
        var action = () => _client.EnsureKitSourceAllowedAsync(Prefix, CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*evaluated settings*administrator policy*no sandbox was replaced*"
        );
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments[1] == "set"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task DelayedEvaluatedSettingsAreCheckedBeforeReturning()
    {
        // Arrange
        var reads = 0;
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments[1] == "get"),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => new ProcessResult(
                0,
                JsonSerializer.Serialize(
                    new
                    {
                        key = "kit.allowedSources",
                        type = "json",
                        source = "override",
                        value = ++reads < 3
                            ? new[] { "docker.io/" }
                            : new[] { "docker.io/", Prefix },
                    }
                ),
                ""
            ));

        // Act
        await _client.EnsureKitSourceAllowedAsync(Prefix, CancellationToken.None);

        // Assert
        reads.Should().Be(3);
    }

    [Theory]
    [InlineData("rwest.jfrog.io/mtiae-docker-release")]
    [InlineData("rwest.jfrog.io/")]
    [InlineData("rwest.jfrog.io")]
    [InlineData("*")]
    public async Task PostWriteVerificationUsesTheSameSegmentCoverageRules(string effectivePrefix)
    {
        // Arrange
        var original = _sources.ToArray();
        var reads = 0;
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments[1] == "get"),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => new ProcessResult(
                0,
                JsonSerializer.Serialize(
                    new
                    {
                        key = "kit.allowedSources",
                        type = "json",
                        source = "override",
                        value = ++reads == 1
                            ? original
                            : original.Append(effectivePrefix).ToArray(),
                    }
                ),
                ""
            ));

        // Act
        await _client.EnsureKitSourceAllowedAsync(Prefix, CancellationToken.None);

        // Assert
        reads.Should().Be(2);
    }

    [Theory]
    [InlineData("rwest.jfrog.io/mtiae")]
    [InlineData("rwest.jfrog.io/mtiae-docker")]
    public async Task PostWriteVerificationDoesNotAcceptPartialRepositorySegments(
        string effectivePrefix
    )
    {
        // Arrange
        var original = _sources.ToArray();
        var reads = 0;
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments[1] == "get"),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => new ProcessResult(
                0,
                JsonSerializer.Serialize(
                    new
                    {
                        key = "kit.allowedSources",
                        type = "json",
                        source = "override",
                        value = ++reads == 1
                            ? original
                            : original.Append(effectivePrefix).ToArray(),
                    }
                ),
                ""
            ));

        // Act
        var action = () => _client.EnsureKitSourceAllowedAsync(Prefix, CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*evaluated settings*no sandbox was replaced*"
        );
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments[1] == "set"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PostWriteCoverageCannotHideMissingOriginalEntries()
    {
        // Arrange
        var original = _sources.ToArray();
        var reads = 0;
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments[1] == "get"),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => new ProcessResult(
                0,
                JsonSerializer.Serialize(
                    new
                    {
                        key = "kit.allowedSources",
                        type = "json",
                        source = "override",
                        value = ++reads == 1 ? original : new[] { Prefix },
                    }
                ),
                ""
            ));

        // Act
        var action = () => _client.EnsureKitSourceAllowedAsync(Prefix, CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*did not preserve the allowlist*no sandbox was replaced*"
        );
    }

    [Fact]
    public async Task ConcurrentAboxClientsMergeRatherThanOverwriteEachOthersAdditions()
    {
        // Arrange
        var sources = new[] { "docker.io/", "dhi.io/" };
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var request = call.Arg<ProcessRequest>();
                if (request.Arguments[1] == "set")
                {
                    sources = JsonSerializer.Deserialize<string[]>(request.Arguments[3])!;
                    return new ProcessResult(0, "", "");
                }

                var snapshot = JsonSerializer.Serialize(
                    new
                    {
                        key = "kit.allowedSources",
                        type = "json",
                        source = "override",
                        value = sources,
                    }
                );
                await Task.Delay(100);
                return new ProcessResult(0, snapshot, "");
            });
        var other = new SbxClient(_runner, _paths);

        // Act
        await Task.WhenAll(
            _client.EnsureKitSourceAllowedAsync(Prefix, CancellationToken.None),
            other.EnsureKitSourceAllowedAsync(
                "rwest.jfrog.io/team-docker-dev/",
                CancellationToken.None
            )
        );

        // Assert
        sources
            .Should()
            .BeEquivalentTo("docker.io/", "dhi.io/", Prefix, "rwest.jfrog.io/team-docker-dev/");
    }

    [Fact]
    public async Task WaitingForAnotherClientsSettingsLockCanBeCancelled()
    {
        // Arrange
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                Interlocked.Increment(ref reads);
                entered.TrySetResult();
                await release.Task;
                return new ProcessResult(
                    0,
                    JsonSerializer.Serialize(
                        new
                        {
                            key = "kit.allowedSources",
                            type = "json",
                            source = "override",
                            value = new[] { Prefix },
                        }
                    ),
                    ""
                );
            });
        var first = _client.EnsureKitSourceAllowedAsync(Prefix, CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var other = new SbxClient(_runner, _paths);

            // Act
            var waiting = () => other.EnsureKitSourceAllowedAsync(Prefix, cancellation.Token);

            // Assert
            await waiting.Should().ThrowAsync<OperationCanceledException>();
            reads.Should().Be(1);
        }
        finally
        {
            release.TrySetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledUpdatesReleaseTheLockForTheNextClient(bool cancelled)
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (cancelled)
                {
                    cancellation.Cancel();
                    call.Arg<CancellationToken>().ThrowIfCancellationRequested();
                }

                return new ProcessResult(1, "", "settings unavailable");
            });
        // Act
        var first = () => _client.EnsureKitSourceAllowedAsync(Prefix, cancellation.Token);

        // Assert
        if (cancelled)
        {
            await first.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            await first.Should().ThrowAsync<AboxException>();
        }
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ProcessResult(
                    0,
                    JsonSerializer.Serialize(
                        new
                        {
                            key = "kit.allowedSources",
                            type = "json",
                            source = "override",
                            value = new[] { Prefix },
                        }
                    ),
                    ""
                )
            );
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        var next = () =>
            new SbxClient(_runner, _paths).EnsureKitSourceAllowedAsync(Prefix, timeout.Token);

        // Assert
        await next.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("*")]
    [InlineData("rwest.jfrog.io/")]
    [InlineData("rwest.jfrog.io/repo/*")]
    public async Task BroadOrWildcardTrustCannotBeIntroduced(string prefix)
    {
        // Arrange: the theory supplies an overbroad trust prefix.

        // Act
        var action = () => _client.EnsureKitSourceAllowedAsync(prefix, CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*repository-scoped prefix*"
        );
        _runner.ReceivedCalls().Should().BeEmpty();
    }
}
