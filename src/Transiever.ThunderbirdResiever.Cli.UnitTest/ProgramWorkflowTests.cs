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
                "rollback", "--dry-run", "--sieve-host", "example.com", "--sieve-username", "user", "--sieve-password", "secret"]),
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

    private static ThunderbirdRuleSource Source() =>
        new("profile", "fixture", "account1", "server1", "imap", "imap.example.com", "user@example.com");

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

    private sealed class StubInteraction : IThunderbirdRunInteraction
    {
        public bool AcceptPartial { get; init; }
        public ThunderbirdRuleSource ResolveSource(IReadOnlyList<ThunderbirdRuleSource> sources) => sources.Single();
        public bool ConfirmPartial(bool explicitlyAllowed, int exportedCount, int skippedCount) => explicitlyAllowed || AcceptPartial;
        public RuleOptimizationMode? ResolveOptimization(RuleOptimizationMode? explicitMode, bool explicitChoice) => explicitMode;
        public bool ConfirmUpload(bool explicitlyDeploy, string scriptName) => false;
    }

    private sealed class TrackingConfiguration : ISieveServerConfigurationProvider
    {
        public bool Called { get; private set; }
        public SieveServerConfiguration GetConfiguration(CommandLineOptions options)
        {
            Called = true;
            return new SieveServerConfiguration("example.com", 4190, "user", "secret", SieveConnectionSecurity.StartTlsRequired);
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
