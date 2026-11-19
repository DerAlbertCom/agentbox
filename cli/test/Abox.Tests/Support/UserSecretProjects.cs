namespace Abox.Tests;

internal static class UserSecretProjects
{
    internal static void Write(
        TempDirectory root,
        string projectFile,
        string storeId,
        string? settingsJson
    )
    {
        root.File(
            projectFile,
            $"<Project><PropertyGroup><UserSecretsId>{storeId}</UserSecretsId></PropertyGroup></Project>"
        );
        if (settingsJson is not null)
            root.File(
                Path.Combine(Path.GetDirectoryName(projectFile) ?? "", "appsettings.json"),
                settingsJson
            );
    }
}
