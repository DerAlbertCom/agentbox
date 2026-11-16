using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Abox.Infrastructure;
using Microsoft.Win32.SafeHandles;

[SupportedOSPlatform("windows")]
static class HostCertificateExport
{
    internal const string Thumbprint = "8E84EA4F2F4EC8548DAF359CCB1179008251C714";
    internal const string Staging =
        "C:/Users/UI520628/AppData/Local/Temp/12/opencode/abox-story3-host-cert-20261116";
    private const string Dotnet = "C:/Program Files/dotnet/dotnet.exe";
    private const string Tool =
        "C:/Program Files/dotnet/sdk/10.0.404/DotnetTools/dotnet-dev-certs/10.0.15-servicing.26529.108/tools/net10.0/any/dotnet-dev-certs.dll";
    private const string DevOid = "1.3.6.1.4.1.311.84.1.1";
    private static readonly Dictionary<string, string> QuietEnvironment = new()
    {
        ["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false",
        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
        ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
        ["DOTNET_NOLOGO"] = "1",
    };

    internal static async Task PreflightAsync(IProcessRunner runner, CancellationToken token)
    {
        AssertStaging();
        if (Directory.EnumerateFiles(Staging, "*.pfx").Any())
            throw new InvalidOperationException(
                "Unexpected existing private export; manual inspection is required."
            );
        var der = InspectExisting();
        await CheckCliMetadata(runner, der, token);
        _ = Snapshot();
        Console.WriteLine(
            "Host preflight: owner-only staging; sole existing trusted/exportable v6 candidate; current dates, key association, localhost SAN and system chain verified."
        );
    }

    internal static async Task StageAsync(
        IProcessRunner runner,
        Func<byte[], string, byte[], Task> transfer,
        CancellationToken token
    )
    {
        AssertStaging();
        if (Directory.EnumerateFiles(Staging, "*.pfx").Any())
            throw new InvalidOperationException(
                "Unexpected existing private export; manual inspection is required."
            );
        var before = Snapshot();
        var expectedDer = InspectExisting();
        await CheckCliMetadata(runner, expectedDer, token);
        var exportPath = Path.Combine(Staging, $"host-export-{Guid.NewGuid():N}.pfx");
        byte[]? pfx = null;
        try
        {
            // The empty file inherits the already-verified owner-only DACL before CLI export.
            using (new FileStream(exportPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { }
            AssertPrivateFile(exportPath);
            var passwordBytes = RandomNumberGenerator.GetBytes(32);
            string password;
            try
            {
                password = Convert.ToBase64String(passwordBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
            }
            // CLI has no existing-only export option. Recheck the sole eligible v6 identity
            // immediately beforehand; never run export after an empty/ambiguous preflight.
            AssertStaging();
            if (!InspectExisting().AsSpan().SequenceEqual(expectedDer) || Snapshot() != before)
                throw new InvalidOperationException(
                    "Host certificate inventory changed before export."
                );
            await CheckCliMetadata(runner, expectedDer, token);
            var exported = await runner.RunAsync(
                new ProcessRequest(
                    Dotnet,
                    [
                        "exec",
                        Tool,
                        "https",
                        "--export-path",
                        exportPath,
                        "--password",
                        password,
                        "--format",
                        "Pfx",
                    ],
                    Environment: QuietEnvironment,
                    StandardInput: "",
                    OutputPolicy: ProcessOutputPolicy.Sensitive
                ),
                token
            );
            if (!exported.Succeeded)
                throw new InvalidOperationException(
                    "CLI export failed; no native store-key fallback is allowed."
                );
            AssertStaging();
            AssertPrivateFile(exportPath);
            if (new FileInfo(exportPath).Length is < 1 or > 1024 * 1024)
                throw new InvalidOperationException("CLI PFX size is invalid.");
            pfx = File.ReadAllBytes(exportPath);
            using var loaded = X509CertificateLoader.LoadPkcs12(
                pfx,
                password,
                X509KeyStorageFlags.EphemeralKeySet
            );
            Validate(loaded);
            if (!loaded.RawDataMemory.Span.SequenceEqual(expectedDer) || Snapshot() != before)
                throw new InvalidOperationException(
                    "Actual CLI PFX identity or host inventory changed."
                );
            // Exercise only the key loaded ephemerally from the actual CLI file, never export
            // private key material through X509Store/.NET as an alternative to the CLI.
            using var privateKey =
                loaded.GetRSAPrivateKey()
                ?? throw new InvalidOperationException("CLI PFX private key is unavailable.");
            using var publicKey =
                loaded.GetRSAPublicKey()
                ?? throw new InvalidOperationException("CLI PFX public key is unavailable.");
            var challenge = RandomNumberGenerator.GetBytes(32);
            var signature = privateKey.SignData(
                challenge,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );
            if (
                !publicKey.VerifyData(
                    challenge,
                    signature,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1
                )
            )
                throw new InvalidOperationException("CLI PFX private-key proof failed.");
            await CheckCliMetadata(runner, expectedDer, token);
            Console.WriteLine(
                "Actual CLI PFX: expected full DER, usable private key, dates/SAN/v6 and unchanged host stores verified."
            );
            await transfer(pfx, password, expectedDer);
        }
        finally
        {
            if (pfx is not null)
                CryptographicOperations.ZeroMemory(pfx);
            if (File.Exists(exportPath))
            {
                AssertStaging();
                AssertPrivateFile(exportPath);
                File.Delete(exportPath);
            }
            if (File.Exists(exportPath))
                throw new InvalidOperationException("Host private-export cleanup is unconfirmed.");
            Console.WriteLine(
                "Host private export removed in finally; no host password file was created."
            );
        }
        if (Snapshot() != before)
            throw new InvalidOperationException(
                "Host store inventory changed during the manual copy."
            );
    }

    internal static byte[] InspectExisting()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var all = store.Certificates;
        try
        {
            var development = all.Cast<X509Certificate2>()
                .Where(c => c.Extensions[DevOid] is not null)
                .ToArray();
            if (development.Length != 1 || development[0].Thumbprint != Thumbprint)
                throw new InvalidOperationException(
                    "Expected exactly the previously identified host development certificate."
                );
            var candidate = development[0];
            Validate(candidate);
            using var publicOnly = X509CertificateLoader.LoadCertificate(candidate.RawData);
            using var chain = new X509Chain();
            chain.ChainPolicy.DisableCertificateDownloads = true;
            chain.ChainPolicy.RevocationMode = X509RevocationMode.Offline;
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
            if (!chain.Build(publicOnly) || chain.ChainStatus.Length != 0)
                throw new InvalidOperationException(
                    "Existing host certificate failed normal system-chain validation."
                );
            return candidate.RawData;
        }
        finally
        {
            foreach (var certificate in all)
                certificate.Dispose();
        }
    }

    private static void Validate(X509Certificate2 certificate)
    {
        var now = DateTime.UtcNow;
        var dev = certificate.Extensions[DevOid];
        var san = certificate
            .Extensions.OfType<X509SubjectAlternativeNameExtension>()
            .SingleOrDefault();
        if (
            certificate.Thumbprint != Thumbprint
            || certificate.Subject != "CN=localhost"
            || !certificate.HasPrivateKey
            || certificate.NotBefore.ToUniversalTime() > now
            || certificate.NotAfter.ToUniversalTime() <= now
            || dev is null
            || !dev.RawData.AsSpan().SequenceEqual(new byte[] { 6 })
            || san is null
            || !san.EnumerateDnsNames().Contains("localhost", StringComparer.OrdinalIgnoreCase)
        )
            throw new InvalidOperationException(
                "Existing certificate key/date/localhost SAN/format is ineligible."
            );
    }

    private static async Task CheckCliMetadata(
        IProcessRunner runner,
        byte[] expectedDer,
        CancellationToken token
    )
    {
        var result = await runner.RunAsync(
            new ProcessRequest(
                Dotnet,
                ["exec", Tool, "https", "--check-trust-machine-readable"],
                Environment: QuietEnvironment,
                StandardInput: "",
                OutputPolicy: ProcessOutputPolicy.CapturedSensitive
            ),
            token
        );
        if (!result.Succeeded)
            throw new InvalidOperationException("Read-only CLI certificate metadata failed.");
        using var json = JsonDocument.Parse(result.StandardOutput);
        if (
            json.RootElement.ValueKind != JsonValueKind.Array
            || json.RootElement.GetArrayLength() != 1
        )
            throw new InvalidOperationException("CLI certificate metadata is empty/ambiguous.");
        var row = json.RootElement[0];
        using var expected = X509CertificateLoader.LoadCertificate(expectedDer);
        if (
            row.GetProperty("Thumbprint").GetString() != Thumbprint
            || row.GetProperty("Subject").GetString() != expected.Subject
            || row.GetProperty("Version").GetInt32() != 6
            || !row.GetProperty("IsHttpsDevelopmentCertificate").GetBoolean()
            || !row.GetProperty("IsExportable").GetBoolean()
            || row.GetProperty("TrustLevel").GetString() != "Full"
            || row.GetProperty("ValidityNotBefore").GetDateTime().ToUniversalTime()
                != expected.NotBefore.ToUniversalTime()
            || row.GetProperty("ValidityNotAfter").GetDateTime().ToUniversalTime()
                != expected.NotAfter.ToUniversalTime()
            || !row.GetProperty("X509SubjectAlternativeNameExtension")
                .EnumerateArray()
                .Any(v => v.GetString() == "localhost")
        )
            throw new InvalidOperationException(
                "CLI metadata does not confirm the exact existing trusted/exportable v6 candidate."
            );
    }

    private static string Snapshot()
    {
        var rows = new List<string>();
        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        foreach (var name in new[] { StoreName.My, StoreName.Root })
        {
            using var store = new X509Store(name, location);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            var all = store.Certificates;
            try
            {
                rows.AddRange(
                    all.Cast<X509Certificate2>()
                        .Select(c =>
                            $"{location}/{name}/{c.GetCertHashString(HashAlgorithmName.SHA256)}"
                        )
                );
            }
            finally
            {
                foreach (var certificate in all)
                    certificate.Dispose();
            }
        }
        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(string.Join('\n', rows.Order(StringComparer.Ordinal)))
            )
        );
    }

    internal static void AssertStaging()
    {
        for (var item = new DirectoryInfo(Staging); item is not null; item = item.Parent)
            if (!item.Exists || (item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException(
                    "Host staging ancestor is missing or redirected."
                );
        var acl = new DirectoryInfo(Staging).GetAccessControl();
        var sid =
            WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Missing host user identity.");
        var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        if (
            !acl.AreAccessRulesProtected
            || acl.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner
            || !owner.Equals(sid)
            || rules.Length != 1
            || rules[0].IsInherited
            || !rules[0].IdentityReference.Equals(sid)
            || rules[0].AccessControlType != AccessControlType.Allow
            || rules[0].FileSystemRights != FileSystemRights.FullControl
            || rules[0].InheritanceFlags
                != (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit)
            || rules[0].PropagationFlags != PropagationFlags.None
        )
            throw new InvalidOperationException(
                "Host staging owner-only inheritable DACL is unsuitable."
            );
        if (Directory.GetFiles(Staging, "*.pfx").Length != 0)
        {
            // The only allowed private export is the current operation's precreated file.
            foreach (var path in Directory.GetFiles(Staging, "*.pfx"))
                AssertPrivateFile(path);
        }
    }

    private static void AssertPrivateFile(string path)
    {
        var info = new FileInfo(path);
        if (
            !info.Exists
            || (info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0
        )
            throw new InvalidOperationException(
                "Host private-export path is not an ordinary file."
            );
        var acl = info.GetAccessControl();
        var sid =
            WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Missing host user identity.");
        var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        if (
            acl.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner
            || !owner.Equals(sid)
            || rules.Length != 1
            || !rules[0].IdentityReference.Equals(sid)
            || rules[0].AccessControlType != AccessControlType.Allow
            || rules[0].FileSystemRights != FileSystemRights.FullControl
        )
            throw new InvalidOperationException("Host private-export owner/DACL mismatch.");
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!GetFileInformationByHandle(handle, out var metadata) || metadata.NumberOfLinks != 1)
            throw new InvalidOperationException("Host private-export hard-link validation failed.");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle handle,
        out ByHandleFileInformation info
    );

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
