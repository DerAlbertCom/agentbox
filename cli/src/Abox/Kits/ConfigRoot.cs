namespace Abox.Kits;

/// <summary>
/// A configuration file and the directory its kits and workloads are resolved against.
/// </summary>
/// <param name="Directory">The directory the configuration lives in.</param>
/// <param name="Name">The name of the configuration, without the file extension.</param>
public sealed record ConfigRoot(string Directory, string Name);
