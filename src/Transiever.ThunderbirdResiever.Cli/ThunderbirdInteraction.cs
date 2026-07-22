using Transiever.SieveRuler.Application;
using Transiever.SieveRuler.Models;
using Transiever.ThunderbirdResiever.Services;

namespace Transiever.ThunderbirdResiever.Cli;

public interface IThunderbirdRunInteraction
{
    ThunderbirdRuleSource ResolveSource(IReadOnlyList<ThunderbirdRuleSource> sources);
    bool ConfirmPartial(bool explicitlyAllowed, int exportedCount, int skippedCount);
    RuleOptimizationMode? ResolveOptimization(RuleOptimizationMode? explicitMode, bool explicitChoice);
    bool ConfirmUpload(bool explicitlyDeploy, string scriptName);
}

public sealed class ConsoleThunderbirdInteraction :
    IThunderbirdRunInteraction,
    ISynchronizationInteraction
{
    public ThunderbirdRuleSource ResolveSource(IReadOnlyList<ThunderbirdRuleSource> sources)
    {
        if (sources.Count == 1)
            return sources[0];
        if (sources.Count == 0)
            throw new InvalidOperationException("No Thunderbird account filter source was found.");
        if (Console.IsInputRedirected)
            throw new InvalidOperationException("Several Thunderbird accounts were found. Redirected input must pass --filters <file>.");

        Console.WriteLine("Select one Thunderbird account:");
        for (int index = 0; index < sources.Count; index++)
            Console.WriteLine($"  {index + 1}. {sources[index].Username}@{sources[index].Hostname} ({sources[index].ServerType})");
        while (true)
        {
            Console.Write($"Account [1-{sources.Count}]: ");
            if (int.TryParse(Console.ReadLine(), out int selected) && selected >= 1 && selected <= sources.Count)
                return sources[selected - 1];
            Console.WriteLine("Enter one listed account number.");
        }
    }

    public bool ConfirmPartial(bool explicitlyAllowed, int exportedCount, int skippedCount)
    {
        if (explicitlyAllowed)
            return true;
        if (Console.IsInputRedirected)
            return false;
        Console.Write($"Exported {exportedCount} rules but skipped {skippedCount} enabled rules. Continue with this partial result? [y/N] ");
        return IsYes(Console.ReadLine());
    }

    public RuleOptimizationMode? ResolveOptimization(RuleOptimizationMode? explicitMode, bool explicitChoice)
    {
        if (explicitChoice)
            return explicitMode;
        if (Console.IsInputRedirected)
            return null;
        Console.Write("Optimize managed rules? [n]one/[c]onservative/[b]alanced/[a]ggressive: ");
        return Console.ReadLine()?.Trim().ToLowerInvariant() switch
        {
            "c" or "conservative" => RuleOptimizationMode.Conservative,
            "b" or "balanced" => RuleOptimizationMode.Balanced,
            "a" or "aggressive" => RuleOptimizationMode.Aggressive,
            _ => null
        };
    }

    public bool ConfirmUpload(bool explicitlyDeploy, string scriptName)
    {
        if (explicitlyDeploy)
            return true;
        if (Console.IsInputRedirected)
            return false;
        Console.Write($"Deploy candidate for target script '{scriptName}'? [y/N] ");
        return IsYes(Console.ReadLine());
    }

    public bool ResolveAdoption(bool? explicitChoice, int compatibleRuleCount)
    {
        if (compatibleRuleCount == 0)
            return false;
        if (explicitChoice is { } choice)
            return choice;
        if (Console.IsInputRedirected)
            return false;
        Console.Write($"Adopt {compatibleRuleCount} compatible server rules? [Y/n] ");
        string? answer = Console.ReadLine();
        return string.IsNullOrWhiteSpace(answer) || IsYes(answer);
    }

    private static bool IsYes(string? value) =>
        value?.Equals("y", StringComparison.OrdinalIgnoreCase) == true ||
        value?.Equals("yes", StringComparison.OrdinalIgnoreCase) == true;
}
