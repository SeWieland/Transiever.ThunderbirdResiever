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
    [InlineData("Forward", "person@example.invalid")]
    [InlineData("Delete", null)]
    [InlineData("AddTag", "important")]
    public void Export_skips_whole_rule_for_unsupported_action(string action, string? value)
    {
        string actionValue = value is null ? "" : $"actionValue=\"{value}\"\n";
        var reader = new MemoryReader($$"""
            version="9"
            logging="no"
            name="Unsafe"
            enabled="yes"
            type="1"
            action="Mark read"
            action="{{action}}"
            {{actionValue}}condition="AND (from,contains,alice@example.invalid)"
            """);

        ThunderbirdRuleExportResult result = new ThunderbirdRuleExporter(reader).Export(Source("ignored"));

        Assert.Empty(result.Rules);
        Assert.True(result.IsPartial);
        Assert.Equal(1, result.SkippedEnabledRuleCount);
        Assert.Contains(action, result.Diagnostics.Single().Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("17", "AND (from,contains,a)")]
    [InlineData("1", "ALL")]
    [InlineData("1", "OR (from,doesn't contain,a) OR (subject,contains,b)")]
    [InlineData("1", "AND (to,contains,a)")]
    public void Export_skips_unsupported_context_or_condition(string type, string condition)
    {
        var reader = new MemoryReader($$"""
            version="9"
            logging="no"
            name="Unsupported"
            enabled="yes"
            type="{{type}}"
            action="Mark read"
            condition="{{condition}}"
            """);

        ThunderbirdRuleExportResult result = new ThunderbirdRuleExporter(reader).Export(Source("ignored"));

        Assert.Empty(result.Rules);
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void Export_rejects_cross_account_folder()
    {
        var reader = new MemoryReader("""
            version="9"
            logging="no"
            name="Cross account"
            enabled="yes"
            type="1"
            action="Move to folder"
            actionValue="imap://other@example.net/INBOX"
            condition="AND (from,contains,a)"
            """);

        ThunderbirdRuleExportResult result = new ThunderbirdRuleExporter(reader).Export(Source("ignored"));

        Assert.Empty(result.Rules);
        Assert.Contains("different", result.Diagnostics.Single().Message, StringComparison.OrdinalIgnoreCase);
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
    [InlineData("version=\"8\"\nlogging=\"no\"")]
    [InlineData("version=\"9\"\nlogging=\"no\"\nmystery=\"x\"")]
    [InlineData("version=\"9\"\nlogging=\"no\"\nname=broken")]
    public void Export_rejects_unknown_or_malformed_files(string text)
    {
        Assert.Throws<InvalidDataException>(
            () => new ThunderbirdRuleExporter(new MemoryReader(text)).Export(Source("ignored")));
    }

    [Fact]
    public void Export_propagates_snapshot_change_failure()
    {
        Assert.Throws<IOException>(
            () => new ThunderbirdRuleExporter(new ThrowingReader()).Export(Source("ignored")));
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

    private sealed class ThrowingReader : IStableFileReader
    {
        public byte[] ReadAllBytes(string path) => throw new IOException("changed");
    }
}
