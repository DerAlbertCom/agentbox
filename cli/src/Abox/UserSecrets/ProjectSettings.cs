using Abox.Configuration;

namespace Abox.UserSecrets;

// Static, workspace-local configuration only. Never invoke MSBuild or application startup.
internal sealed class ProjectSettings(
    string project,
    UserSecretSnapshot selectedSettings,
    IReadOnlyDictionary<string, string?> allSettings,
    bool hasAppSettingsSource
)
{
    internal string Project { get; } = project;
    internal UserSecretSnapshot SelectedSettings { get; } = selectedSettings;

    // Eligibility metadata may use unselected appsettings keys; output must use SelectedSettings.
    internal IReadOnlyDictionary<string, string?> AllSettings { get; } = allSettings;
    internal bool HasAppSettingsSource { get; } = hasAppSettingsSource;

    internal static async Task<IReadOnlyList<ProjectSettings>> ReadAsync(
        string workspace,
        IReadOnlyList<UserSecretSelection> selections,
        CancellationToken cancellationToken
    )
    {
        var result = new List<ProjectSettings>();
        if (selections.Count == 0)
            return result;

        var (root, projects) = WorkspaceProjectFiles.Read(workspace, cancellationToken);
        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selection = SelectedProjectAssociation.TryRead(root, project, selections);
            if (selection is null)
                continue;
            var settings = new EffectiveAppSettings(root, project, selection);
            result.Add(await settings.ReadAsync(cancellationToken));
        }
        return result;
    }
}
