using System.Text.RegularExpressions;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;
using Abox.UserSecrets;

if (args.Length != 1 || !Regex.IsMatch(args[0], @"\Aabox-issue6-[0-9a-f]{8}\z"))
{
    Console.Error.WriteLine("Pass only a fresh disposable abox-issue6-<8 hex> sandbox.");
    return 2;
}

var sandbox = args[0];
var temporary = Path.Combine(Path.GetTempPath(), "abox-issue6-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
const string id = "abox-issue6-synthetic-only";
const string payload = "{\"Nested\":{\"Password\":\"Grüße 🔒 synthetic only\"},\"Flag\":true}";
const string file = "$HOME/.microsoft/usersecrets/abox-issue6-synthetic-only/secrets.json";
var output = new ProcessOutput(new SystemConsole()) { Verbose = true };
var runner = new ProcessRunner(output);
var paths = AboxPaths.For(temporary, temporary);
try
{
    await File.WriteAllTextAsync(
        Path.Combine(temporary, "abox.json"),
        "{\"userSecrets\":[\"" + id + "\"]}"
    );
    var store = Path.Combine(temporary, ".microsoft", "usersecrets", id);
    Directory.CreateDirectory(store);
    await File.WriteAllTextAsync(Path.Combine(store, "secrets.json"), payload);
    var client = new SbxClient(runner, paths);
    var copy = new UserSecretsCopy(new AboxConfig(), new UserSecretsHost(null, temporary), client);
    var prepared = await copy.PrepareAsync(
        await FolderConfig.LoadAsync(temporary, CancellationToken.None),
        false,
        CancellationToken.None
    );
    await copy.CopyAsync(prepared, sandbox, CancellationToken.None);
    await Check(
        "UTF-8 bytes and application-user ownership/modes",
        $"cmp - \"{file}\" && test \"$(stat -c '%a:%u' \"{file}\")\" = \"600:$(id -u)\" && test \"$(stat -c '%a:%u' \"$(dirname \"{file}\")\")\" = \"700:$(id -u)\"",
        payload
    );

    foreach (var damage in new[] { "short", "corrupt" })
    {
        var damaged = new SbxClient(
            new MutatingRunner(
                runner,
                request =>
                    request with
                    {
                        StandardInput =
                            damage == "short"
                                ? request.StandardInput![..^3]
                                : request.StandardInput![..^1] + "x",
                    }
            ),
            paths
        );
        try
        {
            await damaged.WriteUserSecretsAsync(
                sandbox,
                id,
                "{\"Replacement\":123456789}",
                CancellationToken.None
            );
            throw new InvalidOperationException();
        }
        catch (Abox.AboxException) { }
        await Check(damage + " frame retains prior bytes", $"cmp - \"{file}\"", payload);
    }

    await Check(
        "symlink setup (disposable sandbox only)",
        $"mv \"{file}\" \"{file}.saved\" && ln -s secrets.json.saved \"{file}\"",
        null
    );
    try
    {
        await copy.CopyAsync(prepared, sandbox, CancellationToken.None);
        throw new InvalidOperationException();
    }
    catch (Abox.AboxException) { }
    await Check(
        "symlink rejected and original untouched",
        $"test -L \"{file}\" && cmp - \"{file}.saved\"",
        payload
    );
    await Check(
        "restore disposable destination",
        $"rm \"{file}\" && mv \"{file}.saved\" \"{file}\"",
        null
    );

    var appDataClient = new SbxClient(
        new MutatingRunner(
            runner,
            request =>
                request with
                {
                    Arguments =
                    [
                        .. request.Arguments.Take(3),
                        "env",
                        "APPDATA=/tmp/abox-issue6-appdata",
                        .. request.Arguments.Skip(3),
                    ],
                }
        ),
        paths
    );
    await appDataClient.WriteUserSecretsAsync(sandbox, id, payload, CancellationToken.None);
    await Check(
        "effective APPDATA precedes HOME",
        "cmp - /tmp/abox-issue6-appdata/Microsoft/UserSecrets/abox-issue6-synthetic-only/secrets.json",
        payload
    );
    Console.WriteLine(
        "Production user-secrets copy smoke passed; synthetic values only, verbose child output suppressed."
    );
    return 0;
}
catch (Exception)
{
    Console.Error.WriteLine(
        "Production user-secrets copy smoke failed; child diagnostics suppressed."
    );
    return 1;
}
finally
{
    Directory.Delete(temporary, recursive: true);
}

async Task Check(string label, string script, string? input)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var result = await runner.RunAsync(
        new ProcessRequest(
            "sbx",
            ["exec", "-i", sandbox, "sh", "-c", script],
            StandardInput: input,
            OutputPolicy: ProcessOutputPolicy.Sensitive
        ),
        timeout.Token
    );
    if (!result.Succeeded)
        throw new InvalidOperationException();
    Console.WriteLine(label + ": passed.");
}

sealed class MutatingRunner(IProcessRunner inner, Func<ProcessRequest, ProcessRequest> mutate)
    : IProcessRunner
{
    public Task<ProcessResult> RunAsync(
        ProcessRequest request,
        CancellationToken cancellationToken
    ) => inner.RunAsync(mutate(request), cancellationToken);
}
