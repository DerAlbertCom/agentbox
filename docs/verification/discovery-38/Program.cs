using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

if (args.Length != 3)
    throw new ArgumentException(
        "Usage: Discovery38Probe <baseline-abox.dll> <candidate-abox.dll> <new-snapshot-directory>"
    );

var snapshot = Path.GetFullPath(args[2]);
if (Directory.Exists(snapshot) || File.Exists(snapshot))
    throw new ArgumentException(
        "Snapshot path must not exist: this probe only measures its own synthetic input."
    );
Directory.CreateDirectory(snapshot);
try
{
    // No host stores, configuration, MSBuild, sandbox operations or real workspace inputs.
    for (var project = 0; project < 430; project++)
    {
        var path = Path.Combine(snapshot, "sources", $"project-{project:D3}");
        for (var depth = 0; depth < 8; depth++)
            path = Path.Combine(path, $"level-{depth}");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, $"Project{project:D3}.csproj"), "<Project />");
    }
    for (var branch = 0; branch < 400; branch++)
    {
        var path = Path.Combine(snapshot, "node_modules", $"package-{branch:D3}");
        for (var depth = 0; depth < 10; depth++)
        {
            path = Path.Combine(path, $"level-{depth}");
            for (var leaf = 0; leaf < 10; leaf++)
                Directory.CreateDirectory(Path.Combine(path, $"leaf-{leaf}"));
        }
    }
    // These are excluded in both versions, independently of parent-path reuse.
    foreach (var excluded in new[] { ".git", "bin", "obj" })
    {
        var path = Path.Combine(snapshot, excluded);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "Excluded.csproj"), "<Project />");
    }
    var baseline = new Discovery(args[0]);
    var candidate = new Discovery(args[1]);
    var exclusions = new[] { ".git", "bin", "obj", "node_modules", "ordinary" };
    if (exclusions.Any(name => baseline.IsExcluded(name) != candidate.IsExcluded(name)))
        throw new InvalidOperationException(
            "Exclusion policies differ; compare ancestry reuse with unchanged exclusions only."
        );

    // Warm both implementations on the very same snapshot before alternating measured order.
    var expected = baseline.Run(snapshot);
    CheckEquivalent(expected, candidate.Run(snapshot));
    var timings = new List<object>();
    for (var iteration = 0; iteration < 5; iteration++)
    {
        foreach (
            var (name, discovery) in iteration % 2 == 0
                ? new[] { ("baseline", baseline), ("candidate", candidate) }
                : new[] { ("candidate", candidate), ("baseline", baseline) }
        )
        {
            var watch = Stopwatch.StartNew();
            var projects = discovery.Run(snapshot);
            watch.Stop();
            CheckEquivalent(expected, projects);
            timings.Add(
                new
                {
                    iteration = iteration + 1,
                    implementation = name,
                    milliseconds = watch.Elapsed.TotalMilliseconds,
                }
            );
        }
    }
    Console.WriteLine(
        JsonSerializer.Serialize(
            new
            {
                runtime = RuntimeInformation.FrameworkDescription,
                platform = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                directories = Directory
                    .EnumerateDirectories(snapshot, "*", SearchOption.AllDirectories)
                    .Count() + 1,
                projects = expected.Length,
                fingerprint = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', expected)))
                ),
                exclusions = exclusions.Where(baseline.IsExcluded).ToArray(),
                sameProjectSetEveryRun = true,
                timings,
            },
            new JsonSerializerOptions { WriteIndented = true }
        )
    );
}
finally
{
    Directory.Delete(snapshot, recursive: true);
}

static void CheckEquivalent(string[] expected, string[] actual)
{
    if (expected.Length != 430 || !expected.SequenceEqual(actual, StringComparer.Ordinal))
        throw new InvalidOperationException(
            "Discovered project sets differ from the 430-project synthetic snapshot."
        );
}

// Diagnostic-only binding to the existing discovery implementation; no production/public test API.
sealed class Discovery
{
    private readonly Func<string, string> resolve;
    private readonly Func<
        string,
        string,
        HashSet<string>,
        CancellationToken,
        IEnumerable<string>
    > files;
    internal Func<string, bool> IsExcluded { get; }

    internal Discovery(string assemblyPath)
    {
        var assembly = new AssemblyLoadContext(Guid.NewGuid().ToString()).LoadFromAssemblyPath(
            Path.GetFullPath(assemblyPath)
        );
        var type = assembly.GetType("Abox.UserSecrets.ProjectSettings", throwOnError: true)!;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        resolve = type.GetMethod("ResolveDirectory", flags, [typeof(string)])!
            .CreateDelegate<Func<string, string>>();
        files = type.GetMethod("ProjectFiles", flags)!
            .CreateDelegate<
                Func<string, string, HashSet<string>, CancellationToken, IEnumerable<string>>
            >();
        IsExcluded = type.GetMethod("IsExcludedDirectory", flags)!
            .CreateDelegate<Func<string, bool>>();
    }

    internal string[] Run(string snapshot)
    {
        var root = resolve(snapshot);
        return files(
                root,
                root,
                new HashSet<string>(
                    OperatingSystem.IsWindows()
                        ? StringComparer.OrdinalIgnoreCase
                        : StringComparer.Ordinal
                ),
                CancellationToken.None
            )
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
