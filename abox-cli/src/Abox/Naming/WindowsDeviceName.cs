namespace Abox.Naming;

/// <summary>Reserved Windows device names, including extensions and legacy port aliases.</summary>
internal static class WindowsDeviceName
{
    internal static bool IsReserved(string value)
    {
        var name = value.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || (
                name.Length == 4
                && (
                    name.StartsWith("COM", StringComparison.Ordinal)
                    || name.StartsWith("LPT", StringComparison.Ordinal)
                )
                && "0123456789¹²³".Contains(name[3])
            );
    }
}
