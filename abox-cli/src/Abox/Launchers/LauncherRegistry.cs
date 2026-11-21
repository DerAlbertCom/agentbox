namespace Abox.Launchers;

/// <summary>
/// The launchers abox knows.
/// </summary>
public sealed class LauncherRegistry
{
    private readonly IReadOnlyDictionary<string, ILauncher> _launchers;

    public LauncherRegistry(IEnumerable<ILauncher> launchers)
    {
        All = launchers.ToList();
        var byId = new Dictionary<string, ILauncher>(StringComparer.Ordinal);
        foreach (var launcher in All)
        {
            if (!byId.TryAdd(launcher.Id, launcher))
            {
                throw new AboxException($"There is a duplicate launcher id '{launcher.Id}'.");
            }
        }

        _launchers = byId;
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
