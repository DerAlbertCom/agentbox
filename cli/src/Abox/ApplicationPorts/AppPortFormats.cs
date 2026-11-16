using System.Text.Json;

namespace Abox.ApplicationPorts;

internal static class AppPortFormats
{
    internal static void AddPorts(
        DiscoveryFileContent content,
        string source,
        List<AppPortOccurrence> result
    )
    {
        if (content.ExceedsLimit)
            throw DiscoveryDiagnostics.FileSizeLimit(source);
        using var document = JsonDocument.Parse(
            content.WithoutUtf8Bom,
            new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                AllowDuplicateProperties = false,
            }
        );
        switch (Path.GetFileName(source))
        {
            case "launchSettings.json":
                DotnetLaunchSettings.AddPorts(document.RootElement, source, result);
                break;
            case "angular.json":
                AngularConfiguration.AddPorts(document.RootElement, source, result);
                break;
            case "package.json":
                AngularStartScript.AddPorts(document.RootElement, source, result);
                break;
        }
    }
}
