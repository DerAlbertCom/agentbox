using System.Text.Json;
using System.Text.RegularExpressions;
using Abox;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;
using Abox.UserSecrets;

if (
    args.Length != 2
    || !Regex.IsMatch(args[0], @"\Aabox-issue8-[0-9a-f]{8}\z")
    || args[1] is not ("empty" or "nonempty")
)
{
    Console.Error.WriteLine(
        "Pass a fresh disposable abox-issue8-<8 hex> sandbox and empty|nonempty."
    );
    return 2;
}

var sandbox = args[0];
var nonempty = args[1] == "nonempty";
const string id = "abox-issue8-synthetic-only";
var temporary = Path.Combine(Path.GetTempPath(), "abox-issue8-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
var output = new ProcessOutput(new SystemConsole()) { Verbose = true };
var runner = new ProcessRunner(output);
var client = new SbxClient(runner, AboxPaths.For(temporary, temporary));
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var token = timeout.Token;
var source = JsonSerializer.Serialize(
    new
    {
        Sql = "Server=localhost,8004;Password='synthetic;localhost,8004'",
        Url = "http://127.0.0.1:8006/path?q=localhost:8004",
        Named = "https://sql.internal:8004/path",
        Unmapped = "Server=localhost,5432",
        Password = "Grüße 🔒 synthetic only",
    }
);
try
{
    await File.WriteAllTextAsync(
        Path.Combine(temporary, "abox.json"),
        "{\"userSecrets\":[\"" + id + "\"]}",
        token
    );
    var store = Path.Combine(temporary, ".microsoft", "usersecrets", id);
    Directory.CreateDirectory(store);
    var sourcePath = Path.Combine(store, "secrets.json");
    await File.WriteAllTextAsync(sourcePath, source, token);
    var ports = await client.GetUserSecretsHostPortsAsync(sandbox, token);
    int[] expectedPorts = nonempty ? [1521, 4317, 4318, 8004, 8005, 8006, 8007, 13133, 16686] : [];
    Require(ports.SequenceEqual(expectedPorts));
    Console.WriteLine("Exact runtime kit inventory and effective authorization: passed.");

    var folder = await FolderConfig.LoadAsync(temporary, token);
    var denied = new UserSecretsCopy(
        new AboxConfig { UserSecretsDenyList = ["localhost"] },
        new UserSecretsHost(null, temporary),
        client
    );
    var blocked = false;
    try
    {
        await denied.PrepareAsync(folder, false, token);
    }
    catch (AboxException)
    {
        blocked = true;
    }
    Require(blocked);
    Console.WriteLine("Original-value deny before rewriting: passed.");

    var copy = new UserSecretsCopy(new AboxConfig(), new UserSecretsHost(null, temporary), client);
    var prepared = await copy.PrepareAsync(folder, false, token);
    var report = await copy.CopyAsync(prepared, sandbox, token);
    Require(report.Stores.Count == 1 && report.Stores[0].KeyCount == 5);
    Require(
        report
            .Stores[0]
            .RewrittenKeys.SequenceEqual(nonempty ? ["Sql", "Url"] : Array.Empty<string>())
    );
    Require(report.Stores[0].RewrittenKeyCount == (nonempty ? 2 : 0) && !report.SafetyOverride);
    var expected = JsonSerializer.Serialize(
        new
        {
            Sql = nonempty
                ? "Server=host.docker.internal,8004;Password='synthetic;localhost,8004'"
                : "Server=localhost,8004;Password='synthetic;localhost,8004'",
            Url = nonempty
                ? "http://host.docker.internal:8006/path?q=localhost:8004"
                : "http://127.0.0.1:8006/path?q=localhost:8004",
            Named = "https://sql.internal:8004/path",
            Unmapped = "Server=localhost,5432",
            Password = "Grüße 🔒 synthetic only",
        }
    );
    var check = await runner.RunAsync(
        new ProcessRequest(
            "sbx",
            [
                "exec",
                "-i",
                sandbox,
                "sh",
                "-c",
                """
                set -eu
                if [ "${APPDATA+x}" = x ]; then root="$APPDATA/Microsoft/UserSecrets"; else root="$HOME/.microsoft/usersecrets"; fi
                file="$root/abox-issue8-synthetic-only/secrets.json"
                cmp - "$file"
                test "$(stat -c '%a:%u' "$file")" = "600:$(id -u)"
                test "$(stat -c '%a:%u' "$(dirname "$file")")" = "700:$(id -u)"
                """,
            ],
            StandardInput: expected,
            OutputPolicy: ProcessOutputPolicy.Sensitive
        ),
        token
    );
    Require(check.Succeeded && await File.ReadAllTextAsync(sourcePath, token) == source);
    Console.WriteLine(
        "Production rewrite/copy bytes, preservation, host immutability and modes: passed."
    );
    Console.WriteLine(
        "Synthetic-only rewrite smoke passed with verbose child diagnostics suppressed."
    );
    return 0;
}
catch (Exception)
{
    Console.Error.WriteLine(
        "Synthetic rewrite smoke failed; values and child diagnostics suppressed."
    );
    return 1;
}
finally
{
    Directory.Delete(temporary, recursive: true);
}

static void Require(bool condition)
{
    if (!condition)
        throw new InvalidOperationException();
}
