using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

if (
    args.Length is < 1 or > 2
    || !Regex.IsMatch(args[0], "^abox-issue5-(local|published)-[0-9a-f]{8}$")
    || (args.Length == 2 && args[1] != "--verbose")
)
{
    Console.Error.WriteLine(
        "Pass a fresh disposable abox-issue5-local|published-<8 hex> sandbox, optionally --verbose."
    );
    return 2;
}

var sandbox = args[0];
var verbose = args.Length == 2;
var payload = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "synthetic.json"));
var bytes = Encoding.UTF8.GetBytes(payload);
var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
var frame = $"{bytes.Length}\n{digest}\n{payload}";
const string receiver = "/tmp/abox-issue5-receive-framed.sh";
const string file = "$HOME/.microsoft/usersecrets/abox-issue5-synthetic-only/secrets.json";

try
{
    await Expect("application before transfer", ["/tmp/abox-issue5/Probe"], null, 42);
    await Expect("UTF-8 framed transfer", ["sh", receiver], frame, 0);
    await Expect("application after transfer", ["/tmp/abox-issue5/Probe"], null, 0);
    await Expect("short frame rejected", ["sh", receiver], frame[..^5], 65);
    await Expect("application after short frame", ["/tmp/abox-issue5/Probe"], null, 0);
    var corrupt = frame[..^2] + "x\n";
    await Expect("corrupt frame rejected", ["sh", receiver], corrupt, 65);
    await Expect("application after corrupt frame", ["/tmp/abox-issue5/Probe"], null, 0);
    await Expect(
        "bytes and restrictive ownership/modes",
        [
            "sh",
            "-c",
            $"test \"$(sha256sum \"{file}\" | cut -d ' ' -f 1)\" = \"$1\" && test \"$(stat -c '%a:%u' \"{file}\")\" = \"600:$(id -u)\" && test \"$(stat -c '%a:%u' \"$(dirname \"{file}\")\")\" = \"700:$(id -u)\"",
            "_",
            digest,
        ],
        null,
        0
    );
    await Expect(
        "scalar failure output suppressed",
        [
            "python3",
            "-c",
            "import json,sys; v=json.load(sys.stdin)['Probe:Marker']; print(v); print(v.encode('unicode_escape'),file=sys.stderr); sys.exit(9)",
        ],
        payload,
        9
    );
    Console.WriteLine(
        $"Synthetic sbx transport verified; verbose={verbose}; child output suppressed."
    );
    return 0;
}
catch (Exception)
{
    // Neither exception messages nor child diagnostics are safe at this seam.
    Console.Error.WriteLine(
        "Synthetic sbx transport verification failed; child output suppressed."
    );
    return 1;
}

async Task Expect(string label, string[] command, string? input, int expected)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var info = new ProcessStartInfo("sbx")
    {
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardInputEncoding = new UTF8Encoding(false),
    };
    foreach (var argument in new[] { "exec", "-i", sandbox }.Concat(command))
        info.ArgumentList.Add(argument);
    using var process = new Process { StartInfo = info };
    process.Start();
    // Drain without retaining payload-bearing output or passing it to a logger.
    var stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
    var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
    try
    {
        if (input is not null)
            await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
        process.StandardInput.Close();
        await process.WaitForExitAsync(timeout.Token);
        await Task.WhenAll(stdout, stderr);
        if (process.ExitCode != expected)
        {
            Console.Error.WriteLine(
                $"{label}: expected exit {expected}, observed {process.ExitCode}."
            );
            throw new InvalidOperationException();
        }
        Console.WriteLine($"{label}: exit {expected} verified.");
    }
    finally
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }
}
