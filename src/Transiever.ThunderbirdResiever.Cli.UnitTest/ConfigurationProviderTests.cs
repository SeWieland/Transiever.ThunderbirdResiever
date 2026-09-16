using Transiever.SieveRuler.Services;

namespace Transiever.ThunderbirdResiever.Cli.UnitTest;

public sealed class ConfigurationProviderTests
{
    [Fact]
    public void Provider_DoesNotReadStandardInputWithoutTheSelector()
    {
        Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_HOST", "sieve.test");
        Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_USERNAME", "user");
        Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_PASSWORD", "environment-password");
        TextReader originalInput = Console.In; Console.SetIn(new StringReader("stdin-sentinel\n"));
        try
        {
            new EnvironmentSieveServerConfigurationProvider().GetConfiguration(CommandLineOptions.Parse(["run"]));
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
    [InlineData("")]
    [InlineData("\n")]
    public void Provider_RejectsMissingExplicitStandardInputPassword(string input)
    {
        Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_HOST", "sieve.test");
        Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_USERNAME", "user");
        TextReader originalInput = Console.In; Console.SetIn(new StringReader(input));
        try
        {
            Assert.Throws<InvalidOperationException>(() => new EnvironmentSieveServerConfigurationProvider()
                .GetConfiguration(CommandLineOptions.Parse(["run", "--sieve-password-stdin"])));
        }
        finally
        {
            Console.SetIn(originalInput);
            Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_HOST", null);
            Environment.SetEnvironmentVariable("TRANSIEVER_SIEVE_USERNAME", null);
        }
    }
}
