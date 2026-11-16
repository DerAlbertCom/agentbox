using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Abox;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sbx;

try
{
    if (args is not [var template])
        throw new InvalidOperationException("Supply an already available template.");
    return await Probe(template);
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"Synthetic transfer probe failed ({exception.GetType().Name}); private diagnostics suppressed."
    );
    if (exception is ProbeFailure)
        Console.Error.WriteLine("Failed step: " + exception.Message);
    return 1;
}

static async Task<int> Probe(string template)
{
    var checkoutDirectory = new DirectoryInfo(AppContext.BaseDirectory);
    while (
        checkoutDirectory is not null
        && !File.Exists(Path.Combine(checkoutDirectory.FullName, "CODING_STANDARDS.md"))
    )
        checkoutDirectory = checkoutDirectory.Parent;
    if (checkoutDirectory is null)
        throw new InvalidOperationException("Run from the task checkout.");
    var checkout = checkoutDirectory.FullName;
    var workspace = Path.Combine(
        checkout,
        ".abox",
        "abox-issue46-transfer-" + Guid.NewGuid().ToString("N")[..8]
    );
    var runner = new DamagingRunner();
    var client = new SbxClient(runner, AboxPaths.For(workspace, workspace));
    var name = SandboxName.For(workspace);
    Console.WriteLine(
        "Synthetic-only transfer probe: read-only sandbox inventory, then fresh task-owned sandbox."
    );
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
    var token = timeout.Token;
    if (await client.FindSandboxAsync(name, token) is not null || Directory.Exists(workspace))
        throw new InvalidOperationException("Requires a fresh disposable sandbox.");
    Directory.CreateDirectory(workspace);
    var created = false;
    byte[]? original = null;
    byte[]? renewed = null;
    try
    {
        created = true;
        Require(
            (await client.CertificateProbeCreateAsync(workspace, name, template, token)).Succeeded,
            "create"
        );
        Require(
            (
                await client.CertificateProbeExecAsync(
                    name,
                    [
                        "python3",
                        "-I",
                        "-c",
                        "import os; assert os.geteuid() != 0; assert not os.path.lexists('/tmp/abox-https-' + str(os.geteuid()))",
                    ],
                    token
                )
            ).Succeeded,
            "fresh runtime destination"
        );
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var renewedPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        original = SyntheticExport(password);
        renewed = SyntheticExport(renewedPassword);
        await client.StageDevelopmentCertificateAsync(name, original, password, token);
        await Check(original, password);
        Console.WriteLine(
            "PASS: protected byte-preserving PFX/password staging; no host store access."
        );

        foreach (var damage in new[] { "short", "digest", "size", "extra" })
        {
            runner.Damage = damage;
            await Reject(() =>
                client.StageDevelopmentCertificateAsync(name, renewed, renewedPassword, token)
            );
            await Check(original, password);
        }
        Console.WriteLine(
            "PASS: truncated/corrupt/incorrect-length/extra data cannot replace the previous bundle."
        );

        foreach (var fault in new[] { "mode", "link", "hardlink", "directory" })
        {
            await Mutate(fault, false);
            await Reject(() =>
                client.StageDevelopmentCertificateAsync(name, renewed, renewedPassword, token)
            );
            await Mutate(fault, true);
            await Check(original, password);
        }
        Console.WriteLine(
            "PASS: permissive file, symlink, hardlink and nonregular destination are rejected unchanged."
        );
        foreach (var fault in new[] { "root-mode", "root-link" })
        {
            await MutateRoot(fault, false);
            await Reject(() =>
                client.StageDevelopmentCertificateAsync(name, renewed, renewedPassword, token)
            );
            await MutateRoot(fault, true);
            await Check(original, password);
        }
        var uidResult = await client.CertificateProbeExecAsync(name, ["id", "-u"], token);
        Require(
            uidResult.Succeeded && int.TryParse(uidResult.StandardOutput.Trim(), out _),
            "read disposable uid"
        );
        var uid = int.Parse(
            uidResult.StandardOutput.Trim(),
            System.Globalization.CultureInfo.InvariantCulture
        );
        foreach (var restore in new[] { false, true })
        {
            Require(
                (
                    await client.TransferProbeCheckAsync(
                        name,
                        """
                        import json, os, sys
                        assert os.environ['SANDBOX_NAME'].startswith('abox-issue46-transfer-')
                        uid, restore = json.load(sys.stdin)
                        assert uid != 0
                        os.chown('/tmp/abox-https-' + str(uid) + '/active.json', uid if restore else 0, -1)
                        """,
                        JsonSerializer.Serialize(new object[] { uid, restore }),
                        token,
                        root: true
                    )
                ).Succeeded,
                "disposable ownership fixture"
            );
            if (!restore)
                await Reject(() =>
                    client.StageDevelopmentCertificateAsync(name, renewed, renewedPassword, token)
                );
        }
        await Check(original, password);
        Console.WriteLine(
            "PASS: linked/permissive runtime directory and wrong-owned certificate bundle are rejected."
        );

        runner.Damage = "cancel";
        using (var cancellation = new CancellationTokenSource())
        {
            runner.Cancel = cancellation;
            try
            {
                await client.StageDevelopmentCertificateAsync(
                    name,
                    renewed,
                    renewedPassword,
                    cancellation.Token
                );
                throw new InvalidOperationException("Expected cancellation.");
            }
            catch (OperationCanceledException) { }
        }
        await Check(original, password);
        Console.WriteLine(
            "PASS: cancellation cleanup uses a separate bounded request and retains the old bundle."
        );

        for (var index = 0; index < 3; index++)
        {
            await client.StageDevelopmentCertificateAsync(name, renewed, renewedPassword, token);
            await Check(renewed, renewedPassword);
        }
        Console.WriteLine(
            "PASS: renewed PFX/password activate together; repeat refresh leaves exactly one bundle and lock."
        );
        var observer = client.TransferProbeCheckAsync(
            name,
            """
            import json, os, sys, time
            candidates = json.load(sys.stdin)
            path = '/tmp/abox-https-' + str(os.geteuid()) + '/active.json'
            for attempt in range(1000):
                with open(path) as source:
                    current = json.load(source)
                assert current in candidates
                time.sleep(0.005)
            """,
            JsonSerializer.Serialize(
                new[]
                {
                    new
                    {
                        version = 1,
                        pfx = Convert.ToBase64String(original),
                        password,
                    },
                    new
                    {
                        version = 1,
                        pfx = Convert.ToBase64String(renewed),
                        password = renewedPassword,
                    },
                }
            ),
            token
        );
        for (var index = 0; index < 6; index++)
            await client.StageDevelopmentCertificateAsync(
                name,
                index % 2 == 0 ? original : renewed,
                index % 2 == 0 ? password : renewedPassword,
                token
            );
        Require((await observer).Succeeded, "concurrent reader observes only complete generations");
        await Check(renewed, renewedPassword);
        Console.WriteLine(
            "PASS: a concurrent reader observes only complete old or new certificate/password generations."
        );
        return 0;

        async Task Check(byte[] expected, string expectedPassword)
        {
            Require(
                (
                    await client.TransferProbeCheckAsync(
                        name,
                        """
                        import base64, json, os, stat, sys
                        expected = json.load(sys.stdin)
                        root = '/tmp/abox-https-' + str(os.geteuid())
                        assert stat.S_IMODE(os.lstat(root).st_mode) == 0o700
                        assert set(os.listdir(root)) == {'active.json', '.lock'}
                        for entry in os.scandir(root):
                            info = entry.stat(follow_symlinks=False)
                            assert stat.S_ISREG(info.st_mode) and info.st_uid == os.geteuid()
                            assert info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == 0o600
                        with open(root + '/active.json') as source:
                            actual = json.load(source)
                        assert actual == expected
                        assert base64.b64decode(actual['pfx'], validate=True)
                        """,
                        JsonSerializer.Serialize(
                            new
                            {
                                version = 1,
                                pfx = Convert.ToBase64String(expected),
                                password = expectedPassword,
                            }
                        ),
                        token
                    )
                ).Succeeded,
                "exact private bundle and permissions"
            );
        }

        async Task Mutate(string fault, bool restore)
        {
            Require(
                (
                    await client.TransferProbeCheckAsync(
                        name,
                        """
                        import json, os, sys
                        fault, restore = json.load(sys.stdin)
                        path = '/tmp/abox-https-' + str(os.geteuid()) + '/active.json'
                        backup = path.rsplit('/', 1)[0] + '.probe-original'
                        if fault == 'mode':
                            os.chmod(path, 0o600 if restore else 0o644)
                        elif restore:
                            if fault == 'directory': os.rmdir(path)
                            else: os.unlink(path)
                            os.rename(backup, path)
                        else:
                            assert not os.path.lexists(backup)
                            os.rename(path, backup)
                            if fault == 'link': os.symlink(backup, path)
                            elif fault == 'hardlink': os.link(backup, path)
                            else: os.mkdir(path)
                        """,
                        JsonSerializer.Serialize(new object[] { fault, restore }),
                        token
                    )
                ).Succeeded,
                "synthetic destination fixture"
            );
        }

        async Task MutateRoot(string fault, bool restore)
        {
            Require(
                (
                    await client.TransferProbeCheckAsync(
                        name,
                        """
                        import json, os, sys
                        fault, restore = json.load(sys.stdin)
                        root = '/tmp/abox-https-' + str(os.geteuid())
                        if fault == 'root-mode':
                            os.chmod(root, 0o700 if restore else 0o755)
                        elif restore:
                            os.unlink(root)
                            os.rename(root + '.probe-original-dir', root)
                        else:
                            assert not os.path.lexists(root + '.probe-original-dir')
                            os.rename(root, root + '.probe-original-dir')
                            os.symlink(root + '.probe-original-dir', root)
                        """,
                        JsonSerializer.Serialize(new object[] { fault, restore }),
                        token
                    )
                ).Succeeded,
                "synthetic directory fixture"
            );
        }
    }
    finally
    {
        if (original is not null)
            CryptographicOperations.ZeroMemory(original);
        if (renewed is not null)
            CryptographicOperations.ZeroMemory(renewed);
        if (created)
        {
            if (await client.FindSandboxAsync(name, CancellationToken.None) is not null)
            {
                var result = await client.RemoveSandboxAsync(name, CancellationToken.None);
                if (
                    !result.Succeeded
                    || await client.FindSandboxAsync(name, CancellationToken.None) is not null
                )
                    Console.Error.WriteLine("CLEANUP REQUIRED: " + name);
                else
                    Console.WriteLine(
                        "PASS: task-owned sandbox removed; no existing sandbox was touched."
                    );
            }
        }
        Directory.Delete(workspace, recursive: true);
    }
}

static byte[] SyntheticExport(string password)
{
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
    using var certificate = request.CreateSelfSigned(
        DateTimeOffset.UtcNow.AddDays(-1),
        DateTimeOffset.UtcNow.AddDays(1)
    );
    return certificate.Export(X509ContentType.Pfx, password);
}

static async Task Reject(Func<Task> action)
{
    try
    {
        await action();
    }
    catch (AboxException)
    {
        return;
    }
    throw new InvalidOperationException("Unsafe input was not rejected.");
}

static void Require(bool condition, string step)
{
    if (!condition)
        throw new ProbeFailure(step);
}

sealed class ProbeFailure(string step) : Exception(step);

sealed class DamagingRunner : IProcessRunner
{
    private readonly ProcessRunner runner = new();
    public string? Damage { get; set; }
    public CancellationTokenSource? Cancel { get; set; }

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken token)
    {
        if (request.Arguments.Contains("stage") && Damage is { } damage)
        {
            Damage = null;
            if (damage == "cancel")
            {
                Cancel!.Cancel();
                throw new OperationCanceledException(token);
            }
            var input = request.StandardInput!;
            var frame = input.Split('\n', 4);
            request = request with
            {
                StandardInput = damage switch
                {
                    "short" => input[..^7],
                    "digest" => input[..^1] + "x",
                    "size" => string.Join('\n', frame[0], "1", frame[2], frame[3]),
                    "extra" => input + "x",
                    _ => throw new InvalidOperationException("Unknown synthetic damage."),
                },
            };
        }
        return runner.RunAsync(request, token);
    }
}
