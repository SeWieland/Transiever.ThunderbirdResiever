using Transiever.ThunderbirdResiever.Services;

namespace Transiever.ThunderbirdResiever.UnitTest;

public sealed class ThunderbirdProfileLocatorTests
{
    [Fact]
    public void Discover_reads_profiles_ini_and_resolves_account_identity()
    {
        using var directory = new TestDirectory();
        string root = directory.CreateDirectory("root");
        string profile = CreateProfile(directory, "root/Profiles/default", "imap", "imap.example.com", "user@example.com");
        directory.Write("root/profiles.ini", """
            [Profile0]
            Name=default
            IsRelative=1
            Path=Profiles/default
            Default=1
            """);

        ThunderbirdSourceDiscoveryResult result = new ThunderbirdProfileLocator([root]).Discover(new());

        ThunderbirdRuleSource source = Assert.Single(result.Sources);
        Assert.Equal(Path.GetFullPath(profile), source.ProfileDirectory);
        Assert.Equal("account1", source.AccountKey);
        Assert.Equal("server1", source.ServerKey);
        Assert.StartsWith("thunderbird-", source.SourceId);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Discover_supports_multiple_standard_layout_roots()
    {
        using var directory = new TestDirectory();
        var roots = new List<string>();
        foreach (string layout in new[] { "windows", "linux", "flatpak", "snap" })
        {
            string root = directory.CreateDirectory(layout);
            roots.Add(root);
            _ = CreateProfile(directory, $"{layout}/profile", "imap", $"{layout}.example.com", $"{layout}@example.com");
            directory.Write($"{layout}/profiles.ini", "[Profile0]\nIsRelative=1\nPath=profile\n");
        }

        ThunderbirdSourceDiscoveryResult result = new ThunderbirdProfileLocator(roots).Discover(new());

        Assert.Equal(4, result.Sources.Count);
        Assert.Equal(4, result.Sources.Select(source => source.SourceId).Distinct().Count());
    }

    [Fact]
    public void Discover_supports_explicit_relocated_profile_and_filter()
    {
        using var directory = new TestDirectory();
        string profile = CreateProfile(directory, "relocated", "imap", "imap.example.com", "user@example.com");
        string filters = Path.Combine(profile, "ImapMail", "server1", "msgFilterRules.dat");

        ThunderbirdSourceDiscoveryResult result = new ThunderbirdProfileLocator([]).Discover(
            new ThunderbirdSourceRequest(profile, filters));

        Assert.Equal(Path.GetFullPath(filters), Assert.Single(result.Sources).FiltersFile);
    }

    [Fact]
    public void Discover_reports_pop_and_missing_prefs()
    {
        using var directory = new TestDirectory();
        string root = directory.CreateDirectory("root");
        _ = CreateProfile(directory, "root/pop", "pop3", "pop.example.com", "user@example.com");
        directory.CreateDirectory("root/missing");
        directory.Write("root/profiles.ini", """
            [Profile0]
            IsRelative=1
            Path=pop
            [Profile1]
            IsRelative=1
            Path=missing
            """);

        ThunderbirdSourceDiscoveryResult result = new ThunderbirdProfileLocator([root]).Discover(new());

        Assert.Single(result.Sources);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TBRX_ACCOUNT_NOT_IMAP");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TBRX_PREFS_MISSING");
    }

    [Fact]
    public void Source_id_is_stable_for_same_normalized_account()
    {
        var first = new ThunderbirdRuleSource("a", "a/file", "a", "s", "IMAP", "IMAP.Example.COM.", "User@Example.com");
        var second = new ThunderbirdRuleSource("b", "b/file", "b", "x", "imap", "imap.example.com", "user@example.com");

        Assert.Equal(first.SourceId, second.SourceId);
    }

    private static string CreateProfile(
        TestDirectory directory,
        string relative,
        string type,
        string hostname,
        string username)
    {
        string profile = directory.CreateDirectory(relative);
        directory.CreateDirectory($"{relative}/ImapMail/server1");
        directory.Write($"{relative}/ImapMail/server1/msgFilterRules.dat", "version=\"9\"\nlogging=\"no\"\n");
        directory.Write($"{relative}/prefs.js", $$"""
            // Synthetic fixture; not a general JavaScript program.
            user_pref("mail.account.account1.server", "server1");
            user_pref("mail.server.server1.type", "{{type}}");
            user_pref("mail.server.server1.hostname", "{{hostname}}");
            user_pref("mail.server.server1.userName", "{{username}}");
            user_pref("mail.server.server1.directory-rel", "[ProfD]ImapMail/server1");
            """);
        return profile;
    }
}
