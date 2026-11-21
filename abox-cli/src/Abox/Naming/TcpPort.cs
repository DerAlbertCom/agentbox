using System.Globalization;

namespace Abox.Naming;

/// <summary>A parsed TCP port; callers retain their own diagnostics for invalid input.</summary>
internal readonly record struct TcpPort
{
    private TcpPort(int value) => Value = value;

    internal int Value { get; }

    internal static bool IsValid(long value) => value is >= 1 and <= 65535;

    internal static bool TryParse(ReadOnlySpan<char> text, out TcpPort port)
    {
        if (
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            && IsValid(value)
        )
        {
            port = new TcpPort(value);
            return true;
        }
        port = default;
        return false;
    }
}
