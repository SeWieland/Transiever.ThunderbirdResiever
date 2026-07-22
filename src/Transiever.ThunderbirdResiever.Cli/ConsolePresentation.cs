using Transiever.SieveRuler.Models;
using Transiever.ThunderbirdResiever.Services;

namespace Transiever.ThunderbirdResiever.Cli;

public static class ConsolePresentation
{
    public static void PrintHelp()
    {
        Console.WriteLine("tbrx experimental beta - testers wanted");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  tbrx run       Read one Thunderbird account, preview, then optionally deploy.");
        Console.WriteLine("  tbrx export    Write supported filters to SieveRuler schema-v1 JSON.");
        Console.WriteLine("  tbrx rollback  Restore the newest inactive SieveRuler backup.");
        Console.WriteLine();
        Console.WriteLine("Thunderbird source options (run/export):");
        Console.WriteLine("  --profile <directory>     Select a relocated Thunderbird profile.");
        Console.WriteLine("  --filters <file>          Select an exact msgFilterRules.dat account source.");
        Console.WriteLine("  --allow-partial           Continue after enabled filters are skipped (run only).");
        Console.WriteLine();
        Console.WriteLine("Workflow options:");
        Console.WriteLine("  --write-artifacts         Write review files during run.");
        Console.WriteLine("  --rules <file>            Rules JSON destination/review path.");
        Console.WriteLine("  --candidate <file>        Candidate Sieve path.");
        Console.WriteLine("  --reconciled-rules <file> Reconciled rules path.");
        Console.WriteLine("  --candidate-rules <file>  Rendered managed rules path.");
        Console.WriteLine("  --server-snapshot <file>  Active server snapshot path.");
        Console.WriteLine("  --plan <file>             Deployment plan path.");
        Console.WriteLine("  --script-name <name>      Override the target script name.");
        Console.WriteLine("  --adopt-compatible | --preserve-compatible");
        Console.WriteLine("  --deploy                  Deploy after preview without prompting.");
        Console.WriteLine("  --history-limit <count> | --no-prune-history");
        Console.WriteLine("  --dry-run                 Do not write files or mutate the server.");
        Console.WriteLine("  --optimize [conservative|balanced|aggressive]");
        Console.WriteLine("  --no-optimize | -o | -oo | -ooo");
        Console.WriteLine();
        Console.WriteLine("ManageSieve overrides (run/rollback):");
        Console.WriteLine("  --sieve-host <host> --sieve-port <port> --sieve-username <name>");
        Console.WriteLine("  --sieve-password <value> --sieve-security-mode <mode>");
        Console.WriteLine();
        Console.WriteLine("Running without arguments only prints this help and performs no external access.");
    }

    public static void PrintDiagnostics(IEnumerable<ThunderbirdExportDiagnostic> diagnostics)
    {
        foreach (ThunderbirdExportDiagnostic diagnostic in diagnostics)
        {
            string rule = diagnostic.RuleName is null ? "" : $" Rule '{diagnostic.RuleName}':";
            Console.Error.WriteLine($"[{diagnostic.Severity}] {diagnostic.Code}:{rule} {diagnostic.Message}");
        }
    }

    public static void PrintReconciliationDiagnostics(IEnumerable<ReconciliationDiagnostic> diagnostics)
    {
        foreach (ReconciliationDiagnostic diagnostic in diagnostics)
            Console.WriteLine($"[{diagnostic.Severity}] {diagnostic.Code}: {diagnostic.Message}");
    }
}
