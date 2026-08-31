using System.Security.Cryptography;
using System.Text;
using Transiever.SieveRuler.Models;

namespace Transiever.ThunderbirdResiever.Services;

public sealed record ThunderbirdSourceRequest(
    string? ProfileDirectory = null,
    string? FiltersFile = null);

public sealed record ThunderbirdRuleSource(
    string ProfileDirectory,
    string FiltersFile,
    string AccountKey,
    string ServerKey,
    string ServerType,
    string Hostname,
    string Username)
{
    public string SourceId
    {
        get
        {
            string identity = string.Join(
                '|',
                ServerType.Trim().ToLowerInvariant(),
                Hostname.Trim().TrimEnd('.').ToLowerInvariant(),
                Username.Trim().ToLowerInvariant());
            string hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
                .ToLowerInvariant();
            return $"thunderbird-{hash[..16]}";
        }
    }
}

public sealed record ThunderbirdExportDiagnostic(
    string Severity,
    string Code,
    string Message,
    string? RuleName = null);

public sealed record ThunderbirdSourceDiscoveryResult(
    IReadOnlyList<ThunderbirdRuleSource> Sources,
    IReadOnlyList<ThunderbirdExportDiagnostic> Diagnostics,
    bool IsComplete = true);

public sealed record ThunderbirdRuleExportResult(
    ThunderbirdRuleSource Source,
    IReadOnlyList<RuleDefinition> Rules,
    IReadOnlyList<ThunderbirdExportDiagnostic> Diagnostics,
    int EnabledRuleCount,
    int SkippedEnabledRuleCount)
{
    public bool IsPartial => SkippedEnabledRuleCount > 0;
}

public interface IThunderbirdRuleSourceDiscovery
{
    ThunderbirdSourceDiscoveryResult Discover(ThunderbirdSourceRequest request);
}

public interface IThunderbirdRuleExporter
{
    ThunderbirdRuleExportResult Export(ThunderbirdRuleSource source);
}

public interface IStableFileReader
{
    byte[] ReadAllBytes(string path);
}
