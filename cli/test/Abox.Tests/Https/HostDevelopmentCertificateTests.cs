using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Abox.Https;
using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

public sealed class HostDevelopmentCertificateTests
{
    [Fact]
    public async Task PrivateStagingFailureRetainsSpecificSafeRecoveryGuidanceBeforeExport()
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ProcessResult(
                    0,
                    """
                    [{"Thumbprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","Subject":"CN=localhost","Version":6,
                      "ValidityNotBefore":"2020-01-01T00:00:00Z","ValidityNotAfter":"2090-01-01T00:00:00Z",
                      "IsHttpsDevelopmentCertificate":true,"IsExportable":true,"TrustLevel":"Full",
                      "X509SubjectAlternativeNameExtension":["localhost"]}]
                    """,
                    "private child diagnostic"
                )
            );
        var exporter = new HostDevelopmentCertificate(
            runner,
            new TestConsole(),
            Path.Combine(root.Path, "missing-private-parent"),
            [new("tool", 6)]
        );

        // Act
        var action = () => exporter.ExportAsync(new string('a', 64), CancellationToken.None);

        // Assert
        var failure = (await action.Should().ThrowAsync<AboxException>()).Which;
        failure
            .Message.Should()
            .Contain("private certificate staging")
            .And.Contain("temporary directory")
            .And.NotContain("private child diagnostic");
        await runner
            .DidNotReceive()
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("--export-path")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task InstalledSdkDiscoveryReadsTheToolFormatWithoutRunningItsExportOrLoadingItsAssembly()
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var tool = InstallSyntheticTool(root.Path, "10.0.100");
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessRequest>();
                return new ProcessResult(
                    0,
                    request.Arguments.Contains("--list-sdks")
                        ? $"10.0.100 [{Path.Combine(root.Path, "sdk")}]"
                        : "[]",
                    "secret"
                );
            });
        var exporter = new HostDevelopmentCertificate(runner, new TestConsole(), root.Path, null);

        // Act
        var action = () => exporter.ExportAsync(new string('a', 64), CancellationToken.None);

        // Assert
        await action.Should().ThrowAsync<AboxException>();
        await runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.SequenceEqual(
                        new[] { "exec", tool, "https", "--check-trust-machine-readable" }
                    )
                ),
                Arg.Any<CancellationToken>()
            );
        await runner
            .DidNotReceive()
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.Contains("--export-path")
                    || request.Arguments.Contains("dev-certs")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task InstalledToolFormatRatherThanSdkMajorAuthorizesExactExport()
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        using var identity = new SyntheticDevelopmentCertificate();
        var tool = InstallSyntheticTool(root.Path, "99.0.100-preview.1");
        const string password = "generated-test-password";
        var bytes = identity.Export(password);
        var calls = new List<ProcessRequest>();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var child = call.Arg<ProcessRequest>();
                calls.Add(child);
                if (child.Arguments.Contains("--list-sdks"))
                    return new ProcessResult(
                        0,
                        $"malformed listing\n99.0.100-preview.1 [{Path.Combine(root.Path, "sdk")}]",
                        "secret"
                    );
                if (child.Arguments.Contains("--export-path"))
                {
                    AssertPrivateExportDestination(child);
                    File.WriteAllBytes(ExportPath(child), bytes);
                }
                return new ProcessResult(0, identity.Metadata().ToJsonString(), "secret");
            });
        var exporter = new HostDevelopmentCertificate(runner, new TestConsole(), root.Path, null);

        // Act
        using var exported = await exporter.ExportAsync(password, CancellationToken.None);

        // Assert
        exported.Pfx.ToArray().Should().Equal(bytes);
        calls.Should().HaveCount(4);
        calls.Skip(1).Should().OnlyContain(call => call.Arguments[1] == tool);
        AssertSafeToolCalls(calls);
        Directory.GetDirectories(root.Path, "abox-certificate-*").Should().BeEmpty();
    }

    [Theory]
    [InlineData("listing-failed")]
    [InlineData("malformed-listing")]
    [InlineData("missing-tool")]
    [InlineData("invalid-pe")]
    [InlineData("no-format-constant")]
    public async Task FailedOrUnknownInstalledToolDiscoveryCannotReachCertificateOperations(
        string condition
    )
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var tool = InstallSyntheticTool(root.Path, "10.0.100");
        if (condition == "missing-tool")
            File.Delete(tool);
        if (condition == "invalid-pe")
            File.WriteAllText(tool, "not an assembly");
        if (condition == "no-format-constant")
            File.Copy(typeof(HostDevelopmentCertificate).Assembly.Location, tool, overwrite: true);
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ProcessResult(
                    condition == "listing-failed" ? 9 : 0,
                    condition == "malformed-listing"
                        ? "private malformed output"
                        : $"10.0.100 [{Path.Combine(root.Path, "sdk")}]",
                    "private diagnostic"
                )
            );
        var exporter = new HostDevelopmentCertificate(runner, new TestConsole(), root.Path, null);

        // Act
        var action = () => exporter.ExportAsync("generated-test-password", CancellationToken.None);

        // Assert
        var error = (await action.Should().ThrowAsync<AboxException>()).Which;
        error
            .ToString()
            .Should()
            .NotContain("private diagnostic")
            .And.NotContain("private malformed output");
        runner.ReceivedCalls().Should().ContainSingle();
        await runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.SequenceEqual(new[] { "--list-sdks" })
                ),
                Arg.Any<CancellationToken>()
            );
        Directory.GetDirectories(root.Path, "abox-certificate-*").Should().BeEmpty();
    }

    private static string InstallSyntheticTool(string root, string version)
    {
        var path = Path.Combine(
            root,
            "sdk",
            version,
            "DotnetTools",
            "dotnet-dev-certs",
            "tools",
            "net10.0",
            "any",
            "dotnet-dev-certs.dll"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(typeof(HostDevelopmentCertificateTests).Assembly.Location, path);
        return path;
    }

    [Theory]
    [InlineData("old-format")]
    [InlineData("untrusted")]
    [InlineData("partial-trust")]
    [InlineData("not-exportable")]
    [InlineData("not-development")]
    [InlineData("wrong-subject")]
    [InlineData("no-localhost")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("multiple")]
    [InlineData("invalid-key-state")]
    public async Task IneligibleExistingIdentityNeverInvokesThePotentiallyCreatingExport(
        string condition
    )
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var metadata = System
            .Text.Json.Nodes.JsonNode.Parse(
                """
                [{"Thumbprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","Subject":"CN=localhost","Version":6,
                  "ValidityNotBefore":"2020-01-01T00:00:00Z","ValidityNotAfter":"2090-01-01T00:00:00Z",
                  "IsHttpsDevelopmentCertificate":true,"IsExportable":true,"TrustLevel":"Full",
                  "X509SubjectAlternativeNameExtension":["localhost"]}]
                """
            )!
            .AsArray();
        var value = metadata[0]!;
        switch (condition)
        {
            case "old-format":
                value["Version"] = 4;
                break;
            case "untrusted":
                value["TrustLevel"] = "None";
                break;
            case "partial-trust":
                value["TrustLevel"] = "Partial";
                break;
            case "invalid-key-state":
                value["TrustLevel"] = "Invalid";
                break;
            case "not-exportable":
                value["IsExportable"] = false;
                break;
            case "not-development":
                value["IsHttpsDevelopmentCertificate"] = false;
                break;
            case "wrong-subject":
                value["Subject"] = "CN=other";
                break;
            case "no-localhost":
                value["X509SubjectAlternativeNameExtension"] = new System.Text.Json.Nodes.JsonArray(
                    "other"
                );
                break;
            case "expired":
                value["ValidityNotAfter"] = "2020-01-02T00:00:00Z";
                break;
            case "future":
                value["ValidityNotBefore"] = "2080-01-01T00:00:00Z";
                break;
            case "multiple":
                metadata.Add(value.DeepClone());
                break;
        }
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, metadata.ToJsonString(), "secret"));
        var exporter = new HostDevelopmentCertificate(
            runner,
            new TestConsole(),
            root.Path,
            [new("tool", 6)]
        );

        // Act
        var action = () => exporter.ExportAsync(new string('a', 64), CancellationToken.None);

        // Assert
        await action.Should().ThrowAsync<AboxException>();
        await runner
            .DidNotReceive()
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("--export-path")),
                Arg.Any<CancellationToken>()
            );
        Directory.GetFileSystemEntries(root.Path).Should().BeEmpty();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("")]
    [InlineData("not-json private diagnostic")]
    [InlineData("{}")]
    [InlineData("[{}]")]
    [InlineData("[{\"Thumbprint\":true}]")]
    [InlineData("[{\"Thumbprint\":\"A\",\"Thumbprint\":\"B\"}]")]
    public async Task MissingOrMalformedMetadataCannotReachTheCreatingCliExportPath(string metadata)
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var runner = Substitute.For<IProcessRunner>();
        var calls = new List<ProcessRequest>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                calls.Add(call.Arg<ProcessRequest>());
                return new ProcessResult(0, metadata, "private diagnostic");
            });
        var exporter = new HostDevelopmentCertificate(
            runner,
            new TestConsole(),
            root.Path,
            [new("synthetic-tool", 6)]
        );

        // Act
        var action = () => exporter.ExportAsync("generated-test-password", CancellationToken.None);

        // Assert
        var error = (await action.Should().ThrowAsync<AboxException>()).Which;
        error.ToString().Should().NotContain("private diagnostic");
        calls.Should().NotContain(call => call.Arguments.Contains("--export-path"));
        Directory.GetFileSystemEntries(root.Path).Should().BeEmpty();
    }

    [Theory]
    [InlineData(6)]
    [InlineData(4)]
    public async Task ExactTrustedExportRetainsBytesAndCleansPrivateStagingBeforeReturning(
        int format
    )
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        using var identity = new SyntheticDevelopmentCertificate(format);
        const string password = "generated-test-password";
        var bytes = identity.Export(password);
        var metadata = identity.Metadata().ToJsonString();
        var runner = Substitute.For<IProcessRunner>();
        var calls = new List<ProcessRequest>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var child = call.Arg<ProcessRequest>();
                calls.Add(child);
                if (child.Arguments.Contains("--export-path"))
                {
                    AssertPrivateExportDestination(child);
                    File.WriteAllBytes(
                        child.Arguments[
                            Array.IndexOf(child.Arguments.ToArray(), "--export-path") + 1
                        ],
                        bytes
                    );
                    return new ProcessResult(
                        0,
                        "private export diagnostic",
                        "private export error"
                    );
                }
                return new ProcessResult(0, metadata, "private metadata error");
            });
        var exporter = new HostDevelopmentCertificate(
            runner,
            new TestConsole(),
            root.Path,
            [new("newer-installed-tool", 6), new("older-installed-tool", 4)]
        );

        // Act
        using var exported = await exporter.ExportAsync(password, CancellationToken.None);

        // Assert
        exported.Pfx.ToArray().Should().Equal(bytes);
        exported.Password.Should().Be(password);
        exported.ToString().Should().NotContain(password);
        calls.Should().HaveCount(format == 6 ? 3 : 4);
        calls
            .Single(call => call.Arguments.Contains("--export-path"))
            .Arguments.Should()
            .Contain(format == 6 ? "newer-installed-tool" : "older-installed-tool");
        AssertSafeToolCalls(calls);
        Directory.GetFileSystemEntries(root.Path).Should().BeEmpty();
        var ownedBytes = exported.Pfx;
        exported.Dispose();
        ownedBytes.ToArray().Should().OnlyContain(value => value == 0);
        bytes.Should().Contain(value => value != 0);
    }

    private static void AssertPrivateExportDestination(ProcessRequest child)
    {
        child.OutputPolicy.Should().Be(ProcessOutputPolicy.Sensitive);
        var path = child.Arguments[Array.IndexOf(child.Arguments.ToArray(), "--export-path") + 1];
        var directory = Path.GetDirectoryName(path)!;
        Directory.GetFileSystemEntries(directory).Should().BeEmpty();
        foreach (var name in new[] { "TMPDIR", "TMP", "TEMP" })
            child.Environment![name].Should().Be(directory);
        File.GetAttributes(directory).Should().NotHaveFlag(FileAttributes.ReparsePoint);
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(directory)
                .Should()
                .Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return;
        }
        AssertPrivateWindowsDirectory(directory);
    }

    private static void AssertSafeToolCalls(IReadOnlyList<ProcessRequest> calls)
    {
        calls
            .Where(call => !call.Arguments.Contains("--export-path"))
            .Should()
            .OnlyContain(call => call.OutputPolicy == ProcessOutputPolicy.CapturedSensitive);
        calls
            .Should()
            .OnlyContain(call =>
                call.Environment!["DOTNET_GENERATE_ASPNET_CERTIFICATE"] == "false"
            );
        calls.Should().OnlyContain(call => call.StandardInput == "");
    }

    private static string ExportPath(ProcessRequest child) =>
        child.Arguments[Array.IndexOf(child.Arguments.ToArray(), "--export-path") + 1];

    [SupportedOSPlatform("windows")]
    private static void AssertPrivateWindowsDirectory(string directory)
    {
        var acl = new DirectoryInfo(directory).GetAccessControl();
        var user = WindowsIdentity.GetCurrent().User!;
        acl.AreAccessRulesProtected.Should().BeTrue();
        acl.GetOwner(typeof(SecurityIdentifier)).Should().Be(user);
        var rule = acl.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Should()
            .ContainSingle()
            .Which;
        rule.IdentityReference.Should().Be(user);
        rule.AccessControlType.Should().Be(AccessControlType.Allow);
        rule.FileSystemRights.Should().Be(FileSystemRights.FullControl);
        rule.InheritanceFlags.Should()
            .Be(InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit);
    }

    [Theory]
    [InlineData("wrong-identity")]
    [InlineData("no-key")]
    [InlineData("bad-san")]
    [InlineData("bad-format")]
    [InlineData("expired-pfx")]
    [InlineData("future-pfx")]
    [InlineData("post-untrusted")]
    [InlineData("post-empty")]
    [InlineData("post-multiple")]
    [InlineData("post-renewed")]
    [InlineData("post-version")]
    [InlineData("post-valid-from")]
    [InlineData("post-valid-until")]
    [InlineData("post-cancel")]
    [InlineData("partial-export")]
    [InlineData("missing-pfx")]
    [InlineData("empty-pfx")]
    [InlineData("oversized-pfx")]
    [InlineData("invalid-pfx")]
    [InlineData("throw")]
    [InlineData("cancel")]
    public async Task ExportedMaterialAndFreshTrustMustAgreeOrPrivateStagingIsCleaned(
        string condition
    )
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        const string password = "generated-test-password";
        using var cli = new SyntheticCertificateExport(condition, password);
        var exporter = new HostDevelopmentCertificate(
            cli.Runner,
            new TestConsole(),
            root.Path,
            [new("tool", 6)]
        );

        // Act
        var action = () => exporter.ExportAsync(password, cli.Token);

        // Assert
        Exception failure = condition is "cancel" or "post-cancel"
            ? (await action.Should().ThrowAsync<OperationCanceledException>()).Which
            : (await action.Should().ThrowAsync<AboxException>()).Which;
        failure.ToString().Should().NotContain("secret").And.NotContain(password);
        failure.InnerException.Should().BeNull();
        if (failure is OperationCanceledException canceled)
            canceled.CancellationToken.Should().Be(cli.Token);
        Directory.GetFileSystemEntries(root.Path).Should().BeEmpty();
    }
}
