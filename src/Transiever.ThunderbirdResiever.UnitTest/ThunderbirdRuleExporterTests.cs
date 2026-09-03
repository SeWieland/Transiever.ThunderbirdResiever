using System.Text;
using Transiever.SieveRuler.Models;
using Transiever.ThunderbirdResiever.Services;

namespace Transiever.ThunderbirdResiever.UnitTest;

public sealed class ThunderbirdRuleExporterTests
{
    [Fact]
    public void Export_maps_supported_rules_in_stable_order()
    {
        using var directory = new TestDirectory();
        string filters = directory.Write("msgFilterRules.dat", """
            version="9"
            logging="no"
            name="First \"quoted\" rule"
            enabled="yes"
            type="1"
            action="Move to folder"
            actionValue="imap://user%40example.invalid@imap.example.invalid/INBOX/Team%20Mail"
            action="Mark read"
            action="Stop execution"
            condition="AND (from,contains,alice@example.invalid) AND (subject,doesn't contain,ignore)"
            name="Anhang"
            enabled="yes"
            type="1"
            action="Copy to folder"
            actionValue="imap://user%40example.invalid@imap.example.invalid/Archive/%C3%9Cber"
            condition="OR (subject,contains,Rechnung) OR (body,contains,Rechnung)"
            name="Attachment"
            enabled="yes"
            type="1"
            action="Mark read"
            condition="AND (has attachment status,is,true)"
            """.Replace("\n", "\r\n", StringComparison.Ordinal));

        ThunderbirdRuleExportResult result = new ThunderbirdRuleExporter().Export(Source(filters));

        Assert.Equal(3, result.Rules.Count);
        Assert.Equal("First \"quoted\" rule", result.Rules[0].Name);
        Assert.Equal("INBOX/Team Mail", result.Rules[0].TargetFolder);
        Assert.Equal(
            [RuleActionType.FileInto, RuleActionType.SetFlags, RuleActionType.Stop],
            result.Rules[0].Actions.Select(action => action.Type));
        Assert.Equal(RuleConditionType.SenderContains, result.Rules[0].Conditions.Single().Type);
        Assert.Equal(RuleConditionType.SubjectContains, result.Rules[0].Exceptions.Single().Type);
        Assert.Equal(RuleConditionMode.Any, result.Rules[1].ConditionMode);
        Assert.Equal(
            [RuleConditionType.SubjectContains, RuleConditionType.BodyContains],
            result.Rules[1].Conditions.Select(condition => condition.Type));
        Assert.Equal("Archive/Über", result.Rules[1].Actions.Single().Values.Single());
        Assert.Equal(RuleConditionType.HasAttachment, result.Rules[2].Conditions.Single().Type);
        Assert.Equal([0, 1, 2], result.Rules.Select(rule => rule.OriginalOrder));
        Assert.False(result.IsPartial);
    }

    [Theory]
    [InlineData("type=\"17\"\naction=\"Mark read\"\ncondition=\"AND (from,contains,unsafe@example.invalid)\"")]
    [InlineData("type=\"1\"\naction=\"Mark read\"\ncondition=\"ALL\"")]
    [InlineData("type=\"1\"\naction=\"Mark read\"\ncondition=\"OR (from,doesn't contain,a) OR (subject,contains,b)\"")]
    [InlineData("type=\"1\"\naction=\"Mark read\"\ncondition=\"AND (to,contains,unsafe@example.invalid)\"")]
    [InlineData("type=\"1\"\naction=\"Mark read\"\naction=\"Forward\"\nactionValue=\"person@example.invalid\"\ncondition=\"AND (from,contains,unsafe@example.invalid)\"")]
    [InlineData("type=\"1\"\naction=\"Mark read\"\naction=\"Delete\"\ncondition=\"AND (from,contains,unsafe@example.invalid)\"")]
    [InlineData("type=\"1\"\naction=\"Mark read\"\naction=\"AddTag\"\nactionValue=\"important\"\ncondition=\"AND (from,contains,unsafe@example.invalid)\"")]
    public void Export_skips_unsafe_enabled_rule_as_a_whole(string unsafeRule)
    {
        var reader = new MemoryReader($$"""
            version="9"
            logging="no"
            name="Before"
            enabled="yes"
            type="1"
            action="Mark read"
            condition="AND (from,contains,before@example.invalid)"
            name="Unsafe"
            enabled="yes"
            {{unsafeRule}}
            name="After"
            enabled="yes"
            type="1"
            action="Mark read"
            condition="AND (from,contains,after@example.invalid)"
            """);

        ThunderbirdRuleExportResult result = new ThunderbirdRuleExporter(reader).Export(Source("ignored"));

        Assert.Equal(["Before", "After"], result.Rules.Select(rule => rule.Name));
        Assert.Equal(3, result.EnabledRuleCount);
        Assert.Equal(1, result.SkippedEnabledRuleCount);
        ThunderbirdExportDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("Warning", diagnostic.Severity);
        Assert.Equal("TBRX_RULE_SKIPPED", diagnostic.Code);
        Assert.Equal("Unsafe", diagnostic.RuleName);
    }

    [Fact]
    public void Export_ignores_unsupported_fields_in_disabled_rule()
    {
        var reader = new MemoryReader("""
            version="9"
            logging="no"
            name="Disabled"
            enabled="no"
            type="1"
            customId="recognized-but-unsupported"
            action="Forward"
            actionValue="person@example.invalid"
            condition="AND (from,contains,disabled@example.invalid)"
            name="Enabled"
            enabled="yes"
            type="1"
            action="Mark read"
            condition="AND (from,contains,enabled@example.invalid)"
            """);

        ThunderbirdRuleExportResult result = new ThunderbirdRuleExporter(reader).Export(Source("ignored"));

        Assert.Equal(["Enabled"], result.Rules.Select(rule => rule.Name));
        Assert.Equal(1, result.EnabledRuleCount);
        Assert.Equal(0, result.SkippedEnabledRuleCount);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("imap://user%40example.invalid@IMAP.EXAMPLE.INVALID./INBOX/Team", "INBOX/Team")]
    [InlineData("imap://USER%40EXAMPLE.INVALID@imap.example.invalid/Archive", "Archive")]
    public void Export_maps_same_account_folder_with_normalized_account_identity(
        string folderUri,
        string expectedFolder)
    {
        var reader = new MemoryReader($$"""
            version="9"
            logging="no"
            name="Same account"
            enabled="yes"
            type="1"
            action="Move to folder"
            actionValue="{{folderUri}}"
            condition="AND (from,contains,alice@example.invalid)"
            """);

        ThunderbirdRuleExportResult result = new ThunderbirdRuleExporter(reader).Export(
            Source("ignored", "imap.example.invalid", "user@example.invalid"));

        Assert.Single(result.Rules);
        Assert.Equal(expectedFolder, result.Rules.Single().TargetFolder);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("imap://user%40example.invalid@other.example.invalid/Inbox")]
    [InlineData("imap://other%40example.invalid@imap.example.invalid/Inbox")]
    [InlineData("mailbox:///Local%20Folders/Inbox")]
    [InlineData("pop://user%40example.invalid@imap.example.invalid/Inbox")]
    [InlineData("imap://user%40example.invalid@imap.example.invalid")]
    public void Export_skips_whole_rule_for_unsupported_or_unowned_folder_target(string folderUri)
    {
        var reader = new MemoryReader($$"""
            version="9"
            logging="no"
            name="Unsafe folder"
            enabled="yes"
            type="1"
            action="Move to folder"
            actionValue="{{folderUri}}"
            condition="AND (from,contains,alice@example.invalid)"
            """);

        ThunderbirdRuleExportResult result = new ThunderbirdRuleExporter(reader).Export(
            Source("ignored", "imap.example.invalid", "user@example.invalid"));

        Assert.Empty(result.Rules);
        Assert.True(result.IsPartial);
        Assert.Equal(1, result.SkippedEnabledRuleCount);
        Assert.Equal("TBRX_RULE_SKIPPED", result.Diagnostics.Single().Code);
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(false, "logging=\"no\"\nname=\"Valid\"\nenabled=\"yes\"\ntype=\"1\"\naction=\"Mark read\"\ncondition=\"AND (from,contains,alice@example.invalid)\"")]
    [InlineData(false, "version=\"8\"\nlogging=\"no\"")]
    [InlineData(false, "version=\"9\"\nlogging=\"no\"\nname=\"broken")]
    [InlineData(false, "version=\"9\"\nlogging=\"no\"\nmystery=\"x\"")]
    [InlineData(false, "version=\"9\"\nlogging=\"no\"\nenabled=\"yes\"")]
    [InlineData(false, "version=\"9\"\nlogging=\"no\"\nname=\"Duplicate condition\"\nenabled=\"yes\"\ntype=\"1\"\ncondition=\"AND (from,contains,alice@example.invalid)\"\ncondition=\"AND (subject,contains,duplicate@example.invalid)\"")]
    [InlineData(false, "version=\"9\"\nlogging=\"no\"\nname=\"Duplicate enabled\"\nenabled=\"yes\"\nenabled=\"no\"\ntype=\"1\"\naction=\"Mark read\"\ncondition=\"AND (from,contains,alice@example.invalid)\"")]
    [InlineData(false, "version=\"9\"\nlogging=\"no\"\nname=\"Duplicate type\"\nenabled=\"yes\"\ntype=\"1\"\ntype=\"17\"\naction=\"Mark read\"\ncondition=\"AND (from,contains,alice@example.invalid)\"")]
    [InlineData(false, "version=\"9\"\nlogging=\"no\"\nname=\"Missing enabled\"\ntype=\"1\"\naction=\"Mark read\"\ncondition=\"AND (from,contains,alice@example.invalid)\"")]
    [InlineData(false, "version=\"9\"\nlogging=\"no\"\nname=\"Missing type\"\nenabled=\"yes\"\naction=\"Mark read\"\ncondition=\"AND (from,contains,alice@example.invalid)\"")]
    [InlineData(false, "version=\"9\"\nlogging=\"no\"\nname=\"Missing condition\"\nenabled=\"yes\"\ntype=\"1\"\naction=\"Mark read\"")]
    public void Export_rejects_complete_malformed_files(bool invalidUtf8, string text)
    {
        byte[] bytes = invalidUtf8
            ? [0xC3, 0x28]
            : Encoding.UTF8.GetBytes(text);

        Assert.Throws<InvalidDataException>(
            () => new ThunderbirdRuleExporter(new ByteReader(bytes)).Export(Source("ignored")));
    }

    [Fact]
    public void Export_propagates_snapshot_change_failure()
    {
        using var directory = new TestDirectory();
        string filters = directory.Write("msgFilterRules.dat", """
            version="9"
            logging="no"
            name="Read only"
            enabled="yes"
            type="1"
            action="Mark read"
            condition="AND (body,contains,path\\value)"
            """);
        DateTime originalTimeUtc = File.GetLastWriteTimeUtc(filters);
        byte[] changedBytes = File.ReadAllBytes(filters);
        changedBytes[0] = changedBytes[0] == (byte)'v' ? (byte)'V' : (byte)'v';
        var reader = new StableFileReader(path =>
        {
            File.WriteAllBytes(path, changedBytes);
            File.SetLastWriteTimeUtc(path, originalTimeUtc);
        });

        Assert.Throws<IOException>(
            () => new ThunderbirdRuleExporter(reader).Export(Source(filters)));
    }

    [Fact]
    public void Export_does_not_modify_source_file()
    {
        using var directory = new TestDirectory();
        string filters = directory.Write("msgFilterRules.dat", """
            version="9"
            logging="no"
            name="Read only"
            enabled="yes"
            type="1"
            action="Mark read"
            condition="AND (body,contains,path\\value)"
            """);
        byte[] before = File.ReadAllBytes(filters);
        DateTime writeTime = File.GetLastWriteTimeUtc(filters);

        _ = new ThunderbirdRuleExporter().Export(Source(filters));

        Assert.Equal(before, File.ReadAllBytes(filters));
        Assert.Equal(writeTime, File.GetLastWriteTimeUtc(filters));
    }

    private static ThunderbirdRuleSource Source(
        string filters,
        string hostname = "imap.example.invalid",
        string username = "user@example.invalid") =>
        new("profile", filters, "account1", "server1", "imap", hostname, username);

    private sealed class MemoryReader(string text) : IStableFileReader
    {
        public byte[] ReadAllBytes(string path) => Encoding.UTF8.GetBytes(text);
    }

    private sealed class ByteReader(byte[] bytes) : IStableFileReader
    {
        public byte[] ReadAllBytes(string path) => bytes;
    }

}
