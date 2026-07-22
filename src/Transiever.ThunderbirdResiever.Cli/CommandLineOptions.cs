using System.Globalization;
using Transiever.SieveRuler.Models;
using Transiever.SieveRuler.Services;

namespace Transiever.ThunderbirdResiever.Cli;

public sealed class CommandLineOptions
{
    public ThunderbirdResieverCommand Command { get; private init; }
    public string? ProfileDirectory { get; private init; }
    public string? FiltersFile { get; private init; }
    public bool AllowPartial { get; private init; }
    public string RulesFile { get; private init; } = "rules.json";
    public string CandidateFile { get; private init; } = "candidate.sieve";
    public string ReconciledRulesFile { get; private init; } = "reconciled-rules.json";
    public string CandidateRulesFile { get; private init; } = "candidate-rules.json";
    public string ServerSnapshotFile { get; private init; } = "server-active.sieve";
    public string PlanFile { get; private init; } = "deployment-plan.json";
    public string? ScriptName { get; private init; }
    public RuleOptimizationMode? OptimizationMode { get; private init; }
    public bool OptimizationChoiceSpecified { get; private init; }
    public bool? AdoptCompatible { get; private init; }
    public bool Deploy { get; private init; }
    public bool DryRun { get; private init; }
    public bool WriteArtifacts { get; private init; }
    public int HistoryLimit { get; private init; } = 5;
    public bool PruneHistory { get; private init; } = true;
    public string? SieveHost { get; private init; }
    public int? SievePort { get; private init; }
    public string? SieveUserName { get; private init; }
    public string? SievePassword { get; private init; }
    public SieveConnectionSecurity? SieveSecurity { get; private init; }
    public bool ShowHelp { get; private init; }

    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || IsHelp(args[0]))
            return new CommandLineOptions { ShowHelp = true };

        ThunderbirdResieverCommand command = args[0].ToLowerInvariant() switch
        {
            "run" => ThunderbirdResieverCommand.Run,
            "export" => ThunderbirdResieverCommand.Export,
            "rollback" => ThunderbirdResieverCommand.Rollback,
            _ => throw new ArgumentException($"Unknown command: {args[0]}")
        };

        string? profile = null;
        string? filters = null;
        bool allowPartial = false;
        string rules = "rules.json";
        string candidate = "candidate.sieve";
        string reconciled = "reconciled-rules.json";
        string candidateRules = "candidate-rules.json";
        string snapshot = "server-active.sieve";
        string plan = "deployment-plan.json";
        string? scriptName = null;
        RuleOptimizationMode? optimization = null;
        bool optimizationSpecified = false;
        bool? adopt = null;
        bool deploy = false;
        bool dryRun = false;
        bool writeArtifacts = false;
        int historyLimit = 5;
        bool pruneHistory = true;
        string? sieveHost = null;
        int? sievePort = null;
        string? sieveUsername = null;
        string? sievePassword = null;
        SieveConnectionSecurity? sieveSecurity = null;
        bool runOnlyUsed = false;
        bool sourceUsed = false;
        bool rulesUsed = false;
        bool sieveUsed = false;
        bool artifactPathUsed = false;

        for (int index = 1; index < args.Count; index++)
        {
            string option = args[index];
            switch (option)
            {
                case "--profile":
                    profile = ReadValue(args, ref index, option);
                    sourceUsed = true;
                    break;
                case "--filters":
                    filters = ReadValue(args, ref index, option);
                    sourceUsed = true;
                    break;
                case "--allow-partial":
                    allowPartial = true;
                    runOnlyUsed = true;
                    break;
                case "--rules":
                    rules = ReadValue(args, ref index, option);
                    rulesUsed = true;
                    break;
                case "--candidate":
                    candidate = ReadValue(args, ref index, option);
                    artifactPathUsed = runOnlyUsed = true;
                    break;
                case "--reconciled-rules":
                    reconciled = ReadValue(args, ref index, option);
                    artifactPathUsed = runOnlyUsed = true;
                    break;
                case "--candidate-rules":
                    candidateRules = ReadValue(args, ref index, option);
                    artifactPathUsed = runOnlyUsed = true;
                    break;
                case "--server-snapshot":
                    snapshot = ReadValue(args, ref index, option);
                    artifactPathUsed = runOnlyUsed = true;
                    break;
                case "--plan":
                    plan = ReadValue(args, ref index, option);
                    artifactPathUsed = runOnlyUsed = true;
                    break;
                case "--script-name":
                    scriptName = ReadValue(args, ref index, option);
                    runOnlyUsed = true;
                    break;
                case "--adopt-compatible":
                    adopt = true;
                    runOnlyUsed = true;
                    break;
                case "--preserve-compatible":
                    adopt = false;
                    runOnlyUsed = true;
                    break;
                case "--deploy":
                    deploy = true;
                    runOnlyUsed = true;
                    break;
                case "--optimize":
                    optimization = ReadOptionalOptimization(args, ref index);
                    optimizationSpecified = runOnlyUsed = true;
                    break;
                case "--no-optimize":
                    optimization = null;
                    optimizationSpecified = runOnlyUsed = true;
                    break;
                case "--optimize-conservative":
                    optimization = RuleOptimizationMode.Conservative;
                    optimizationSpecified = runOnlyUsed = true;
                    break;
                case "--optimize-balanced":
                    optimization = RuleOptimizationMode.Balanced;
                    optimizationSpecified = runOnlyUsed = true;
                    break;
                case "--optimize-aggressive":
                    optimization = RuleOptimizationMode.Aggressive;
                    optimizationSpecified = runOnlyUsed = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--write-artifacts":
                    writeArtifacts = true;
                    runOnlyUsed = true;
                    break;
                case "--history-limit":
                    historyLimit = ReadNonNegativeInt(args, ref index, option);
                    runOnlyUsed = true;
                    break;
                case "--no-prune-history":
                    pruneHistory = false;
                    runOnlyUsed = true;
                    break;
                case "--sieve-host":
                    sieveHost = ReadValue(args, ref index, option);
                    sieveUsed = true;
                    break;
                case "--sieve-port":
                    sievePort = ReadPort(args, ref index, option);
                    sieveUsed = true;
                    break;
                case "--sieve-username":
                    sieveUsername = ReadValue(args, ref index, option);
                    sieveUsed = true;
                    break;
                case "--sieve-password":
                    sievePassword = ReadValue(args, ref index, option);
                    sieveUsed = true;
                    break;
                case "--sieve-security-mode":
                    sieveSecurity = ParseSecurity(ReadValue(args, ref index, option));
                    sieveUsed = true;
                    break;
                case "-h":
                case "--help":
                    return new CommandLineOptions { ShowHelp = true };
                default:
                    if (TryParseOptimizationShorthand(option, out RuleOptimizationMode shorthand))
                    {
                        optimization = shorthand;
                        optimizationSpecified = runOnlyUsed = true;
                        break;
                    }
                    throw new ArgumentException($"Unknown option: {option}");
            }
        }

        if (command == ThunderbirdResieverCommand.Export && (runOnlyUsed || sieveUsed))
            throw new ArgumentException("tbrx export only accepts --profile, --filters, --rules, and --dry-run.");
        if (command == ThunderbirdResieverCommand.Rollback && (runOnlyUsed || sourceUsed || rulesUsed))
            throw new ArgumentException("tbrx rollback only accepts --dry-run and --sieve-* options.");
        if (command == ThunderbirdResieverCommand.Run && artifactPathUsed && !writeArtifacts)
            throw new ArgumentException("Artifact path options require --write-artifacts for tbrx run.");
        if (command == ThunderbirdResieverCommand.Run && rulesUsed && !writeArtifacts)
            throw new ArgumentException("--rules requires --write-artifacts for tbrx run.");

        return new CommandLineOptions
        {
            Command = command,
            ProfileDirectory = profile,
            FiltersFile = filters,
            AllowPartial = allowPartial,
            RulesFile = rules,
            CandidateFile = candidate,
            ReconciledRulesFile = reconciled,
            CandidateRulesFile = candidateRules,
            ServerSnapshotFile = snapshot,
            PlanFile = plan,
            ScriptName = scriptName,
            OptimizationMode = optimization,
            OptimizationChoiceSpecified = optimizationSpecified,
            AdoptCompatible = adopt,
            Deploy = deploy,
            DryRun = dryRun,
            WriteArtifacts = writeArtifacts,
            HistoryLimit = historyLimit,
            PruneHistory = pruneHistory,
            SieveHost = sieveHost,
            SievePort = sievePort,
            SieveUserName = sieveUsername,
            SievePassword = sievePassword,
            SieveSecurity = sieveSecurity
        };
    }

    private static string ReadValue(IReadOnlyList<string> args, ref int index, string option)
    {
        index++;
        if (index >= args.Count || args[index].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"{option} requires a value.");
        return args[index];
    }

    private static int ReadNonNegativeInt(IReadOnlyList<string> args, ref int index, string option)
    {
        string value = ReadValue(args, ref index, option);
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed < 0)
            throw new ArgumentException($"{option} must be a non-negative integer.");
        return parsed;
    }

    private static int ReadPort(IReadOnlyList<string> args, ref int index, string option)
    {
        int port = ReadNonNegativeInt(args, ref index, option);
        if (port is < 1 or > 65535)
            throw new ArgumentException($"{option} must be a TCP port from 1 to 65535.");
        return port;
    }

    private static RuleOptimizationMode ReadOptionalOptimization(IReadOnlyList<string> args, ref int index)
    {
        int next = index + 1;
        if (next >= args.Count || args[next].StartsWith("-", StringComparison.Ordinal))
            return RuleOptimizationMode.Conservative;
        index = next;
        return ParseOptimization(args[next]);
    }

    private static RuleOptimizationMode ParseOptimization(string value) =>
        Enum.TryParse(value, true, out RuleOptimizationMode mode)
            ? mode
            : throw new ArgumentException($"Unknown optimization mode: {value}");

    private static SieveConnectionSecurity ParseSecurity(string value)
    {
        if (!Enum.TryParse(value, true, out SieveConnectionSecurity security))
        {
            throw new ArgumentException($"Unsupported Sieve security mode: {value}");
        }
        return security;
    }

    private static bool TryParseOptimizationShorthand(string value, out RuleOptimizationMode mode)
    {
        mode = default;
        if (value.Length < 2 || value[0] != '-' || value[1..].Any(character => character != 'o'))
            return false;
        mode = value.Length switch
        {
            2 => RuleOptimizationMode.Conservative,
            3 => RuleOptimizationMode.Balanced,
            _ => RuleOptimizationMode.Aggressive
        };
        return true;
    }

    private static bool IsHelp(string value) => value is "-h" or "--help" or "help";
}
