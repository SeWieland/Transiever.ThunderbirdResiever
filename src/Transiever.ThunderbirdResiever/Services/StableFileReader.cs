namespace Transiever.ThunderbirdResiever.Services;

public sealed class StableFileReader : IStableFileReader
{
    public byte[] ReadAllBytes(string path)
    {
        var before = new FileInfo(path);
        if (!before.Exists)
            throw new FileNotFoundException("Thunderbird source file was not found.", path);

        long length = before.Length;
        DateTime lastWriteUtc = before.LastWriteTimeUtc;
        byte[] bytes;
        using (var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete))
        {
            if (stream.Length > int.MaxValue)
                throw new IOException("Thunderbird source file is too large to read safely.");

            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }

        var after = new FileInfo(path);
        if (!after.Exists ||
            after.Length != length ||
            after.LastWriteTimeUtc != lastWriteUtc)
        {
            throw new IOException(
                $"Thunderbird source file changed while it was being read: {path}");
        }

        return bytes;
    }
}
