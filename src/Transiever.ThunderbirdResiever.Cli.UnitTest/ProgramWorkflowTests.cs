using Transiever.SieveRuler.Application;
using Transiever.SieveRuler.Models;
using Transiever.SieveRuler.Services;
using Transiever.ThunderbirdResiever.Application;
using Transiever.ThunderbirdResiever.Services;

namespace Transiever.ThunderbirdResiever.Cli.UnitTest;

public sealed class ProgramWorkflowTests
{
    [Fact]
    public async Task No_argument_program_path_returns_help_without_configuration()
    {
        int code = await Program.Main([]);

        Assert.Equal(0, code);
    }

    [Fact]
    public async Task Run_blocks_empty_export_before_configuration_or_network()
    {
        var configuration = new TrackingConfiguration();
        var synchronization = new StubSynchronization();
        ThunderbirdResieverCliApplication cli = CreateCli([], 0, configuration, synchronization, new StubInteraction());

        int code = await cli.RunAsync(
            CommandLineOptions.Parse(["run", "--filters", "fixture", "--no-optimize"]),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, code);
        Assert.False(configuration.Called);
        Assert.False(synchronization.Called);
    }

    [Fact]
    public async Task Run_blocks_unacknowledged_partial_export_before_configuration_or_network()
    {
        var configuration = new TrackingConfiguration();
        var synchronization = new StubSynchronization();
        ThunderbirdResieverCliApplication cli = CreateCli(
            [SupportedRule()],
            1,
            configuration,
            synchronization,
            new StubInteraction { AcceptPartial = false });

        int code = await cli.RunAsync(
            CommandLineOptions.Parse(["run", "--filters", "fixture", "--no-optimize"]),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, code);
        Assert.False(configuration.Called);
        Assert.False(synchronization.Called);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Export_refuses_incomplete_discovery_before_interaction_export_or_output(bool explicitProfile)
    {
        string rulesFile = Path.Combine(Path.GetTempPath(), $"tbrx-incomplete-{Guid.NewGuid():N}.json");
        var interaction = new TrackingInteraction();
        var exporter = new TrackingExporter();
        ThunderbirdResieverCliApplication cli = new(
            new ThunderbirdExportApplication(new IncompleteDiscovery(Source()), exporter, new JsonRuleSerializer()),
            new StubSynchronization(),
            new TrackingConfiguration(),
            interaction);

        string[] arguments = explicitProfile
            ? ["export", "--profile", "profile", "--rules", rulesFile]
            : ["export", "--rules", rulesFile];

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cli.RunAsync(CommandLineOptions.Parse(arguments), TestContext.Current.CancellationToken));

        Assert.Contains("discovery was incomplete", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(interaction.Called);
        Assert.False(exporter.Called);
        Assert.False(File.Exists(rulesFile));
    }

    [Fact]
    public async Task Export_allows_complete_pop_source_without_configuration_or_synchronization()
    {
        string rulesFile = Path.Combine(Path.GetTempPath(), $"tbrx-pop-{Guid.NewGuid():N}.json");
        var configuration = new TrackingConfiguration();
        var synchronization = new StubSynchronization();
        ThunderbirdRuleSource source = Source("pop3");
        ThunderbirdResieverCliApplication cli = new(
            new ThunderbirdExportApplication(
                new FakeDiscovery(source),
                new FakeExporter([SupportedRule()], 0),
                new JsonRuleSerializer()),
            synchronization,
            configuration,
            new StubInteraction());

        int code = await cli.RunAsync(
            CommandLineOptions.Parse(["export", "--filters", "fixture", "--rules", rulesFile]),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, code);
        Assert.True(File.Exists(rulesFile));
        Assert.False(configuration.Called);
        Assert.False(synchronization.Called);
        File.Delete(rulesFile);
    }

    [Fact]
    public async Task Export_uses_complete_exact_filter_when_unrelated_account_is_incomplete()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("tbrx-routing-");
        try
        {
            string profile = directory.FullName;
            string mailDirectory = Path.Combine(profile, "ImapMail", "server1");
            Directory.CreateDirectory(mailDirectory);
            string filters = Path.Combine(mailDirectory, "msgFilterRules.dat");
            File.WriteAllText(filters, """
                version="9"
                logging="no"
                name="Read"
                enabled="yes"
                type="1"
                action="Mark read"
                condition="AND (from,contains,a)"
                """);
            File.WriteAllText(Path.Combine(profile, "prefs.js"), """
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

            var configuration = new TrackingConfiguration();
            var synchronization = new StubSynchronization();
            ThunderbirdResieverCliApplication cli = new(
                new ThunderbirdExportApplication(
                    new ThunderbirdProfileLocator([]),
                    new ThunderbirdRuleExporter(),
                    new JsonRuleSerializer()),
                synchronization,
                configuration,
                new StubInteraction());

            int code = await cli.RunAsync(
                CommandLineOptions.Parse(["export", "--filters", filters, "--rules", Path.Combine(profile, "rules.json")]),
                TestContext.Current.CancellationToken);

            Assert.Equal(0, code);
            Assert.False(configuration.Called);
            Assert.False(synchronization.Called);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task Run_refuses_complete_pop_source_before_export_configuration_or_network()
    {
        var exporter = new TrackingExporter();
        var configuration = new TrackingConfiguration();
        var synchronization = new StubSynchronization();
        ThunderbirdResieverCliApplication cli = new(
            new ThunderbirdExportApplication(
                new FakeDiscovery(Source("pop3")),
                exporter,
                new JsonRuleSerializer()),
            synchronization,
            configuration,
            new StubInteraction());

        int code = await cli.RunAsync(
            CommandLineOptions.Parse(["run", "--filters", "fixture", "--no-optimize"]),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, code);
        Assert.False(exporter.Called);
        Assert.False(configuration.Called);
        Assert.False(synchronization.Called);
    }

    [Fact]
    public void Redirected_ambiguous_source_selection_requires_filters()
    {
        if (!Console.IsInputRedirected)
            return;

        Func<ThunderbirdRuleSource> resolve = () =>
            new ConsoleThunderbirdInteraction().ResolveSource([Source(), Source("imap", "other")]);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(resolve);

        Assert.Contains("--filters", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rollback_never_discovers_Thunderbird_data()
    {
        var discovery = new ThrowingDiscovery();
        var synchronization = new StubSynchronization
        {
            RestoreResult = new HistoryRestoreResult
            {
                Status = HistoryRestoreStatus.PlanValidated,
                SourceScriptName = "srtx-backup-test"
            }
        };
        var cli = new ThunderbirdResieverCliApplication(
            new ThunderbirdExportApplication(discovery, new FakeExporter([], 0), new JsonRuleSerializer()),
            synchronization,
            new TrackingConfiguration(),
            new StubInteraction());

        int code = await cli.RunAsync(
            CommandLineOptions.Parse([
                "rollback", "--dry-run", "--sieve-host", "example.invalid", "--sieve-username", "user", "--sieve-password", "secret"]),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, code);
        Assert.False(discovery.Called);
        Assert.True(synchronization.Called);
    }

    private static ThunderbirdResieverCliApplication CreateCli(
        IReadOnlyList<RuleDefinition> rules,
        int skipped,
        TrackingConfiguration configuration,
        StubSynchronization synchronization,
        StubInteraction interaction)
    {
        ThunderbirdRuleSource source = Source();
        return new ThunderbirdResieverCliApplication(
            new ThunderbirdExportApplication(
                new FakeDiscovery(source),
                new FakeExporter(rules, skipped),
                new JsonRuleSerializer()),
            synchronization,
            configuration,
            interaction);
    }

    private static ThunderbirdRuleSource Source(string serverType = "imap", string account = "account1") =>
        new("profile", "fixture", account, $"server-{account}", serverType, $"{serverType}.example.invalid", "user@example.invalid");

    private static RuleDefinition SupportedRule() => new()
    {
        Name = "Rule",
        Conditions = [new RuleCondition { Type = RuleConditionType.SenderContains, Values = ["a"] }],
        Actions = [new RuleAction { Type = RuleActionType.SetFlags, Values = ["\\Seen"] }]
    };

    private sealed class FakeDiscovery(ThunderbirdRuleSource source) : IThunderbirdRuleSourceDiscovery
    {
        public ThunderbirdSourceDiscoveryResult Discover(ThunderbirdSourceRequest request) => new([source], []);
    }

    private sealed class IncompleteDiscovery(ThunderbirdRuleSource source) : IThunderbirdRuleSourceDiscovery
    {
        public ThunderbirdSourceDiscoveryResult Discover(ThunderbirdSourceRequest request) =>
            new([source], [new ThunderbirdExportDiagnostic("Error", "TBRX_PREFS_MISSING", "prefs.js is missing.")], false);
    }

    private sealed class ThrowingDiscovery : IThunderbirdRuleSourceDiscovery
    {
        public bool Called { get; private set; }
        public ThunderbirdSourceDiscoveryResult Discover(ThunderbirdSourceRequest request)
        {
            Called = true;
            throw new InvalidOperationException("Thunderbird access is forbidden during rollback.");
        }
    }

    private sealed class FakeExporter(IReadOnlyList<RuleDefinition> rules, int skipped) : IThunderbirdRuleExporter
    {
        public ThunderbirdRuleExportResult Export(ThunderbirdRuleSource source) =>
            new(source, rules, [], rules.Count + skipped, skipped);
    }

    private sealed class TrackingExporter : IThunderbirdRuleExporter
    {
        public bool Called { get; private set; }

        public ThunderbirdRuleExportResult Export(ThunderbirdRuleSource source)
        {
            Called = true;
            throw new InvalidOperationException("Exporter should not be called.");
        }
    }

    private sealed class StubInteraction : IThunderbirdRunInteraction
    {
        public bool AcceptPartial { get; init; }
        public ThunderbirdRuleSource ResolveSource(IReadOnlyList<ThunderbirdRuleSource> sources) => sources.Single();
        public bool ConfirmPartial(bool explicitlyAllowed, int exportedCount, int skippedCount) => explicitlyAllowed || AcceptPartial;
        public RuleOptimizationMode? ResolveOptimization(RuleOptimizationMode? explicitMode, bool explicitChoice) => explicitMode;
        public bool ConfirmUpload(bool explicitlyDeploy, string scriptName) => false;
    }

    private sealed class TrackingInteraction : IThunderbirdRunInteraction
    {
        public bool Called { get; private set; }
        public ThunderbirdRuleSource ResolveSource(IReadOnlyList<ThunderbirdRuleSource> sources)
        {
            Called = true;
            return sources.Single();
        }
        public bool ConfirmPartial(bool explicitlyAllowed, int exportedCount, int skippedCount) => false;
        public RuleOptimizationMode? ResolveOptimization(RuleOptimizationMode? explicitMode, bool explicitChoice) => null;
        public bool ConfirmUpload(bool explicitlyDeploy, string scriptName) => false;
    }

    private sealed class TrackingConfiguration : ISieveServerConfigurationProvider
    {
        public bool Called { get; private set; }
        public SieveServerConfiguration GetConfiguration(CommandLineOptions options)
        {
            Called = true;
            return new SieveServerConfiguration("example.invalid", 4190, "user", "secret", SieveConnectionSecurity.StartTlsRequired);
        }
    }

    private sealed class StubSynchronization : ISieveSynchronizationWorkflow
    {
        public bool Called { get; private set; }
        public HistoryRestoreResult? RestoreResult { get; init; }
        public Task<PreviewSynchronizationResult> PreviewAsync(PreviewSynchronizationRequest request, CancellationToken cancellationToken)
        {
            Called = true;
            throw new InvalidOperationException("Unexpected preview.");
        }
        public Task<DeploySynchronizationResult> DeployAsync(DeploySynchronizationRequest request, CancellationToken cancellationToken)
        {
            Called = true;
            throw new InvalidOperationException("Unexpected deploy.");
        }
        public Task<RollbackSynchronizationResult> RollbackAsync(RollbackSynchronizationRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<HistoryListResult> ListHistoryAsync(HistoryListRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<HistoryShowResult> ShowHistoryAsync(HistoryShowRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<HistoryRestoreResult> RestoreHistoryAsync(HistoryRestoreRequest request, CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult(RestoreResult ?? throw new InvalidOperationException("No restore result."));
        }
        public Task<HistoryDeleteResult> DeleteHistoryAsync(HistoryDeleteRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<HistoryPruneResult> PruneHistoryAsync(HistoryPruneRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
