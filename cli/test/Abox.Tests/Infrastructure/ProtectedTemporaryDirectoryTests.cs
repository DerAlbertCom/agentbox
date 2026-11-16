using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

public sealed class ProtectedTemporaryDirectoryTests
{
    [Theory]
    [InlineData("")]
    [InlineData("nested")]
    public void LinkedParentOrAncestorIsRejectedWithoutRemovingUnownedContents(string subdirectory)
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var target = System.IO.Path.Combine(root.Path, "target");
        Directory.CreateDirectory(System.IO.Path.Combine(target, "nested"));
        var marker = System.IO.Path.Combine(target, "keep.txt");
        File.WriteAllText(marker, "unowned contents");
        var link = System.IO.Path.Combine(root.Path, "linked");
        CreateDirectoryLink(link, target);
        var parent = System.IO.Path.Combine(link, subdirectory);
        var console = Substitute.For<IConsole>();

        // Act
        var create = () => new ProtectedTemporaryDirectory(parent, console);

        // Assert
        create.Should().Throw<AboxException>().WithMessage("*no export was started.*");
        File.ReadAllText(marker).Should().Be("unowned contents");
        Directory
            .GetDirectories(target, "abox-certificate-*", SearchOption.AllDirectories)
            .Should()
            .BeEmpty();
        console.DidNotReceive().WriteError(Arg.Any<string>());
    }

    private static void CreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException(
                Xunit.v3.DynamicSkipToken.Value
                    + "Windows directory-link creation needs permission."
            );
        }
    }

    [Fact]
    public void ExportStartsInAnAlreadyPrivateDirectoryAndDisposalRemovesItsContents()
    {
        // Arrange
        using var parent = new CertificateTestDirectory();
        var console = Substitute.For<IConsole>();
        string path;

        // Act
        using (var staging = new ProtectedTemporaryDirectory(parent.Path, console))
        {
            path = staging.Path;

            // Assert
            if (OperatingSystem.IsWindows())
            {
                var acl = new DirectoryInfo(path).GetAccessControl();
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
                rule.InheritanceFlags.Should()
                    .Be(InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit);
            }
            else
                File.GetUnixFileMode(path)
                    .Should()
                    .Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            // Arrange
            File.WriteAllText(System.IO.Path.Combine(path, "synthetic.pfx"), "not a real key");
        }

        // Assert
        Directory.Exists(path).Should().BeFalse();
        Directory.GetFileSystemEntries(parent.Path).Should().BeEmpty();
        console.DidNotReceive().WriteError(Arg.Any<string>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task APartialSyntheticExportIsRemovedAfterFailureOrCancellation(bool cancel)
    {
        // Arrange
        using var parent = new CertificateTestDirectory();
        var console = Substitute.For<IConsole>();
        string? path = null;
        var export = async () =>
        {
            using var staging = new ProtectedTemporaryDirectory(parent.Path, console);
            path = staging.Path;
            // Explicit export mock: no dotnet tool or host certificate store is used.
            using var key = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=localhost",
                key,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );
            using var certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(1)
            );
            var bytes = certificate.Export(
                X509ContentType.Pfx,
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
            );
            try
            {
                await File.WriteAllBytesAsync(System.IO.Path.Combine(path, "partial.pfx"), bytes);
                if (cancel)
                    throw new OperationCanceledException();
                throw new IOException("synthetic exporter failed");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        };

        // Act
        var action = () => export();

        // Assert
        if (cancel)
            await action.Should().ThrowAsync<OperationCanceledException>();
        else
            await action.Should().ThrowAsync<IOException>();
        Directory.Exists(path).Should().BeFalse();
        Directory.GetFileSystemEntries(parent.Path).Should().BeEmpty();
    }

    [Fact]
    public void CleanupFailureWarnsWithoutHidingTheOriginalExporterFailure()
    {
        // Arrange
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException(
                Xunit.v3.DynamicSkipToken.Value
                    + "This fixture uses Windows exclusive file locks to force cleanup failure."
            );
        using var parent = new CertificateTestDirectory();
        var console = Substitute.For<IConsole>();
        var staging = new ProtectedTemporaryDirectory(parent.Path, console);
        using var blockedFile = new FileStream(
            System.IO.Path.Combine(staging.Path, "synthetic.pfx"),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None
        );
        var export = () =>
        {
            using (staging)
                throw new IOException("known primary synthetic exporter failure");
        };

        // Act
        var error = export.Should().Throw<IOException>();

        // Assert
        error.Which.Message.Should().Be("known primary synthetic exporter failure");
        console
            .Received(1)
            .WriteError(
                Arg.Is<string>(text =>
                    text.Contains("cleanup failed")
                    && text.Contains("abox-certificate-")
                    && !text.Contains("known primary")
                )
            );
    }
}
