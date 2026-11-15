using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;

if (args.Length != 3)
    throw new ArgumentException(
        "Usage: Discovery39 <before-abox.dll> <after-abox.dll> <scratch-directory>"
    );

var beforePath = Path.GetFullPath(args[0]);
var afterPath = Path.GetFullPath(args[1]);
if (!File.Exists(beforePath) || !File.Exists(afterPath))
    throw new ArgumentException("Build both production assemblies before running the benchmark.");
var workspace = Path.Combine(
    Path.GetFullPath(args[2]),
    "discovery-39-" + Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(workspace);
try
{
    CreateSnapshot(workspace);
    var originals = SnapshotHash(workspace);
    var allDirectories = Directory.GetDirectories(workspace, "*", SearchOption.AllDirectories);
    var allProjects = Directory
        .GetFiles(workspace, "*.csproj", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(workspace, path).Replace('\\', '/'))
        .Order(StringComparer.Ordinal)
        .ToArray();
    var includedProjects = allProjects.Where(path => !IsDependency(path)).ToArray();
    Console.WriteLine(
        $"Platform: {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}; {RuntimeInformation.FrameworkDescription}"
    );
    Console.WriteLine($"Before assembly SHA256: {FileHash(beforePath)}");
    Console.WriteLine($"After assembly SHA256: {FileHash(afterPath)}");
    Console.WriteLine(
        $"Snapshot: {allDirectories.Length + 1} directories including root; {allProjects.Length} projects; SHA256 {originals}"
    );
    Console.WriteLine(
        $"Reachable ordinary directory inventory (not syscall counts): before {allDirectories.Length + 1}, after {allDirectories.Count(path => !IsDependency(Path.GetRelativePath(workspace, path))) + 1}"
    );
    Console.WriteLine(
        $"Expected project sets: before {allProjects.Length}, after {includedProjects.Length}; included SHA256 {Fingerprint(includedProjects)}"
    );
    Console.WriteLine(
        "Intentional removals: " + string.Join(", ", allProjects.Except(includedProjects))
    );

    using var before = new DiscoveryProbe(beforePath, workspace);
    using var after = new DiscoveryProbe(afterPath, workspace);
    await before.Initialize();
    await after.Initialize();
    // Warm both versions on exactly the same synthetic snapshot. Build/startup are outside timing.
    Check((await before.Read()).Projects, allProjects);
    Check((await after.Read()).Projects, includedProjects);
    var beforeTimes = new List<double>();
    var afterTimes = new List<double>();
    for (var iteration = 0; iteration < 5; iteration++)
    {
        foreach (var baseline in iteration % 2 == 0 ? new[] { true, false } : new[] { false, true })
        {
            var sample = await (baseline ? before : after).Read();
            Check(sample.Projects, baseline ? allProjects : includedProjects);
            (baseline ? beforeTimes : afterTimes).Add(sample.Milliseconds);
            Console.WriteLine(
                $"{(baseline ? "before" : "after")} sample {iteration + 1}: {sample.Milliseconds:F2} ms; {sample.Projects.Length} projects"
            );
        }
    }
    Console.WriteLine(
        $"Medians: before {beforeTimes.Order().ElementAt(2):F2} ms; after {afterTimes.Order().ElementAt(2):F2} ms"
    );
    if (SnapshotHash(workspace) != originals)
        throw new InvalidOperationException("Synthetic source snapshot changed during discovery.");
    Console.WriteLine(
        "PASS: all samples match expected sets, no differences outside node_modules, source snapshot unchanged."
    );
}
finally
{
    // Only the uniquely owned synthetic child is removed, never the supplied scratch root.
    Directory.Delete(workspace, true);
}

static void CreateSnapshot(string root)
{
    File.WriteAllText(
        Path.Combine(root, "abox.json"),
        """{"userSecrets":["benchmark-39-no-real-store"]}"""
    );
    for (var index = 0; index < 1200; index++)
    {
        var path = Path.Combine(
            root,
            "src",
            $"area-{index / 30:D2}",
            $"project-{index:D4}",
            "settings"
        );
        Directory.CreateDirectory(path);
        if (index < 400)
            Project(path);
    }
    for (var index = 0; index < 2600; index++)
    {
        var path = Path.Combine(
            root,
            "web",
            "node_modules",
            $"package-{index:D4}",
            "lib",
            "nested"
        );
        Directory.CreateDirectory(path);
        if (index < 10)
            Project(path);
    }
    Project(Path.Combine(root, "web", "nested", "NoDe_MoDuLeS", "package"));
    Project(Path.Combine(root, "web", "node_modules", "package-0000", "node_modules", "child"));
    Project(Path.Combine(root, "node_modules-backup", "Api"));
    Project(Path.Combine(root, "my_node_modules", "Api"));
}

static void Project(string directory)
{
    Directory.CreateDirectory(directory);
    File.WriteAllText(
        Path.Combine(directory, "Synthetic.csproj"),
        "<Project><PropertyGroup><UserSecretsId>benchmark-39-no-real-store</UserSecretsId></PropertyGroup></Project>"
    );
    File.WriteAllText(Path.Combine(directory, "appsettings.json"), "{}");
}

static bool IsDependency(string relative) =>
    relative
        .Replace('\\', '/')
        .Split('/')
        .Any(part => part.Equals("node_modules", StringComparison.OrdinalIgnoreCase));

static string FileHash(string path) =>
    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

static string Fingerprint(IEnumerable<string> lines) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines))));

static string SnapshotHash(string root) =>
    Fingerprint(
        Directory
            .GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path =>
                (Relative: Path.GetRelativePath(root, path).Replace('\\', '/'), Path: path)
            )
            .OrderBy(file => file.Relative, StringComparer.Ordinal)
            .Select(file => file.Relative + ":" + FileHash(file.Path))
    );

static void Check(string[] actual, string[] expected)
{
    if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        throw new InvalidOperationException(
            "Discovery project set differs from the independently generated snapshot inventory."
        );
}

// Diagnostic-only reflection over existing internals; no product testing API or command invocation.
sealed class DiscoveryProbe : AssemblyLoadContext, IDisposable
{
    private readonly AssemblyDependencyResolver resolver;
    private readonly Assembly assembly;
    private readonly string workspace;
    private object? selections;

    internal DiscoveryProbe(string assemblyPath, string workspace)
        : base(isCollectible: true)
    {
        resolver = new AssemblyDependencyResolver(assemblyPath);
        assembly = LoadFromAssemblyPath(assemblyPath);
        this.workspace = workspace;
    }

    protected override Assembly? Load(AssemblyName name) =>
        resolver.ResolveAssemblyToPath(name) is { } path ? LoadFromAssemblyPath(path) : null;

    internal async Task Initialize()
    {
        var type = assembly.GetType("Abox.Configuration.FolderConfig", throwOnError: true)!;
        var task = (Task)
            type.GetMethod("LoadAsync")!.Invoke(null, [workspace, CancellationToken.None])!;
        await task;
        var config = task.GetType().GetProperty("Result")!.GetValue(task)!;
        selections = type.GetProperty("UserSecrets")!.GetValue(config);
    }

    internal async Task<(double Milliseconds, string[] Projects)> Read()
    {
        var type = assembly.GetType("Abox.UserSecrets.ProjectSettings", throwOnError: true)!;
        var read = type.GetMethod("ReadAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var stopwatch = Stopwatch.StartNew();
        var task = (Task)read.Invoke(null, [workspace, selections, CancellationToken.None])!;
        await task;
        stopwatch.Stop();
        var projects = ((IEnumerable)task.GetType().GetProperty("Result")!.GetValue(task)!)
            .Cast<object>()
            .Select(project =>
                (string)
                    type.GetProperty("Project", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .GetValue(project)!
            )
            .Select(path => path.Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        return (stopwatch.Elapsed.TotalMilliseconds, projects);
    }

    public void Dispose() => Unload();
}
