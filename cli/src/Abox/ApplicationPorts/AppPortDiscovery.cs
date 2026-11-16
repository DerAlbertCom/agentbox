namespace Abox.ApplicationPorts;

internal sealed record AppPortOccurrence(string SourceFile, string Profile, int Port);

internal sealed record AppPortDiscoveryPlan(
    IReadOnlyList<AppPortOccurrence> Occurrences,
    bool HasDotnetProjects
);

internal static class AppPortDiscovery
{
    internal static async Task<AppPortDiscoveryPlan> DiscoverAsync(
        string location,
        CancellationToken token
    )
    {
        var result = new List<AppPortOccurrence>();
        var dotnet = false;
        foreach (var file in BoundedDiscoveryFiles.Enumerate(location, token))
        {
            var source = Path.GetRelativePath(location, file);
            try
            {
                var content = await DiscoveryFileContent.ReadAsync(file, token);
                if (StaticAspNetProject.IsProject(file))
                    // Do not short-circuit: every matching input must be validated.
                    dotnet |= await StaticAspNetProject.IsApplicableAsync(content, source, token);
                else
                    AppPortFormats.AddPorts(content, source, result);
            }
            catch (Exception exception) when (DiscoveryDiagnostics.IsInputFailure(exception))
            {
                throw DiscoveryDiagnostics.CannotRead(source);
            }
        }
        return new AppPortDiscoveryPlan(result, dotnet);
    }
}
