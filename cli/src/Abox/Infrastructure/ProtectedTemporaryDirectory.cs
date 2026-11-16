using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace Abox.Infrastructure;

/// <summary>Owner-only staging, protected before an exporter can write private material.</summary>
public sealed class ProtectedTemporaryDirectory : IDisposable
{
    private readonly IConsole console;
    private bool disposed;
    private bool created;

    public ProtectedTemporaryDirectory(string parentDirectory, IConsole console)
    {
        this.console = console;
        Path = System.IO.Path.Combine(
            System.IO.Path.GetFullPath(parentDirectory),
            "abox-certificate-" + Guid.NewGuid().ToString("N")
        );
        try
        {
            var parent = new DirectoryInfo(System.IO.Path.GetDirectoryName(Path)!);
            if (!parent.Exists)
                throw new IOException();
            for (var ancestor = parent; ancestor is not null; ancestor = ancestor.Parent)
                if ((ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException();
            if (Directory.Exists(Path) || File.Exists(Path))
                throw new IOException();
            if (OperatingSystem.IsWindows())
            {
                var user = WindowsIdentity.GetCurrent().User ?? throw new IOException();
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
                security.CreateDirectory(Path);
                created = true;
                var actual = new DirectoryInfo(Path).GetAccessControl();
                var rules = actual
                    .GetAccessRules(true, true, typeof(SecurityIdentifier))
                    .Cast<FileSystemAccessRule>()
                    .ToArray();
                if (
                    !actual.AreAccessRulesProtected
                    || !user.Equals(actual.GetOwner(typeof(SecurityIdentifier)))
                    || rules.Length != 1
                    || !user.Equals(rules[0].IdentityReference)
                    || rules[0].AccessControlType != AccessControlType.Allow
                    || rules[0].FileSystemRights != FileSystemRights.FullControl
                    || rules[0].InheritanceFlags
                        != (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit)
                )
                    throw new IOException();
            }
            else
            {
                const UnixFileMode mode =
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
                Directory.CreateDirectory(Path, mode);
                created = true;
                if (File.GetUnixFileMode(Path) != mode)
                    throw new IOException();
            }
            if ((File.GetAttributes(Path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException();
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or System.Security.SecurityException
            )
        {
            Dispose();
            throw new AboxException(
                "Cannot establish private certificate staging. Use an ordinary, accessible temporary directory with owner-only permissions; no export was started."
            );
        }
    }

    public string Path { get; }

    public override string ToString() => "ProtectedTemporaryDirectory (private staging)";

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        try
        {
            if (created && Directory.Exists(Path))
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
