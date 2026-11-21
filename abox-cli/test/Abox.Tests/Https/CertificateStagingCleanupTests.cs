using System.Security.Cryptography;
using System.Text.Json;
using Abox.Https;
using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

public sealed class CertificateStagingCleanupTests
{
    [Theory]
    [InlineData("export")]
    [InlineData("cancel")]
    public async Task CleanupFailureRetainsTheExportFailureOrCancellationAndSafeRecovery(
        string outcome
    )
    {
        // Arrange
        using var fixture = new UnixCleanupFailureFixture(outcome);
        var export = () =>
            fixture.Exporter.ExportAsync("synthetic-password", fixture.Cancellation.Token);

        // Act
        var failure =
            outcome == "cancel"
                ? (Exception)(await export.Should().ThrowAsync<OperationCanceledException>()).Which
                : (await export.Should().ThrowAsync<AboxException>()).Which;

        // Assert
        AssertPrimaryFailure(failure, fixture.Cancellation.Token);
        fixture
            .Console.Errors.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("cleanup failed")
            .And.Contain(JsonSerializer.Serialize(fixture.StagingPath))
            .And.Contain("before retrying")
            .And.NotContain("private child detail");
        Directory.Exists(fixture.StagingPath).Should().BeTrue();
        File.ReadAllText(fixture.MarkerPath).Should().Be("unowned sibling");
    }

    private static void AssertPrimaryFailure(Exception failure, CancellationToken token)
    {
        if (failure is OperationCanceledException cancellation)
            cancellation.CancellationToken.Should().Be(token);
        else
            failure.Message.Should().Contain("Cannot export an exact existing trusted");
        failure
            .ToString()
            .Should()
            .NotContain("private child detail")
            .And.NotContain("synthetic-password");
        failure.InnerException.Should().BeNull();
    }

    private sealed class UnixCleanupFailureFixture : IDisposable
    {
        private const string EligibleMetadata = """
            [{"Thumbprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","Subject":"CN=localhost","Version":6,
              "ValidityNotBefore":"2020-01-01T00:00:00Z","ValidityNotAfter":"2090-01-01T00:00:00Z",
              "IsHttpsDevelopmentCertificate":true,"IsExportable":true,"TrustLevel":"Full",
              "X509SubjectAlternativeNameExtension":["localhost"]}]
            """;
        private readonly CertificateTestDirectory root;
        private readonly UnixFileMode originalMode;

        public UnixCleanupFailureFixture(string outcome)
        {
            if (OperatingSystem.IsWindows() || Environment.UserName == "root")
                throw new InvalidOperationException(
                    Xunit.v3.DynamicSkipToken.Value
                        + "Cleanup failure requires Unix directory permissions enforced for a non-root user."
                );
            root = new CertificateTestDirectory();
            originalMode = File.GetUnixFileMode(root.Path);
            MarkerPath = Path.Combine(root.Path, "keep.txt");
            File.WriteAllText(MarkerPath, "unowned sibling");
            var runner = Substitute.For<IProcessRunner>();
            runner
                .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
                .Returns(call => Run(call.Arg<ProcessRequest>(), outcome));
            Exporter = new HostDevelopmentCertificate(
                runner,
                Console,
                root.Path,
                [new("synthetic-tool", 6)]
            );
        }

        public TestConsole Console { get; } = new();
        public CancellationTokenSource Cancellation { get; } = new();
        public HostDevelopmentCertificate Exporter { get; }
        public string? StagingPath { get; private set; }
        public string MarkerPath { get; }

        private Task<ProcessResult> Run(ProcessRequest request, string outcome)
        {
            if (!request.Arguments.Contains("--export-path"))
                return Task.FromResult(
                    new ProcessResult(0, EligibleMetadata, "private child detail")
                );
            var path = request.Arguments[
                Array.IndexOf(request.Arguments.ToArray(), "--export-path") + 1
            ];
            StagingPath = Path.GetDirectoryName(path)!;
            File.WriteAllText(path, "synthetic partial export, not a certificate");
            if (OperatingSystem.IsWindows())
                throw new InvalidOperationException("Unix-only fixture");
            // Only the fixture's parent loses write permission, so removal fails
            // without touching any host location or preventing fixture recovery.
            File.SetUnixFileMode(root.Path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            if (outcome == "cancel")
            {
                Cancellation.Cancel();
                return Task.FromException<ProcessResult>(
                    new OperationCanceledException("private child detail", Cancellation.Token)
                );
            }
            return Task.FromException<ProcessResult>(
                new CryptographicException("private child detail")
            );
        }

        public void Dispose()
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(root.Path, originalMode);
            root.Dispose();
            Cancellation.Dispose();
        }
    }
}
