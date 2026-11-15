using System.Text.Json;
using Abox.Infrastructure;

if (args.Length != 1 || !args[0].StartsWith("abox-issue5-", StringComparison.Ordinal))
{
    Console.Error.WriteLine("Pass the name of a disposable abox-issue5-* Docker container.");
    return 2;
}

const string scalar = "fixture-scalar-not-a-real-secret";
const string json = "{\"Probe:Marker\":\"fixture-scalar-not-a-real-secret\"}";
var diagnosticRequest = new ProcessRequest(
    "docker",
    [
        "exec",
        "-i",
        args[0],
        "python3",
        "-c",
        "import json,sys; body=sys.stdin.read(); sys.stdout.write(body); sys.stderr.write(json.loads(body)['Probe:Marker']); sys.exit(9)",
    ],
    StandardInput: json
);
var captured = await new ProcessRunner().RunAsync(diagnosticRequest, CancellationToken.None);
if (
    captured.ExitCode != 9
    || !captured.StandardOutput.Contains(scalar)
    || !captured.StandardError.Contains(scalar)
)
    throw new Exception("The synthetic diagnostic child did not produce the expected failure.");
foreach (var verbose in new[] { false, true })
{
    var diagnostics = CaptureDiagnostics(diagnosticRequest, captured, verbose);
    Console.WriteLine(
        $"verbose={verbose}; full-json-hidden={!diagnostics.Contains(json)}; scalar-diagnostic-visible={diagnostics.Contains(scalar)}"
    );
}
Console.WriteLine(
    $"raw-captured-result-retains-payload={captured.StandardOutput.Contains(scalar)}"
);

var payload = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "synthetic.json"));
await Transfer(payload);
var rawRead = await ReadProbe();
Console.WriteLine(
    $"raw-unicode-read-exit={rawRead.ExitCode}; console-stdin-codepage={Console.InputEncoding.CodePage}"
);

// Default JSON escaping makes a representative alternative to changing ProcessRunner.
var asciiPayload = JsonSerializer.Serialize(
    JsonSerializer.Deserialize<Dictionary<string, string>>(payload)
);
if (asciiPayload.Any(character => character > 127))
    throw new Exception("Expected ASCII-escaped serializer output.");
await Transfer(asciiPayload);
var escapedRead = await ReadProbe();
if (escapedRead.ExitCode != 0)
    throw new Exception("The .NET application did not read the synthetic values correctly.");
Console.WriteLine("ASCII-escaped Unicode payload read verified.");
return 0;

async Task Transfer(string input)
{
    var result = await new ProcessRunner().RunAsync(
        new ProcessRequest(
            "docker",
            ["exec", "-i", args[0], "sh", "/tmp/abox-issue5-receive.sh"],
            StandardInput: input
        ),
        CancellationToken.None
    );
    if (
        result.ExitCode != 0
        || (result.StandardOutput + result.StandardError).Contains("synthetic-only-")
    )
        throw new Exception("Synthetic transfer failed or echoed a fixture value.");
}

Task<ProcessResult> ReadProbe() =>
    new ProcessRunner().RunAsync(
        new ProcessRequest("docker", ["exec", args[0], "/opt/abox-issue5/Probe"]),
        CancellationToken.None
    );

static string CaptureDiagnostics(ProcessRequest request, ProcessResult result, bool verbose)
{
    var previous = Console.Error;
    using var capture = new StringWriter();
    try
    {
        Console.SetError(capture);
        new ProcessOutput(new SystemConsole()) { Verbose = verbose }.Write(request, result);
        return capture.ToString();
    }
    finally
    {
        Console.SetError(previous);
    }
}
