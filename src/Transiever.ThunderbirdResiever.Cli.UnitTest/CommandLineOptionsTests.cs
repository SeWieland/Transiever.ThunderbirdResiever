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

    [Fact]
    public void Run_rejects_password_option_without_displaying_its_value()
    {
        const string password = "secret";

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => CommandLineOptions.Parse(["run", "--sieve-password", password]));

        Assert.Equal("Passwords cannot be supplied through --sieve-password.", exception.Message);
        Assert.DoesNotContain(password, exception.Message);
    }

    [Theory]
    [InlineData("--sieve-password=secret")]
    [InlineData("--sieve-password-stdin=secret")]
    public void Run_redacts_password_option_values(string option)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => CommandLineOptions.Parse(["run", option]));

        Assert.Equal("Secret input cannot be supplied through an option value.", exception.Message);
        Assert.DoesNotContain("secret", exception.Message);
    }

    [Theory]
    [InlineData("--sieve-password=secret")]
    [InlineData("--sieve-password-stdin", "secret")]
    [InlineData("--sieve-password-stdin", "-secret")]
    public void Run_redacts_password_values_at_every_position(params string[] values)
    {
        string[] args = values.Length == 1 ? values : ["run", ..values];
        Assert.DoesNotContain("secret", Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(args)).Message);
    }
}
