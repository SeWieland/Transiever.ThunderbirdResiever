using Transiever.SieveRuler.Services;

namespace Transiever.ThunderbirdResiever.Cli.UnitTest;

[Collection(ConsoleCaptureCollection.Name)]
public sealed class ConfigurationProviderTests
{
    [Fact]
    public void StandardInputPassword_RejectsTerminalWithoutReading()
    {
        TextReader originalInput = Console.In;
        using var input = new StringReader("terminal-password\n");
        Console.SetIn(input);
        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => EnvironmentSieveServerConfigurationProvider.ReadStandardInputPassword(inputIsRedirected: false));

            Assert.Contains("redirected standard input", exception.Message);
            Assert.Equal("terminal-password", input.ReadLine());
        }
        finally
        {
            Console.SetIn(originalInput);
        }
    }

    [Fact]
    public void Provider_DoesNotReadStandardInputWithoutTheSelector()
    {
        Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_HOST", "sieve.test");
        Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_USERNAME", "user");
        Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_PASSWORD", "environment-password");
        TextReader originalInput = Console.In;
        Console.SetIn(new StringReader("stdin-sentinel\n"));
        try
        {
            SieveServerConfiguration configuration = new EnvironmentSieveServerConfigurationProvider()
                .GetConfiguration(CommandLineOptions.Parse(["run"]));

            Assert.Equal("environment-password", configuration.Password);
            Assert.Equal("stdin-sentinel", Console.In.ReadLine());
        }
        finally
        {
            Console.SetIn(originalInput);
            Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_HOST", null);
            Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_USERNAME", null);
            Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_PASSWORD", null);
        }
    }

    [Fact]
    public void Provider_UsesExplicitStandardInputPasswordBeforeEnvironment()
    {
        Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_HOST", "sieve.test");
        Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_USERNAME", "user");
        Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_PASSWORD", "environment-password");
        TextReader originalInput = Console.In;
        Console.SetIn(new StringReader("stdin-password\nunused\n"));
        try
        {
            SieveServerConfiguration configuration = new EnvironmentSieveServerConfigurationProvider()
                .GetConfiguration(CommandLineOptions.Parse(["run", "--sieve-password-stdin"]));

            Assert.Equal("stdin-password", configuration.Password);
            Assert.Equal("unused", Console.In.ReadLine());
        }
        finally
        {
            Console.SetIn(originalInput);
            Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_HOST", null);
            Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_USERNAME", null);
            Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_PASSWORD", null);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Provider_RejectsMissingExplicitStandardInputPasswordEvenWithEnvironmentPassword(bool emptyLine)
    {
        Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_HOST", "sieve.test");
        Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_USERNAME", "user");
        Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_PASSWORD", "environment-password");
        TextReader originalInput = Console.In;
        Console.SetIn(new StringReader(emptyLine ? "\n" : ""));
        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new EnvironmentSieveServerConfigurationProvider()
                .GetConfiguration(CommandLineOptions.Parse(["run", "--sieve-password-stdin"])));

            Assert.Equal("Standard input did not contain a Sieve password.", exception.Message);
        }
        finally
        {
            Console.SetIn(originalInput);
            Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_HOST", null);
            Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_USERNAME", null);
            Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_PASSWORD", null);
        }
    }
}
