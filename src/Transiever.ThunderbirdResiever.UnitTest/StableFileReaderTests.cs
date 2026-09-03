using Transiever.ThunderbirdResiever.Services;

namespace Transiever.ThunderbirdResiever.UnitTest;

public sealed class StableFileReaderTests
{
    [Fact]
    public void ReadAllBytes_returns_exact_bytes_and_preserves_metadata_when_unchanged()
    {
        using var directory = new TestDirectory();
        string file = directory.Write("source.dat", "original");
        DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(file);
        FileAttributes attributes = File.GetAttributes(file);

        byte[] result = new StableFileReader().ReadAllBytes(file);

        Assert.Equal("original"u8.ToArray(), result);
        Assert.Equal(lastWriteTimeUtc, File.GetLastWriteTimeUtc(file));
        Assert.Equal(attributes, File.GetAttributes(file));
    }

    [Fact]
    public void ReadAllBytes_rejects_a_byte_appended_after_the_first_read()
    {
        using var directory = new TestDirectory();
        string file = directory.Write("source.dat", "original");
        var reader = new StableFileReader(path => File.AppendAllText(path, "!"));

        Assert.Throws<IOException>(() => reader.ReadAllBytes(file));
    }

    [Fact]
    public void ReadAllBytes_rejects_a_timestamp_changed_after_the_first_read()
    {
        using var directory = new TestDirectory();
        string file = directory.Write("source.dat", "original");
        DateTime changedTimeUtc = File.GetLastWriteTimeUtc(file).AddMinutes(1);
        var reader = new StableFileReader(path => File.SetLastWriteTimeUtc(path, changedTimeUtc));

        Assert.Throws<IOException>(() => reader.ReadAllBytes(file));
    }

    [Fact]
    public void ReadAllBytes_rejects_equal_length_bytes_even_when_the_timestamp_is_restored()
    {
        using var directory = new TestDirectory();
        string file = directory.Write("source.dat", "original");
        DateTime originalTimeUtc = File.GetLastWriteTimeUtc(file);
        var reader = new StableFileReader(path =>
        {
            File.WriteAllBytes(path, "changed!"u8.ToArray());
            File.SetLastWriteTimeUtc(path, originalTimeUtc);
        });

        Assert.Throws<IOException>(() => reader.ReadAllBytes(file));
    }

    [Fact]
    public void ReadAllBytes_rejects_a_source_deleted_after_the_first_read()
    {
        using var directory = new TestDirectory();
        string file = directory.Write("source.dat", "original");
        var reader = new StableFileReader(File.Delete);

        Assert.ThrowsAny<IOException>(() => reader.ReadAllBytes(file));
    }

}
