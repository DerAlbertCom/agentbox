using System.Xml;
using System.Xml.Linq;

namespace Abox.ApplicationPorts;

/// <summary>Classifies declarations without evaluating imports, MSBuild or project tools.</summary>
internal static class StaticAspNetProject
{
    internal static bool IsProject(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".csproj" or ".fsproj" or ".vbproj";

    internal static async Task<bool> IsApplicableAsync(
        DiscoveryFileContent content,
        string source,
        CancellationToken token
    )
    {
        using var text = new StreamReader(
            new MemoryStream(content.Bytes, 0, content.Count),
            detectEncodingFromByteOrderMarks: true
        );
        var declaration = await text.ReadToEndAsync(token);
        // Only successfully read bytes can establish an unrelated project. These markers
        // select the conservative error policy, not successful ASP.NET classification.
        var explicitlyMentionsAspNet =
            declaration.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase)
            || declaration.Contains("Microsoft.AspNetCore.App", StringComparison.OrdinalIgnoreCase);
        if (content.ExceedsLimit)
        {
            if (!explicitlyMentionsAspNet)
                return false;
            throw DiscoveryDiagnostics.FileSizeLimit(source);
        }
        try
        {
            return HasAspNetDeclaration(content);
        }
        catch (Exception exception)
            when (!explicitlyMentionsAspNet && DiscoveryDiagnostics.IsInputFailure(exception))
        {
            return false;
        }
    }

    private static bool HasAspNetDeclaration(DiscoveryFileContent content)
    {
        using var stream = new MemoryStream(
            content.Bytes,
            content.Utf8BomLength,
            content.Count - content.Utf8BomLength
        );
        using var reader = XmlReader.Create(
            stream,
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }
        );
        var project = XDocument.Load(reader).Root;
        if (project?.Name.LocalName != "Project")
            return false;
        return ((string?)project.Attribute("Sdk"))
                ?.Split(';', StringSplitOptions.TrimEntries)
                .Any(IsWebSdk) == true
            || project
                .Elements()
                .Any(element =>
                    element.Name.LocalName == "Sdk" && IsWebSdk((string?)element.Attribute("Name"))
                )
            || project
                .Descendants()
                .Any(element =>
                    element.Name.LocalName == "FrameworkReference"
                    && string.Equals(
                        (string?)element.Attribute("Include"),
                        "Microsoft.AspNetCore.App",
                        StringComparison.OrdinalIgnoreCase
                    )
                );
    }

    private static bool IsWebSdk(string? value) =>
        value?.Split('/')[0].Equals("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase)
        == true;
}
