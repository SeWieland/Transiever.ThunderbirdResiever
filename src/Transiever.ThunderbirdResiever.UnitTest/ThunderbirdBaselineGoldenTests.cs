using System.Security.Cryptography;
using Transiever.SieveRuler.Services;
using Transiever.ThunderbirdResiever.Application;
using Transiever.ThunderbirdResiever.Services;

namespace Transiever.ThunderbirdResiever.UnitTest;

public sealed class ThunderbirdBaselineGoldenTests
{
    private const string Tbe001SourceSha256 = "A4E7206482729ACCA75CB1F9A28B615397C5D2340FBC8AA6F3E1B9CE90F21792";

    [Fact]
    public async Task Tbe001_ExportsExactCanonicalDocument()
    {
        string sourceFile = Fixture("TBE-001.filters.dat");
        string actualFile = Path.Combine(
            Path.GetTempPath(),
            $"tbe-001-{Guid.NewGuid():N}.rules.json");
        try
        {
            byte[] sourceBytes = await File.ReadAllBytesAsync(
                sourceFile,
                TestContext.Current.CancellationToken);
            Assert.Equal(Tbe001SourceSha256, Hash(sourceBytes));

            ThunderbirdExportResult result = await CreateApplication().ExportAsync(
                new ThunderbirdExportRequest(Source(sourceFile), actualFile),
                TestContext.Current.CancellationToken);

            Assert.Equal((4, 0), (result.EnabledRuleCount, result.SkippedEnabledRuleCount));
            Assert.Equal(
                await File.ReadAllBytesAsync(
                    Fixture("TBE-001.rules.json"),
                    TestContext.Current.CancellationToken),
                await File.ReadAllBytesAsync(
                    actualFile,
                    TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(actualFile);
        }
    }

    [Fact]
    public async Task Malformed_export_does_not_create_target_file()
    {
        using var directory = new TestDirectory();
        string sourceFile = directory.Write("msgFilterRules.dat", """
            version="8"
            logging="no"
            """);
        string targetFile = Path.Combine(
            Path.GetDirectoryName(sourceFile)!,
            "malformed.rules.json");
        var application = CreateApplication();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => application.ExportAsync(
                new ThunderbirdExportRequest(Source(sourceFile), targetFile),
                TestContext.Current.CancellationToken));
        Assert.False(File.Exists(targetFile));
    }

    private static ThunderbirdExportApplication CreateApplication() =>
        new(
            new ThunderbirdProfileLocator(Array.Empty<string>()),
            new ThunderbirdRuleExporter(),
            new JsonRuleSerializer());

    private static ThunderbirdRuleSource Source(string filters) =>
        new("profile", filters, "account1", "server1", "imap", "imap.example.invalid", "user@example.invalid");

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "ThunderbirdV1", name);

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}
