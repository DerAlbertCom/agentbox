namespace Abox.ApplicationPorts;

/// <summary>A bounded ordinary-file snapshot, including one byte to detect overflow.</summary>
internal sealed record DiscoveryFileContent(byte[] Bytes, int Count)
{
    private const int MaximumFileSize = 1024 * 1024;
    internal bool ExceedsLimit => Count == Bytes.Length;
    internal ReadOnlyMemory<byte> WithoutUtf8Bom =>
        Bytes.AsMemory(Utf8BomLength, Count - Utf8BomLength);
    internal int Utf8BomLength =>
        Count >= 3 && Bytes[0] == 0xef && Bytes[1] == 0xbb && Bytes[2] == 0xbf ? 3 : 0;

    internal static async Task<DiscoveryFileContent> ReadAsync(string file, CancellationToken token)
    {
        if ((File.GetAttributes(file) & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
            throw new IOException();
        await using var stream = File.OpenRead(file);
        var bytes = new byte[MaximumFileSize + 1];
        var count = await stream.ReadAtLeastAsync(
            bytes,
            bytes.Length,
            throwOnEndOfStream: false,
            token
        );
        return new DiscoveryFileContent(bytes, count);
    }
}
