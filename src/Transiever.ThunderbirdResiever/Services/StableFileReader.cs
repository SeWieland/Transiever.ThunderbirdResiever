namespace Transiever.ThunderbirdResiever.Services;

public sealed class StableFileReader : IStableFileReader
{
    private readonly Action<string>? afterFirstRead;

    public StableFileReader()
    {
    }

    internal StableFileReader(Action<string> afterFirstRead) =>
        this.afterFirstRead = afterFirstRead;

    public byte[] ReadAllBytes(string path)
    {
        FileSnapshot before = Snapshot(path);
        byte[] first = ReadOnce(path, before.Length);
        afterFirstRead?.Invoke(path);
        FileSnapshot middle = Snapshot(path);
        EnsureSame(before, middle, path);
        byte[] second = ReadOnce(path, middle.Length);
        FileSnapshot after = Snapshot(path);
        EnsureSame(middle, after, path);
        if (!first.AsSpan().SequenceEqual(second))
            throw Changed(path);
        return first;
    }

    private readonly record struct FileSnapshot(long Length, DateTime LastWriteUtc);

    private static FileSnapshot Snapshot(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
            throw new FileNotFoundException("Thunderbird source file was not found.", path);
        return new FileSnapshot(file.Length, file.LastWriteTimeUtc);
    }

    private static byte[] ReadOnce(string path, long expectedLength)
    {
        if (expectedLength > int.MaxValue)
            throw new IOException("Thunderbird source file is too large to read safely.");

        using (var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete))
        {
            if (stream.Length != expectedLength)
                throw Changed(path);

            byte[] bytes = new byte[checked((int)expectedLength)];
            stream.ReadExactly(bytes);
            return bytes;
        }
    }

    private static void EnsureSame(FileSnapshot expected, FileSnapshot actual, string path)
    {
        if (expected.Length != actual.Length || expected.LastWriteUtc != actual.LastWriteUtc)
            throw Changed(path);
    }

    private static IOException Changed(string path) =>
        new(
            $"Thunderbird source file changed while it was being read: {path}");
}
