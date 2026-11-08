namespace Abox.Launchers;

/// <summary>
/// The launchers abox knows.
/// </summary>
public sealed class LauncherRegistry
{
    private readonly IReadOnlyDictionary<string, ILauncher> _launchers;

    public LauncherRegistry(IEnumerable<ILauncher> launchers)
    {
        _launchers = launchers.ToDictionary(launcher => launcher.Id, StringComparer.Ordinal);
        All = launchers.ToList();
    }

    /// <summary>All launchers.</summary>
    public IReadOnlyList<ILauncher> All { get; }

    /// <summary>The launcher with this id.</summary>
    public ILauncher Get(string id)
    {
        if (_launchers.TryGetValue(id, out var launcher))
        {
            return launcher;
        }

        var known = string.Join(", ", All.Select(launcher => launcher.Id));
        throw new AboxException($"There is no launcher '{id}'. Known launchers: {known}.");
    }
}
