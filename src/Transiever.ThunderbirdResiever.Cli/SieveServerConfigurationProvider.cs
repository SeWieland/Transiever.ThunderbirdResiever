using System.Text;
using Transiever.SieveRuler.Services;

namespace Transiever.ThunderbirdResiever.Cli;

public interface ISieveServerConfigurationProvider
{
    SieveServerConfiguration GetConfiguration(CommandLineOptions options);
}

public sealed class EnvironmentSieveServerConfigurationProvider : ISieveServerConfigurationProvider
{
    public SieveServerConfiguration GetConfiguration(CommandLineOptions options)
    {
        string host = options.SieveHost ?? Required("HOST");
        string username = options.SieveUserName ?? Required("USERNAME");
        string password = options.SievePassword ?? Read("PASSWORD") ?? ReadPassword();
        int port = options.SievePort ?? (int.TryParse(Read("PORT"), out int configuredPort)
            ? configuredPort
            : SieveServerConfiguration.DefaultPort);
        string? configuredSecurity = Read("SECURITY_MODE");
        if (configuredSecurity?.Equals("PlainText", StringComparison.OrdinalIgnoreCase) == true)
            throw new InvalidOperationException("tbrx refuses to send credentials over plaintext ManageSieve.");
        SieveConnectionSecurity security = options.SieveSecurity ??
            (Enum.TryParse(configuredSecurity, true, out SieveConnectionSecurity configured)
                ? configured
                : SieveConnectionSecurity.StartTlsRequired);
        return new SieveServerConfiguration(host, port, username, password, security);
    }

    private static string Required(string suffix) =>
        Read(suffix) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Environment variable TRANSIEVER_SIEVE_{suffix} is required.");

    private static string? Read(string suffix) =>
        Environment.GetEnvironmentVariable($"TRANSIEVER_SIEVE_{suffix}");

    private static string ReadPassword()
    {
        if (Console.IsInputRedirected)
            throw new InvalidOperationException("TRANSIEVER_SIEVE_PASSWORD is required when input is redirected.");
        Console.Write("ManageSieve password: ");
        var password = new StringBuilder();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                password.Length--;
            else if (!char.IsControl(key.KeyChar))
                password.Append(key.KeyChar);
        }
        Console.WriteLine();
        return password.ToString();
    }
}
