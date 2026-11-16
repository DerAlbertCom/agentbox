using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace Abox.Infrastructure;

/// <summary>Owner-only staging, protected before an exporter can write private material.</summary>
public sealed class ProtectedTemporaryDirectory : IDisposable
{
    internal const string PreparationFailureMessage =
        "Cannot establish private certificate staging. Use an ordinary, accessible temporary directory with owner-only permissions; no export was started.";
    private readonly IConsole console;
    private bool disposed;
    private bool ownsDirectory;

    public ProtectedTemporaryDirectory(string parentDirectory, IConsole console)
    {
        this.console = console;
        Path = System.IO.Path.Combine(
            System.IO.Path.GetFullPath(parentDirectory),
            "abox-certificate-" + Guid.NewGuid().ToString("N")
        );
        try
        {
            ValidateAncestors(System.IO.Path.GetDirectoryName(Path)!);
            if (Directory.Exists(Path) || File.Exists(Path))
                throw new IOException();
            if (OperatingSystem.IsWindows())
                CreatePrivateWindowsDirectory();
            else
                CreatePrivateUnixDirectory();
            VerifyFinalDirectoryIsNotLinked();
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or System.Security.SecurityException
            )
        {
            Dispose();
            throw new AboxException(PreparationFailureMessage);
        }
    }

    public string Path { get; }

    private static void ValidateAncestors(string parentPath)
    {
        var parent = new DirectoryInfo(parentPath);
        if (!parent.Exists)
            throw new IOException();
        for (var ancestor = parent; ancestor is not null; ancestor = ancestor.Parent)
            if ((ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException();
    }

    [SupportedOSPlatform("windows")]
    private void CreatePrivateWindowsDirectory()
    {
        var user = WindowsIdentity.GetCurrent().User ?? throw new IOException();
        var security = OwnerOnlyWindowsSecurity(user);
        security.CreateDirectory(Path);
        // Take cleanup ownership before verification, which can still fail.
        ownsDirectory = true;
        VerifyWindowsSecurity(user);
    }

    [SupportedOSPlatform("windows")]
    private static DirectorySecurity OwnerOnlyWindowsSecurity(SecurityIdentifier user)
    {
        var security = new DirectorySecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(
            new FileSystemAccessRule(
                user,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow
            )
        );
        return security;
    }

    [SupportedOSPlatform("windows")]
    private void VerifyWindowsSecurity(SecurityIdentifier user)
    {
        var actual = new DirectoryInfo(Path).GetAccessControl();
        var rules = actual
            .GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        if (
            !actual.AreAccessRulesProtected
            || !user.Equals(actual.GetOwner(typeof(SecurityIdentifier)))
            || rules.Length != 1
        )
            throw new IOException();
        if (!IsOwnerFullControl(rules[0], user))
            throw new IOException();
    }

    [SupportedOSPlatform("windows")]
    private static bool IsOwnerFullControl(FileSystemAccessRule rule, SecurityIdentifier user) =>
        user.Equals(rule.IdentityReference)
        && rule.AccessControlType == AccessControlType.Allow
        && rule.FileSystemRights == FileSystemRights.FullControl
        && rule.InheritanceFlags
            == (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit);

    [UnsupportedOSPlatform("windows")]
    private void CreatePrivateUnixDirectory()
    {
        const UnixFileMode mode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        Directory.CreateDirectory(Path, mode);
        // Take cleanup ownership before verification, which can still fail.
        ownsDirectory = true;
        if (File.GetUnixFileMode(Path) != mode)
            throw new IOException();
    }

    private void VerifyFinalDirectoryIsNotLinked()
    {
        if ((File.GetAttributes(Path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException();
    }

    public override string ToString() => "ProtectedTemporaryDirectory (private staging)";

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        try
        {
            if (ownsDirectory && Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or System.Security.SecurityException
            )
        {
            console.WriteError(
                $"warning: private certificate staging cleanup failed. Remove task-owned directory {JsonSerializer.Serialize(Path)} before retrying; child diagnostics were suppressed."
            );
        }
    }
}
