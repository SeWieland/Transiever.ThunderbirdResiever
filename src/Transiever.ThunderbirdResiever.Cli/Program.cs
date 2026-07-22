using Transiever.SieveRuler.Application;
using Transiever.SieveRuler.Services;
using Transiever.ThunderbirdResiever.Application;
using Transiever.ThunderbirdResiever.Services;

namespace Transiever.ThunderbirdResiever.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        CommandLineOptions options;
        try
        {
            options = CommandLineOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            ConsolePresentation.PrintHelp();
            return 1;
        }

        if (options.ShowHelp)
        {
            ConsolePresentation.PrintHelp();
            return 0;
        }

        IRuleSerializer serializer = new JsonRuleSerializer();
        IRuleOptimizer optimizer = new RuleOptimizer();
        ISieveGenerator generator = new SieveGenerator();
        ISieveImporter importer = new SieveImporter();
        IRuleReconciler reconciler = new RuleReconciler(optimizer);
        ISieveScriptComposer composer = new SieveScriptComposer(importer, generator);
        var interaction = new ConsoleThunderbirdInteraction();
        ISieveSynchronizationWorkflow synchronization = new SieveSynchronizationWorkflow(
            serializer,
            importer,
            reconciler,
            composer,
            new ManageSieveServerConnectionFactory(),
            interaction);
        var cli = new ThunderbirdResieverCliApplication(
            new ThunderbirdExportApplication(
                new ThunderbirdProfileLocator(),
                new ThunderbirdRuleExporter(),
                serializer),
            synchronization,
            new EnvironmentSieveServerConfigurationProvider(),
            interaction);

        try
        {
            return await cli.RunAsync(options);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
