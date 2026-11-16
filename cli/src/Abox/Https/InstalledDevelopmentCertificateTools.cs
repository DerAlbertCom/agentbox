using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using Abox.Infrastructure;

namespace Abox.Https;

/// <summary>Inspects installed SDK tool metadata; never loads an assembly or installs a tool.</summary>
internal sealed class InstalledDevelopmentCertificateTools(IProcessRunner runner)
{
    internal async Task<IReadOnlyList<DevelopmentCertificateTool>> FindAsync(
        CancellationToken token
    )
    {
        var result = await runner.RunAsync(
            new ProcessRequest(
                "dotnet",
                ["--list-sdks"],
                StandardInput: "",
                OutputPolicy: ProcessOutputPolicy.CapturedSensitive,
                Environment: new Dictionary<string, string>
                {
                    ["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false",
                }
            ),
            token
        );
        if (!result.Succeeded)
            throw new InvalidOperationException("Installed SDK listing failed.");
        var found = new List<DevelopmentCertificateTool>();
        foreach (var path in ToolPaths(result.StandardOutput))
            found.AddRange(
                ReadCertificateFormats(path)
                    .Select(format => new DevelopmentCertificateTool(path, format))
            );
        return found;
    }

    private static IEnumerable<string> ToolPaths(string listing)
    {
        foreach (var line in listing.Split('\n').Reverse())
        {
            var match = Regex.Match(
                line.Trim(),
                @"^(?<version>[0-9][0-9A-Za-z.\-+]*) \[(?<root>.+)\]$"
            );
            if (!match.Success)
                continue;
            var root = Path.Combine(
                match.Groups["root"].Value,
                match.Groups["version"].Value,
                "DotnetTools",
                "dotnet-dev-certs"
            );
            if (!Directory.Exists(root))
                continue;
            foreach (
                var path in Directory.EnumerateFiles(
                    root,
                    "dotnet-dev-certs.dll",
                    SearchOption.AllDirectories
                )
            )
                yield return path;
        }
    }

    private static IEnumerable<int> ReadCertificateFormats(string path)
    {
        // Read the installed tool's literal without loading/executing it.
        // Unknown layouts/formats fail closed instead of guessing SDK major.
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        foreach (var handle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(handle);
            if (
                metadata.GetString(type.Name) != "CertificateManager"
                || metadata.GetString(type.Namespace)
                    != "Microsoft.AspNetCore.Certificates.Generation"
            )
                continue;
            foreach (var format in ReadFormatConstants(metadata, type))
                yield return format;
        }
    }

    private static IEnumerable<int> ReadFormatConstants(
        MetadataReader metadata,
        TypeDefinition type
    )
    {
        foreach (var handle in type.GetFields())
        {
            var field = metadata.GetFieldDefinition(handle);
            if (
                metadata.GetString(field.Name) != "CurrentAspNetCoreCertificateVersion"
                || field.GetDefaultValue().IsNil
            )
                continue;
            var constant = metadata.GetConstant(field.GetDefaultValue());
            if (constant.TypeCode != ConstantTypeCode.Int32)
                continue;
            var format = metadata.GetBlobReader(constant.Value).ReadInt32();
            if (format is >= 1 and <= 255)
                yield return format;
        }
    }
}

internal sealed record DevelopmentCertificateTool(string Path, int FormatVersion);
