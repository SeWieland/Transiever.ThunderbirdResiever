namespace Transiever.ThunderbirdResiever.UnitTest;

internal sealed class TestDirectory : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"tbrx-tests-{Guid.NewGuid():N}");

    public TestDirectory() => Directory.CreateDirectory(root);

    public string CreateDirectory(string relative)
    {
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(path);
        return path;
    }

    public string CreateDirectoryLink(string relative, string target)
    {
        string link = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        Directory.CreateSymbolicLink(link, target);
        return link;
    }

    public string Write(string relative, string content)
    {
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
        return path;
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
