using System.Text.Json;
using System.Xml;

namespace Abox.ApplicationPorts;

internal static class DiscoveryDiagnostics
{
    internal static bool IsInputFailure(Exception exception) =>
        exception
            is JsonException
                or XmlException
                or InvalidOperationException
                or FormatException
                or IOException
                or UnauthorizedAccessException;

    internal static AboxException FileSizeLimit(string source) =>
        new(
            $"Application port discovery file-size limit (1 MiB) exceeded by {JsonSerializer.Serialize(source)}. Reduce the file and retry; no ports were published."
        );

    internal static AboxException CannotScan(string directory) =>
        new(
            $"Cannot scan {JsonSerializer.Serialize(directory)} for application ports. Check the --location and directory permissions; no ports were published."
        );

    internal static AboxException CannotRead(string source)
    {
        var hint = StaticAspNetProject.IsProject(source)
            ? "use readable ordinary project XML with an explicit Microsoft.NET.Sdk.Web SDK or Microsoft.AspNetCore.App FrameworkReference; project imports and MSBuild are never evaluated"
            : Path.GetFileName(source) switch
            {
                "angular.json" =>
                    "use object projects and serve options/named configurations with literal integer port values 1..65535; define serve in architect or targets, not both",
                "package.json" =>
                    "use an object scripts and a literal start script: ng serve (or ng s / ng dev) with one --port N or --port=N, N in 1..65535; remove shell operators, variables and escapes, or configure the port in angular.json and remove dynamic --port flags",
                _ =>
                    "use object profiles and semicolon-separated absolute HTTP(S) applicationUrl strings with literal ports 1..65535",
            };
        return new AboxException(
            $"Cannot discover application ports in {JsonSerializer.Serialize(source)}. Fix file access, JSON structure or port configuration: {hint}. No ports were published."
        );
    }
}
