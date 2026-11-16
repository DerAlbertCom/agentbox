using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Abox.Configuration;
using Abox.Naming;

namespace Abox.UserSecrets;

// Only readable XML explicitly naming a selected ID can establish an association.
internal static class SelectedProjectAssociation
{
    internal static UserSecretSelection? TryRead(
        string root,
        string project,
        IReadOnlyList<UserSecretSelection> selections
    )
    {
        try
        {
            WorkspaceProjectFiles.EnsureOrdinaryFile(project);
            using var reader = XmlReader.Create(
                project,
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }
            );
            return Resolve(XDocument.Load(reader), root, project, selections);
        }
        catch (Exception exception)
            when (exception is XmlException or IOException or UnauthorizedAccessException)
        {
            // Unreadable/linked XML cannot establish an explicit selected-ID association.
            return null;
        }
    }

    private static UserSecretSelection? Resolve(
        XDocument document,
        string root,
        string project,
        IReadOnlyList<UserSecretSelection> selections
    )
    {
        var declarations = document
            .Descendants()
            .Where(element => element.Name.LocalName == "UserSecretsId")
            .ToArray();
        var selection = selections.FirstOrDefault(selection =>
            declarations.Any(declaration =>
                selection.Id.Equals(declaration.Value.Trim(), StringComparison.OrdinalIgnoreCase)
            )
        );
        if (selection is null)
            return null;

        // Validate only after explicit matching, including malformed/nested declarations.
        var declaration = declarations.Length == 1 ? declarations[0] : null;
        if (
            declaration is null
            || !IsUnconditionalLiteral(declaration)
            || !IsTopLevelProperty(document, declaration)
        )
        {
            throw new AboxException(
                $"Project {JsonSerializer.Serialize(Path.GetRelativePath(root, project))}, store '{selection.Id}': matching UserSecretsId must be one unconditional literal declaration in a top-level PropertyGroup. Fix conditional, multiple or malformed declarations; MSBuild is never evaluated. No stores were copied."
            );
        }
        return selection;
    }

    private static bool IsUnconditionalLiteral(XElement declaration) =>
        !declaration.HasElements
        && !declaration.HasAttributes
        && UserSecretId.IsValid(declaration.Value.Trim())
        && !declaration
            .AncestorsAndSelf()
            .Any(element => element.Attribute("Condition") is not null);

    private static bool IsTopLevelProperty(XDocument document, XElement declaration) =>
        document.Root?.Name.LocalName == "Project"
        && declaration.Parent?.Name.LocalName == "PropertyGroup"
        && declaration.Parent.Parent == document.Root;
}
