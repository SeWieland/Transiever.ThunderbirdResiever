namespace Transiever.ThunderbirdResiever.Cli.UnitTest;

public sealed class CommandLineOptionsTests
{
    [Fact]
    public void No_arguments_select_help()
    {
        Assert.True(CommandLineOptions.Parse([]).ShowHelp);
    }

    [Fact]
    public void Run_accepts_source_partial_and_shared_options()
    {
        CommandLineOptions options = CommandLineOptions.Parse(
            ["run", "--filters", "rules.dat", "--allow-partial", "--deploy", "--no-optimize", "--sieve-host", "example.invalid"]);

        Assert.Equal(ThunderbirdResieverCommand.Run, options.Command);
        Assert.Equal("rules.dat", options.FiltersFile);
        Assert.True(options.AllowPartial);
        Assert.True(options.Deploy);
        Assert.True(options.OptimizationChoiceSpecified);
    }

    [Theory]
    [InlineData("export", "--deploy")]
    [InlineData("export", "--sieve-host", "example.invalid")]
    [InlineData("rollback", "--profile", "profile")]
    [InlineData("rollback", "--rules", "rules.json")]
    public void Commands_reject_options_owned_by_other_workflows(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(args));
    }

    [Fact]
    public void Run_requires_write_artifacts_for_artifact_paths()
    {
        Assert.Throws<ArgumentException>(
            () => CommandLineOptions.Parse(["run", "--candidate", "candidate.sieve"]));
    }
}
