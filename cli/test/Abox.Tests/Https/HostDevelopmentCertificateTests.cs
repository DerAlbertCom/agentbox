using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text.Json;
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
        var tool = Path.Combine(
            root.Path,
            "sdk",
            "10.0.100",
            "DotnetTools",
            "dotnet-dev-certs",
            "10.0.0",
            "tools",
            "net10.0",
            "any",
            "dotnet-dev-certs.dll"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(tool)!);
        File.Copy(typeof(HostDevelopmentCertificateTests).Assembly.Location, tool);
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
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(
            new X509Extension("1.3.6.1.4.1.311.84.1.1", [(byte)format], false)
        );
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(10)
        );
        const string password = "generated-test-password";
        var bytes = certificate.Export(X509ContentType.Pfx, password);
        var metadata = JsonSerializer.Serialize(
            new[]
            {
                new
                {
                    certificate.Thumbprint,
                    certificate.Subject,
                    Version = format,
                    ValidityNotBefore = certificate.NotBefore.ToUniversalTime(),
                    ValidityNotAfter = certificate.NotAfter.ToUniversalTime(),
                    IsHttpsDevelopmentCertificate = true,
                    IsExportable = true,
                    TrustLevel = "Full",
                    X509SubjectAlternativeNameExtension = new[] { "localhost" },
                },
            }
        );
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
        calls
            .Where(call => !call.Arguments.Contains("--export-path"))
            .Should()
            .OnlyContain(call => call.OutputPolicy == ProcessOutputPolicy.CapturedSensitive);
        calls
            .Should()
            .OnlyContain(call =>
                call.Environment!["DOTNET_GENERATE_ASPNET_CERTIFICATE"] == "false"
            );
        Directory.GetFileSystemEntries(root.Path).Should().BeEmpty();
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
    [InlineData("partial-export")]
    [InlineData("throw")]
    [InlineData("cancel")]
    public async Task ExportedMaterialAndFreshTrustMustAgreeOrPrivateStagingIsCleaned(
        string condition
    )
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(condition == "bad-san" ? "not-localhost" : "localhost");
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(
            new X509Extension(
                "1.3.6.1.4.1.311.84.1.1",
                [condition == "bad-format" ? (byte)4 : (byte)6],
                false
            )
        );
        using var certificate = request.CreateSelfSigned(
            condition == "future-pfx"
                ? DateTimeOffset.UtcNow.AddDays(1)
                : DateTimeOffset.UtcNow.AddDays(-10),
            condition == "expired-pfx"
                ? DateTimeOffset.UtcNow.AddDays(-1)
                : DateTimeOffset.UtcNow.AddDays(10)
        );
        const string password = "generated-test-password";
        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
        var bytes = (condition == "no-key" ? publicOnly : certificate).Export(
            X509ContentType.Pfx,
            password
        );
        var before = new System.Text.Json.Nodes.JsonArray(
            new System.Text.Json.Nodes.JsonObject
            {
                ["Thumbprint"] =
                    condition == "wrong-identity" ? new string('A', 40) : certificate.Thumbprint,
                ["Subject"] = "CN=localhost",
                ["Version"] = 6,
                ["ValidityNotBefore"] =
                    condition == "future-pfx"
                        ? DateTime.UtcNow.AddDays(-1)
                        : certificate.NotBefore.ToUniversalTime(),
                ["ValidityNotAfter"] =
                    condition == "expired-pfx"
                        ? DateTime.UtcNow.AddDays(1)
                        : certificate.NotAfter.ToUniversalTime(),
                ["IsHttpsDevelopmentCertificate"] = true,
                ["IsExportable"] = true,
                ["TrustLevel"] = "Full",
                ["X509SubjectAlternativeNameExtension"] = new System.Text.Json.Nodes.JsonArray(
                    "localhost"
                ),
            }
        );
        var after = before.DeepClone().AsArray();
        switch (condition)
        {
            case "post-untrusted":
                after[0]!["TrustLevel"] = "None";
                break;
            case "post-empty":
                after.Clear();
                break;
            case "post-multiple":
                after.Add(after[0]!.DeepClone());
                break;
            case "post-renewed":
                after[0]!["Thumbprint"] = new string('B', 40);
                break;
        }
        using var cancellation = new CancellationTokenSource();
        var runner = Substitute.For<IProcessRunner>();
        var read = 0;
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var child = call.Arg<ProcessRequest>();
                if (!child.Arguments.Contains("--export-path"))
                    return Task.FromResult(
                        new ProcessResult(
                            0,
                            (read++ == 0 ? before : after).ToJsonString(),
                            "secret metadata"
                        )
                    );
                var path = child.Arguments[
                    Array.IndexOf(child.Arguments.ToArray(), "--export-path") + 1
                ];
                File.WriteAllBytes(path, bytes);
                if (condition == "cancel")
                {
                    cancellation.Cancel();
                    return Task.FromException<ProcessResult>(
                        new OperationCanceledException("secret cancellation", cancellation.Token)
                    );
                }
                if (condition == "throw")
                    return Task.FromException<ProcessResult>(new IOException("secret export"));
                return Task.FromResult(
                    new ProcessResult(
                        condition == "partial-export" ? 9 : 0,
                        "secret export",
                        "secret password"
                    )
                );
            });
        var exporter = new HostDevelopmentCertificate(
            runner,
            new TestConsole(),
            root.Path,
            [new("tool", 6)]
        );

        // Act
        var action = () => exporter.ExportAsync(password, cancellation.Token);

        // Assert
        Exception failure =
            condition == "cancel"
                ? (await action.Should().ThrowAsync<OperationCanceledException>()).Which
                : (await action.Should().ThrowAsync<AboxException>()).Which;
        failure.ToString().Should().NotContain("secret").And.NotContain(password);
        failure.InnerException.Should().BeNull();
        Directory.GetFileSystemEntries(root.Path).Should().BeEmpty();
    }
}
