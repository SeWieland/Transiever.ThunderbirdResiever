using Transiever.SieveRuler.Application;
using Transiever.SieveRuler.Models;
using Transiever.SieveRuler.Services;
using Transiever.ThunderbirdResiever.Application;
using Transiever.ThunderbirdResiever.Services;

namespace Transiever.ThunderbirdResiever.Cli;

public sealed class ThunderbirdResieverCliApplication(
    ThunderbirdExportApplication thunderbird,
    ISieveSynchronizationWorkflow synchronization,
    ISieveServerConfigurationProvider configurationProvider,
    IThunderbirdRunInteraction interaction)
{
    public Task<int> RunAsync(CommandLineOptions options, CancellationToken cancellationToken = default) =>
        options.Command switch
        {
            ThunderbirdResieverCommand.Run => RunWorkflowAsync(options, cancellationToken),
            ThunderbirdResieverCommand.Export => ExportAsync(options, cancellationToken),
            ThunderbirdResieverCommand.Rollback => RollbackAsync(options, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported command: {options.Command}")
        };

    private async Task<int> ExportAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        ThunderbirdRuleSource source = ResolveSource(options);
        ThunderbirdExportResult result = await ExportSourceAsync(source, options, options.DryRun, cancellationToken);
        PrintExport(result);
        return 0;
    }

    private async Task<int> RunWorkflowAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        ThunderbirdRuleSource source = ResolveSource(options);
        if (!source.ServerType.Equals("imap", StringComparison.OrdinalIgnoreCase))
            return Error($"Selected account uses '{source.ServerType}'. Only resolved IMAP accounts may enter tbrx run.");

        ThunderbirdExportResult export = await ExportSourceAsync(
            source,
            options,
            options.DryRun || !options.WriteArtifacts,
            cancellationToken);
        PrintExport(export);
        if (export.Document.Rules.Count == 0)
            return Error("No supported enabled rules were exported; refusing to contact or modify the server.");
        if (export.IsPartial && !interaction.ConfirmPartial(
            options.AllowPartial,
            export.Document.Rules.Count,
            export.SkippedEnabledRuleCount))
        {
            return Error("Partial export was not acknowledged. Use --allow-partial for unattended runs after reviewing diagnostics.");
        }

        RuleOptimizationMode? optimization = interaction.ResolveOptimization(
            options.OptimizationMode,
            options.OptimizationChoiceSpecified);
        SieveServerConfiguration configuration = configurationProvider.GetConfiguration(options);
        PreviewSynchronizationResult preview = await synchronization.PreviewAsync(
            new PreviewSynchronizationRequest(
                configuration,
                options.RulesFile,
                options.ReconciledRulesFile,
                options.CandidateRulesFile,
                options.ServerSnapshotFile,
                options.CandidateFile,
                options.PlanFile,
                options.AdoptCompatible,
                optimization,
                options.DryRun,
                export.Document,
                options.ScriptName,
                options.WriteArtifacts),
            cancellationToken);
        ConsolePresentation.PrintReconciliationDiagnostics(preview.Diagnostics);
        int previewCode = PrintPreview(preview, options);
        if (previewCode != 0 || preview.Status != PreviewSynchronizationStatus.Prepared || options.DryRun)
            return previewCode;

        if (!interaction.ConfirmUpload(options.Deploy, preview.TargetScriptName ?? options.PlanFile))
        {
            Console.WriteLine("Deployment skipped. No server changes were made.");
            return 0;
        }

        DeploySynchronizationResult deployed = await synchronization.DeployAsync(
            new DeploySynchronizationRequest(
                configuration,
                options.PlanFile,
                HistoryLimit: options.HistoryLimit,
                PruneHistory: options.PruneHistory,
                Plan: preview.Plan ?? throw new InvalidOperationException("Preview returned no deployment metadata.")),
            cancellationToken);
        return PrintDeploy(deployed);
    }

    private async Task<int> RollbackAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        HistoryRestoreResult result = await synchronization.RestoreHistoryAsync(
            new HistoryRestoreRequest(
                configurationProvider.GetConfiguration(options),
                "latest",
                DryRun: options.DryRun),
            cancellationToken);
        Console.WriteLine(result.Status switch
        {
            HistoryRestoreStatus.PlanValidated => $"Latest backup '{result.SourceScriptName}' can be restored. No server changes were made.",
            HistoryRestoreStatus.AlreadyActive => $"Latest backup '{result.SourceScriptName}' already matches the active state.",
            HistoryRestoreStatus.RestoredScript => $"Restored '{result.SourceScriptName}' into '{result.TargetScriptName}'. Backup '{result.BackupScriptName}' was retained.",
            HistoryRestoreStatus.DisabledActive => $"Restored the original no-active state. Backup '{result.BackupScriptName}' was retained.",
            _ => throw new InvalidOperationException($"Unsupported restore status: {result.Status}")
        });
        return 0;
    }

    private ThunderbirdRuleSource ResolveSource(CommandLineOptions options)
    {
        ThunderbirdSourceDiscoveryResult discovery = thunderbird.Discover(
            new ThunderbirdSourceRequest(options.ProfileDirectory, options.FiltersFile));
        ConsolePresentation.PrintDiagnostics(discovery.Diagnostics);
        return interaction.ResolveSource(discovery.Sources);
    }

    private async Task<ThunderbirdExportResult> ExportSourceAsync(
        ThunderbirdRuleSource source,
        CommandLineOptions options,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        ThunderbirdExportResult result = await thunderbird.ExportAsync(
            new ThunderbirdExportRequest(source, options.RulesFile, dryRun),
            cancellationToken);
        ConsolePresentation.PrintDiagnostics(result.Diagnostics);
        return result;
    }

    private static void PrintExport(ThunderbirdExportResult result)
    {
        Console.WriteLine(result.FilesWritten
            ? $"Exported {result.Document.Rules.Count} rules to {result.RulesFile}."
            : $"Exported {result.Document.Rules.Count} rules. No files written.");
        if (result.SkippedEnabledRuleCount > 0)
            Console.WriteLine($"Skipped {result.SkippedEnabledRuleCount} of {result.EnabledRuleCount} enabled rules.");
    }

    private static int PrintPreview(PreviewSynchronizationResult result, CommandLineOptions options)
    {
        switch (result.Status)
        {
            case PreviewSynchronizationStatus.Prepared:
                Console.WriteLine($"Prepared candidate with {result.ManagedRuleCount} managed rules. No server changes were made.");
                if (result.TargetScriptName is not null)
                    Console.WriteLine(result.ReplacesActiveScript
                        ? $"Target '{result.TargetScriptName}' is active and will be replaced in place."
                        : $"Target script: {result.TargetScriptName}");
                if (result.FilesWritten)
                    Console.WriteLine($"Review {options.RulesFile}, {options.ReconciledRulesFile}, {options.CandidateRulesFile}, {options.ServerSnapshotFile}, {options.CandidateFile}, and {options.PlanFile}.");
                return 0;
            case PreviewSynchronizationStatus.Blocked:
                return Error("Candidate generation is blocked by reconciliation errors.");
            case PreviewSynchronizationStatus.MissingCapabilities:
                return Error($"Server lacks required Sieve capabilities: {string.Join(", ", result.MissingCapabilities)}.");
            case PreviewSynchronizationStatus.InsufficientSpace:
                return Error("The server reported insufficient space for the candidate script.");
            default:
                throw new InvalidOperationException($"Unsupported preview status: {result.Status}");
        }
    }

    private static int PrintDeploy(DeploySynchronizationResult result)
    {
        string message = result.Status switch
        {
            DeploySynchronizationStatus.PlanValidated => $"Deployment plan for '{result.ScriptName}' is valid. No server changes were made.",
            DeploySynchronizationStatus.Skipped => "Deployment skipped. No server changes were made.",
            DeploySynchronizationStatus.UploadedInactive => $"Uploaded inactive script '{result.ScriptName}'.",
            DeploySynchronizationStatus.Activated => $"Activated '{result.ScriptName}'.",
            DeploySynchronizationStatus.ReplacedActive => $"Replaced '{result.ScriptName}'. Backup '{result.BackupScriptName}' was retained.",
            DeploySynchronizationStatus.InsufficientSpace => "The server reported insufficient space for the target or backup script.",
            _ => throw new InvalidOperationException($"Unsupported deployment status: {result.Status}")
        };
        (result.Status == DeploySynchronizationStatus.InsufficientSpace ? Console.Error : Console.Out).WriteLine(message);
        foreach (string script in result.DeletedHistoryScriptNames)
            Console.WriteLine($"Deleted obsolete SieveRuler history script '{script}'.");
        foreach (string warning in result.CleanupWarnings)
            Console.Error.WriteLine($"Warning: {warning}");
        return result.Status == DeploySynchronizationStatus.InsufficientSpace ? 2 : 0;
    }

    private static int Error(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }
}
