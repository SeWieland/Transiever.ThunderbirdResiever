using Transiever.SieveRuler.Models;
using Transiever.SieveRuler.Services;
using Transiever.ThunderbirdResiever.Services;

namespace Transiever.ThunderbirdResiever.Application;

public sealed record ThunderbirdExportRequest(
    ThunderbirdRuleSource Source,
    string RulesFile,
    bool DryRun = false);

public sealed record ThunderbirdExportResult(
    RuleDocument Document,
    ThunderbirdRuleSource Source,
    IReadOnlyList<ThunderbirdExportDiagnostic> Diagnostics,
    int EnabledRuleCount,
    int SkippedEnabledRuleCount,
    string RulesFile,
    bool FilesWritten)
{
    public bool IsPartial => SkippedEnabledRuleCount > 0;
}

public sealed class ThunderbirdExportApplication(
    IThunderbirdRuleSourceDiscovery discovery,
    IThunderbirdRuleExporter exporter,
    IRuleSerializer serializer)
{
    public ThunderbirdSourceDiscoveryResult Discover(ThunderbirdSourceRequest request) =>
        discovery.Discover(request);

    public async Task<ThunderbirdExportResult> ExportAsync(
        ThunderbirdExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ThunderbirdRuleExportResult export = exporter.Export(request.Source);
        var document = new RuleDocument
        {
            SourceId = request.Source.SourceId,
            Rules = export.Rules.ToList()
        };

        if (!request.DryRun)
            await serializer.SaveDocumentAsync(document, request.RulesFile, cancellationToken);

        return new ThunderbirdExportResult(
            document,
            request.Source,
            export.Diagnostics,
            export.EnabledRuleCount,
            export.SkippedEnabledRuleCount,
            request.RulesFile,
            !request.DryRun);
    }
}
