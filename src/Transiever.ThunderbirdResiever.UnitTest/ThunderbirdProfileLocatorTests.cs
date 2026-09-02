using Transiever.ThunderbirdResiever.Services;

namespace Transiever.ThunderbirdResiever.UnitTest;

public sealed class ThunderbirdProfileLocatorTests
{
    [Fact]
    public void Default_discovery_roots_returns_only_app_data_on_windows()
    {
        string appData = Path.Combine("C:", "Users", "tester", "AppData", "Roaming");

        IReadOnlyList<string> roots = ThunderbirdProfileLocator.DefaultDiscoveryRoots(
            appData,
            Path.Combine("C:", "Users", "tester"),
            isWindows: true);

        Assert.Equal([Path.Combine(appData, "Thunderbird")], roots);
    }

    [Fact]
    public void Default_discovery_roots_returns_standard_flatpak_and_snap_on_linux()
    {
        string home = Path.Combine(Path.DirectorySeparatorChar.ToString(), "home", "tester");

        IReadOnlyList<string> roots = ThunderbirdProfileLocator.DefaultDiscoveryRoots(
            appData: null,
            home,
            isWindows: false);

        Assert.Equal(
            [
                Path.Combine(home, ".thunderbird"),
                Path.Combine(home, ".var", "app", "org.mozilla.Thunderbird", ".thunderbird"),
                Path.Combine(home, "snap", "thunderbird", "common", ".thunderbird")
            ],
            roots);
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(" ", "", true)]
    [InlineData(null, null, false)]
    [InlineData("ignored", " ", false)]
    public void Default_discovery_roots_ignores_blank_inputs(
        string? appData,
        string? home,
        bool isWindows)
    {
        IReadOnlyList<string> roots = ThunderbirdProfileLocator.DefaultDiscoveryRoots(
            appData,
            home,
            isWindows);

        Assert.Empty(roots);
    }

    [Fact]
    public void Discover_reads_relative_and_absolute_profiles_ini_paths()
    {
        using var directory = new TestDirectory();
        string root = directory.CreateDirectory("root");
        string relativeProfile = CreateProfile(
            directory,
            "root/Profiles/default",
            "imap",
            "imap.example.invalid",
            "user@example.invalid");
        string absoluteProfile = CreateProfile(
            directory,
            "relocated",
            "imap",
            "imap.relocated.example.invalid",
            "relocated@example.invalid");
        directory.Write("root/profiles.ini", $$"""
            [Profile0]
            Name=default
            IsRelative=1
            Path=Profiles/default
            Default=1
            [Profile1]
            Name=relocated
            IsRelative=0
            Path={{absoluteProfile}}
            """);

        ThunderbirdSourceDiscoveryResult result = new ThunderbirdProfileLocator([root]).Discover(new());

        Assert.Equal(2, result.Sources.Count);
        Assert.Contains(result.Sources, source => source.ProfileDirectory == Path.GetFullPath(relativeProfile));
        Assert.Contains(result.Sources, source => source.ProfileDirectory == Path.GetFullPath(absoluteProfile));
        Assert.All(result.Sources, source => Assert.Equal("account1", source.AccountKey));
        Assert.All(result.Sources, source => Assert.Equal("server1", source.ServerKey));
        Assert.All(result.Sources, source => Assert.StartsWith("thunderbird-", source.SourceId));
        Assert.Empty(result.Diagnostics);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Discover_reports_relative_profile_path_marked_absolute()
    {
        using var directory = new TestDirectory();
        string root = directory.CreateDirectory("root");
        string validProfile = CreateProfile(
            directory,
            "root/valid",
            "imap",
            "valid.example.invalid",
            "valid@example.invalid");
        directory.Write("root/profiles.ini", """
            [Profile0]
            IsRelative=1
            Path=valid
            [Profile1]
            IsRelative=0
            Path=Profiles/invalid
            """);

        ThunderbirdSourceDiscoveryResult result = new ThunderbirdProfileLocator([root]).Discover(new());

        Assert.Equal(Path.GetFullPath(validProfile), Assert.Single(result.Sources).ProfileDirectory);
        Assert.False(result.IsComplete);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic is
                { Severity: "Warning", Code: "TBRX_PROFILE_PATH_INVALID" } &&
                diagnostic.Message.Contains(
                    "Use IsRelative=1 for relative paths and 0 for rooted paths.",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void Discover_reports_rooted_profile_path_marked_relative()
    {
        using var directory = new TestDirectory();
        string root = directory.CreateDirectory("root");
        string validProfile = CreateProfile(
            directory,
            "root/valid",
            "imap",
            "valid.example.invalid",
            "valid@example.invalid");
        string profile = CreateProfile(
            directory,
            "profile",
            "imap",
            "imap.example.invalid",
            "user@example.invalid");
        Assert.True(Path.IsPathRooted(profile));
        directory.Write("root/profiles.ini", $$"""
            [Profile0]
            IsRelative=1
            Path=valid
            [Profile1]
            IsRelative=1
            Path={{profile}}
            """);

        ThunderbirdSourceDiscoveryResult result = new ThunderbirdProfileLocator([root]).Discover(new());

        Assert.Equal(Path.GetFullPath(validProfile), Assert.Single(result.Sources).ProfileDirectory);
        Assert.False(result.IsComplete);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic is
                { Severity: "Warning", Code: "TBRX_PROFILE_PATH_INVALID" } &&
                diagnostic.Message.Contains(
                    "Use IsRelative=1 for relative paths and 0 for rooted paths.",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void Discover_applies_native_case_semantics_to_explicit_filter_paths()
    {
        using var directory = new TestDirectory();
        string profile = CreateProfile(
            directory,
            "profile",
            "imap",
            "imap.example.invalid",
            "user@example.invalid");
        string filters = Path.Combine(profile, "ImapMail", "server1", "msgFilterRules.dat");

        ThunderbirdSourceDiscoveryResult result = new ThunderbirdProfileLocator([]).Discover(
            new ThunderbirdSourceRequest(profile, filters.ToUpperInvariant()));

        if (OperatingSystem.IsWindows())
            Assert.Single(result.Sources);
        else
            Assert.Empty(result.Sources);
    }

    [Fact]
    public void Discover_keeps_case_distinct_linux_profiles()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var directory = new TestDirectory();
        string root = directory.CreateDirectory("root");
        _ = CreateProfile(directory, "root/Profile", "imap", "upper.example.invalid", "upper@example.invalid");
        _ = CreateProfile(directory, "root/profile", "imap", "lower.example.invalid", "lower@example.invalid");
        directory.Write("root/profiles.ini", """
            [Profile0]
            IsRelative=1
            Path=Profile
            [Profile1]
            IsRelative=1
            Path=profile
            """);

        ThunderbirdSourceDiscoveryResult result = new ThunderbirdProfileLocator([root]).Discover(new());

        Assert.Equal(2, result.Sources.Count);
    }

    [Fact]
    public void Discover_collapses_case_variants_of_one_windows_profile()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var directory = new TestDirectory();
        string root = directory.CreateDirectory("root");
        _ = CreateProfile(directory, "root/Profile", "imap", "imap.example.invalid", "user@example.invalid");
        directory.Write("root/profiles.ini", """
            [Profile0]
            IsRelative=1
            Path=Profile
            [Profile1]
            IsRelative=1
            Path=profile
            """);

        ThunderbirdSourceDiscoveryResult result = new ThunderbirdProfileLocator([root]).Discover(new());

        Assert.Single(result.Sources);
    }

    [Fact]
    public void Discover_keeps_linux_link_aliases_as_distinct_lexical_paths()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var directory = new TestDirectory();
        string root = directory.CreateDirectory("root");
        string profile = CreateProfile(
            directory,
            "root/Profile",
            "imap",
            "imap.example.invalid",
            "user@example.invalid");
        _ = directory.CreateDirectoryLink("root/Alias", profile);
        directory.Write("root/profiles.ini", """
            [Profile0]
            IsRelative=1
            Path=Profile
            [Profile1]
            IsRelative=1
            Path=Alias
            """);

        ThunderbirdSourceDiscoveryResult result = new ThunderbirdProfileLocator([root]).Discover(new());

        Assert.Equal(2, result.Sources.Count);
        Assert.Single(result.Sources.Select(source => source.SourceId).Distinct(StringComparer.Ordinal));
        Assert.Equal(
            2,
            result.Sources.Select(source => source.ProfileDirectory).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            2,
            result.Sources.Select(source => source.FiltersFile).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Discover_does_not_modify_profile_files()
    {
        using var directory = new TestDirectory();
        string root = directory.CreateDirectory("root");
        string profile = CreateProfile(
            directory,
            "root/Profile",
            "imap",
            "imap.example.invalid",
            "user@example.invalid");
        string profiles = directory.Write("root/profiles.ini", """
            [Profile0]
            IsRelative=1
            Path=Profile
            """);
        string prefs = Path.Combine(profile, "prefs.js");
        string filters = Path.Combine(profile, "ImapMail", "server1", "msgFilterRules.dat");
        var snapshots = new[] { profiles, prefs, filters }
            .Select(path => (Path: path, Bytes: File.ReadAllBytes(path), LastWriteTimeUtc: File.GetLastWriteTimeUtc(path)))
            .ToArray();

        _ = new ThunderbirdProfileLocator([root]).Discover(new());

        foreach (var snapshot in snapshots)
        {
            Assert.Equal(snapshot.Bytes, File.ReadAllBytes(snapshot.Path));
            Assert.Equal(snapshot.LastWriteTimeUtc, File.GetLastWriteTimeUtc(snapshot.Path));
        }
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
            _ = CreateProfile(directory, $"{layout}/profile", "imap", $"{layout}.example.invalid", $"{layout}@example.invalid");
            directory.Write($"{layout}/profiles.ini", "[Profile0]\nIsRelative=1\nPath=profile\n");
        }

        ThunderbirdSourceDiscoveryResult result = new ThunderbirdProfileLocator(roots).Discover(new());

        Assert.Equal(4, result.Sources.Count);
        Assert.Equal(4, result.Sources.Select(source => source.SourceId).Distinct().Count());
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Discover_supports_explicit_relocated_profile_and_filter()
    {
        using var directory = new TestDirectory();
        string profile = CreateProfile(directory, "relocated", "imap", "imap.example.invalid", "user@example.invalid");
        string filters = Path.Combine(profile, "ImapMail", "server1", "msgFilterRules.dat");

        ThunderbirdSourceDiscoveryResult result = new ThunderbirdProfileLocator([]).Discover(
            new ThunderbirdSourceRequest(profile, filters));

        Assert.Equal(Path.GetFullPath(filters), Assert.Single(result.Sources).FiltersFile);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Discover_reports_pop_and_marks_missing_preferences_incomplete()
    {
        using var directory = new TestDirectory();
        string root = directory.CreateDirectory("root");
        _ = CreateProfile(directory, "root/pop", "pop3", "pop.example.invalid", "user@example.invalid");
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
        Assert.False(result.IsComplete);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TBRX_ACCOUNT_NOT_IMAP");
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic is
                { Severity: "Warning", Code: "TBRX_PREFS_MISSING" });
    }

    [Fact]
    public void Discover_reports_missing_account_filter_as_incomplete()
    {
        using var directory = new TestDirectory();
        string profile = CreateProfile(
            directory,
            "profile",
            "imap",
            "imap.example.invalid",
            "user@example.invalid",
            createFilters: false);

        ThunderbirdSourceDiscoveryResult result = new ThunderbirdProfileLocator([]).Discover(
            new ThunderbirdSourceRequest(ProfileDirectory: profile));

        Assert.False(result.IsComplete);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic is
                { Severity: "Warning", Code: "TBRX_FILTERS_MISSING" });
    }

    [Fact]
    public void Discover_rejects_invalid_explicit_profile()
    {
        using var directory = new TestDirectory();
        string profile = directory.CreateDirectory("invalid");

        ThunderbirdSourceDiscoveryResult result = new ThunderbirdProfileLocator([]).Discover(
            new ThunderbirdSourceRequest(ProfileDirectory: profile));

        Assert.False(result.IsComplete);
        Assert.Empty(result.Sources);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic is
                { Severity: "Error", Code: "TBRX_PROFILE_INVALID" });
    }

    [Theory]
    [InlineData("type", "TBRX_ACCOUNT_INCOMPLETE")]
    [InlineData("userName", "TBRX_ACCOUNT_INCOMPLETE")]
    [InlineData("directory-rel", "TBRX_ACCOUNT_DIRECTORY_MISSING")]
    public void Discover_marks_incomplete_account_preferences(
        string missingPreference,
        string expectedCode)
    {
        using var directory = new TestDirectory();
        string profile = directory.CreateDirectory("profile");
        directory.CreateDirectory("profile/ImapMail/server1");
        directory.Write("profile/ImapMail/server1/msgFilterRules.dat", "version=\"9\"\nlogging=\"no\"\n");
        directory.Write("profile/prefs.js", $$"""
            user_pref("mail.account.account1.server", "server1");
            {{PreferenceUnlessMissing("type", "imap")}}
            {{PreferenceUnlessMissing("hostname", "imap.example.invalid")}}
            {{PreferenceUnlessMissing("userName", "user@example.invalid")}}
            {{PreferenceUnlessMissing("directory-rel", "[ProfD]ImapMail/server1")}}
            """);

        ThunderbirdSourceDiscoveryResult result = new ThunderbirdProfileLocator([]).Discover(
            new ThunderbirdSourceRequest(ProfileDirectory: profile));

        Assert.False(result.IsComplete);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic is
                { Severity: "Warning", Code: var code } && code == expectedCode);

        string PreferenceUnlessMissing(string key, string value) =>
            missingPreference == key ? "" : $"user_pref(\"mail.server.server1.{key}\", \"{value}\");";
    }

    [Fact]
    public void Discover_collapses_multiple_account_keys_for_one_server()
    {
        using var directory = new TestDirectory();
        string profile = CreateProfile(
            directory,
            "profile",
            "imap",
            "imap.example.invalid",
            "user@example.invalid");
        directory.Write("profile/prefs.js", """
            user_pref("mail.account.account2.server", "server1");
            user_pref("mail.account.account1.server", "server1");
            user_pref("mail.server.server1.type", "imap");
            user_pref("mail.server.server1.hostname", "imap.example.invalid");
            user_pref("mail.server.server1.userName", "user@example.invalid");
            user_pref("mail.server.server1.directory-rel", "[ProfD]ImapMail/server1");
            """);

        ThunderbirdSourceDiscoveryResult result = new ThunderbirdProfileLocator([]).Discover(
            new ThunderbirdSourceRequest(ProfileDirectory: profile));

        ThunderbirdRuleSource source = Assert.Single(result.Sources);
        Assert.Equal("account1", source.AccountKey);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Discover_exact_filter_ignores_unrelated_incomplete_account()
    {
        using var directory = new TestDirectory();
        string profile = CreateProfile(directory, "profile", "imap", "imap.example.invalid", "user@example.invalid");
        string filters = Path.Combine(profile, "ImapMail", "server1", "msgFilterRules.dat");
        directory.Write("profile/prefs.js", """
            user_pref("mail.account.account1.server", "server1");
            user_pref("mail.account.account2.server", "server2");
            user_pref("mail.server.server1.type", "imap");
            user_pref("mail.server.server1.hostname", "imap.example.invalid");
            user_pref("mail.server.server1.userName", "user@example.invalid");
            user_pref("mail.server.server1.directory-rel", "[ProfD]ImapMail/server1");
            user_pref("mail.server.server2.type", "imap");
            user_pref("mail.server.server2.hostname", "broken.example.invalid");
            user_pref("mail.server.server2.directory-rel", "[ProfD]ImapMail/server2");
            """);

        ThunderbirdSourceDiscoveryResult result = new ThunderbirdProfileLocator([]).Discover(
            new ThunderbirdSourceRequest(profile, filters));

        Assert.Equal(filters, Assert.Single(result.Sources).FiltersFile);
        Assert.True(result.IsComplete);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Discover_marks_unmapped_explicit_filter_incomplete()
    {
        using var directory = new TestDirectory();
        string profile = CreateProfile(directory, "profile", "imap", "imap.example.invalid", "user@example.invalid");
        string filters = Path.Combine(profile, "ImapMail", "missing", "msgFilterRules.dat");

        ThunderbirdSourceDiscoveryResult result = new ThunderbirdProfileLocator([]).Discover(
            new ThunderbirdSourceRequest(profile, filters));

        Assert.Empty(result.Sources);
        Assert.False(result.IsComplete);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TBRX_SOURCE_NOT_MAPPED");
    }

    [Fact]
    public void Discover_rejects_conflicting_accounts_for_one_filter_file()
    {
        using var directory = new TestDirectory();
        string profile = directory.CreateDirectory("profile");
        directory.Write("profile/ImapMail/shared/msgFilterRules.dat", "version=\"9\"\nlogging=\"no\"\n");
        directory.Write("profile/prefs.js", """
            user_pref("mail.account.account1.server", "server1");
            user_pref("mail.account.account2.server", "server2");
            user_pref("mail.server.server1.type", "imap");
            user_pref("mail.server.server1.hostname", "imap.example.invalid");
            user_pref("mail.server.server1.userName", "user@example.invalid");
            user_pref("mail.server.server1.directory-rel", "[ProfD]ImapMail/shared");
            user_pref("mail.server.server2.type", "imap");
            user_pref("mail.server.server2.hostname", "other.example.invalid");
            user_pref("mail.server.server2.userName", "user@example.invalid");
            user_pref("mail.server.server2.directory-rel", "[ProfD]ImapMail/shared");
            """);

        ThunderbirdSourceDiscoveryResult result = new ThunderbirdProfileLocator([]).Discover(
            new ThunderbirdSourceRequest(ProfileDirectory: profile));

        Assert.Empty(result.Sources);
        Assert.False(result.IsComplete);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic is
                { Severity: "Error", Code: "TBRX_SOURCE_AMBIGUOUS" });
    }

    [Fact]
    public void Source_id_is_stable_for_same_normalized_account()
    {
        var first = new ThunderbirdRuleSource("a", "a/file", "a", "s", "IMAP", "IMAP.Example.INVALID.", "User@Example.invalid");
        var second = new ThunderbirdRuleSource("b", "b/file", "b", "x", "imap", "imap.example.invalid", "user@example.invalid");

        Assert.Equal("thunderbird-4cbd0cf818a5b322", first.SourceId);
        Assert.Equal(first.SourceId, second.SourceId);
    }

    private static string CreateProfile(
        TestDirectory directory,
        string relative,
        string type,
        string hostname,
        string username,
        bool createFilters = true)
    {
        string profile = directory.CreateDirectory(relative);
        directory.CreateDirectory($"{relative}/ImapMail/server1");
        if (createFilters)
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
