using System.Text;

namespace Transiever.ThunderbirdResiever.Services;

public sealed class ThunderbirdProfileLocator(
    IReadOnlyList<string>? discoveryRoots = null,
    IStableFileReader? fileReader = null)
    : IThunderbirdRuleSourceDiscovery
{
    private readonly IReadOnlyList<string> roots =
        discoveryRoots ?? DefaultDiscoveryRoots();
    private readonly IStableFileReader reader = fileReader ?? new StableFileReader();

    public ThunderbirdSourceDiscoveryResult Discover(ThunderbirdSourceRequest request)
    {
        string? requestedFilters = FullPathOrNull(request.FiltersFile);
        var sources = new List<ThunderbirdRuleSource>();
        var diagnostics = new List<ThunderbirdExportDiagnostic>();
        string? requestedProfile = FullPathOrNull(request.ProfileDirectory);
        if (requestedProfile is not null &&
            !File.Exists(Path.Combine(requestedProfile, "prefs.js")))
        {
            diagnostics.Add(new ThunderbirdExportDiagnostic(
                "Error",
                "TBRX_PROFILE_INVALID",
                $"The selected Thunderbird profile does not contain prefs.js: {requestedProfile}"));
            return new ThunderbirdSourceDiscoveryResult(sources, diagnostics, false);
        }

        bool isComplete = true;
        IReadOnlyList<string> profiles = ResolveProfiles(
            request,
            requestedFilters,
            diagnostics,
            ref isComplete);

        foreach (string profile in profiles)
            isComplete &= DiscoverProfile(profile, requestedFilters, sources, diagnostics);

        IReadOnlyList<ThunderbirdRuleSource> distinct =
            CollapseMappings(sources, diagnostics, ref isComplete);

        if (requestedFilters is not null && distinct.Count == 0)
        {
            isComplete = false;
            diagnostics.Add(new ThunderbirdExportDiagnostic(
                "Error",
                "TBRX_SOURCE_NOT_MAPPED",
                "The selected filter file could not be associated with a Thunderbird account in prefs.js."));
        }

        return new ThunderbirdSourceDiscoveryResult(distinct, diagnostics, isComplete);
    }

    private IReadOnlyList<string> ResolveProfiles(
        ThunderbirdSourceRequest request,
        string? requestedFilters,
        List<ThunderbirdExportDiagnostic> diagnostics,
        ref bool isComplete)
    {
        if (!string.IsNullOrWhiteSpace(request.ProfileDirectory))
            return [Path.GetFullPath(request.ProfileDirectory)];

        if (requestedFilters is not null)
        {
            DirectoryInfo? directory = new FileInfo(requestedFilters).Directory;
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "prefs.js")))
                    return [directory.FullName];
                directory = directory.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate prefs.js above the selected filter file. Pass --profile as well.");
        }

        var profiles = new List<string>();
        foreach (string rootValue in roots.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            string root = Path.GetFullPath(rootValue);
            string ini = Path.Combine(root, "profiles.ini");
            if (File.Exists(ini))
                profiles.AddRange(ReadProfilesIni(root, ini, diagnostics, ref isComplete));
            else if (File.Exists(Path.Combine(root, "prefs.js")))
                profiles.Add(root);
        }

        return profiles
            .Distinct(PathComparer())
            .OrderBy(path => path, PathComparer())
            .ToArray();
    }

    private bool DiscoverProfile(
        string profile,
        string? requestedFilters,
        List<ThunderbirdRuleSource> sources,
        List<ThunderbirdExportDiagnostic> diagnostics)
    {
        bool isComplete = true;
        string prefsPath = Path.Combine(profile, "prefs.js");
        if (!File.Exists(prefsPath))
        {
            diagnostics.Add(new ThunderbirdExportDiagnostic(
                "Warning",
                "TBRX_PREFS_MISSING",
                $"Skipped profile without prefs.js: {profile}"));
            return false;
        }

        Dictionary<string, string> preferences = ParseStringPreferences(
            DecodeUtf8(reader.ReadAllBytes(prefsPath), prefsPath));
        string prefix = "mail.server.";
        IEnumerable<string> serverKeys = preferences.Keys
            .Where(key => key.StartsWith(prefix, StringComparison.Ordinal) &&
                key.EndsWith(".type", StringComparison.Ordinal))
            .Select(key => key[prefix.Length..^".type".Length])
            .Concat(preferences
                .Where(pair =>
                    pair.Key.StartsWith("mail.account.", StringComparison.Ordinal) &&
                    pair.Key.EndsWith(".server", StringComparison.Ordinal))
                .Select(pair => pair.Value))
            .Distinct(StringComparer.Ordinal);

        foreach (string serverKey in serverKeys)
        {
            string keyPrefix = $"mail.server.{serverKey}.";
            string? localDirectory = ResolveServerDirectory(profile, keyPrefix, preferences);
            if (requestedFilters is not null)
            {
                if (localDirectory is null)
                    continue;

                string candidateFilters = Path.GetFullPath(Path.Combine(localDirectory, "msgFilterRules.dat"));
                if (!PathComparer().Equals(candidateFilters, requestedFilters))
                    continue;
            }

            if (!preferences.TryGetValue(keyPrefix + "type", out string? serverType) ||
                !preferences.TryGetValue(keyPrefix + "hostname", out string? hostname) ||
                !preferences.TryGetValue(keyPrefix + "userName", out string? username))
            {
                diagnostics.Add(new ThunderbirdExportDiagnostic(
                    "Warning",
                    "TBRX_ACCOUNT_INCOMPLETE",
                    $"Skipped Thunderbird server '{serverKey}' because type, hostname, or username is missing."));
                isComplete = false;
                continue;
            }

            if (localDirectory is null)
            {
                diagnostics.Add(new ThunderbirdExportDiagnostic(
                    "Warning",
                    "TBRX_ACCOUNT_DIRECTORY_MISSING",
                    $"Skipped Thunderbird server '{serverKey}' because its local directory is unresolved."));
                isComplete = false;
                continue;
            }

            string filters = Path.GetFullPath(Path.Combine(localDirectory, "msgFilterRules.dat"));
            if (!File.Exists(filters))
            {
                diagnostics.Add(new ThunderbirdExportDiagnostic(
                    "Warning",
                    "TBRX_FILTERS_MISSING",
                    $"Skipped Thunderbird server '{serverKey}' because msgFilterRules.dat is missing: {filters}"));
                isComplete = false;
                continue;
            }

            string accountKey = preferences
                .Where(pair =>
                    pair.Key.StartsWith("mail.account.", StringComparison.Ordinal) &&
                    pair.Key.EndsWith(".server", StringComparison.Ordinal) &&
                    pair.Value.Equals(serverKey, StringComparison.Ordinal))
                .Select(pair => pair.Key["mail.account.".Length..^".server".Length])
                .Order(StringComparer.Ordinal)
                .FirstOrDefault() ?? serverKey;

            sources.Add(new ThunderbirdRuleSource(
                Path.GetFullPath(profile),
                filters,
                accountKey,
                serverKey,
                serverType,
                hostname,
                username));

            if (!serverType.Equals("imap", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new ThunderbirdExportDiagnostic(
                    "Warning",
                    "TBRX_ACCOUNT_NOT_IMAP",
                    $"Account '{username}@{hostname}' uses '{serverType}' and cannot be deployed by tbrx."));
            }
        }

        return isComplete;
    }

    private static IReadOnlyList<ThunderbirdRuleSource> CollapseMappings(
        IEnumerable<ThunderbirdRuleSource> mappings,
        List<ThunderbirdExportDiagnostic> diagnostics,
        ref bool isComplete)
    {
        var collapsed = new List<ThunderbirdRuleSource>();
        foreach (IGrouping<string, ThunderbirdRuleSource> group in mappings.GroupBy(
            source => Path.GetFullPath(source.FiltersFile),
            PathComparer()))
        {
            if (group.Select(source => source.SourceId).Distinct(StringComparer.Ordinal).Skip(1).Any())
            {
                diagnostics.Add(new ThunderbirdExportDiagnostic(
                    "Error",
                    "TBRX_SOURCE_AMBIGUOUS",
                    $"The filter file maps to more than one Thunderbird account identity: {group.Key}"));
                isComplete = false;
                continue;
            }

            collapsed.Add(group
                .OrderBy(source => source.AccountKey, StringComparer.Ordinal)
                .ThenBy(source => source.ServerKey, StringComparer.Ordinal)
                .First());
        }

        return collapsed
            .OrderBy(source => source.Hostname, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.Username, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.FiltersFile, PathComparer())
            .ToArray();
    }

    private static string? ResolveServerDirectory(
        string profile,
        string keyPrefix,
        IReadOnlyDictionary<string, string> preferences)
    {
        if (preferences.TryGetValue(keyPrefix + "directory-rel", out string? relative))
        {
            const string profileToken = "[ProfD]";
            if (relative.StartsWith(profileToken, StringComparison.Ordinal))
            {
                string suffix = relative[profileToken.Length..]
                    .TrimStart('/', '\\')
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar);
                return Path.GetFullPath(Path.Combine(profile, suffix));
            }
        }

        if (preferences.TryGetValue(keyPrefix + "directory", out string? absolute) &&
            Path.IsPathRooted(absolute))
        {
            return Path.GetFullPath(absolute);
        }

        return null;
    }

    private IReadOnlyList<string> ReadProfilesIni(
        string root,
        string path,
        List<ThunderbirdExportDiagnostic> diagnostics,
        ref bool isComplete)
    {
        string text = DecodeUtf8(reader.ReadAllBytes(path), path);
        Dictionary<string, Dictionary<string, string>> sections = ParseIni(text);
        var profiles = new List<string>();
        foreach ((string name, Dictionary<string, string> values) in sections)
        {
            if (!name.StartsWith("Profile", StringComparison.OrdinalIgnoreCase) ||
                !values.TryGetValue("Path", out string? configuredPath))
            {
                continue;
            }

            bool relative = !values.TryGetValue("IsRelative", out string? flag) || flag == "1";
            if (relative == Path.IsPathRooted(configuredPath))
            {
                diagnostics.Add(new ThunderbirdExportDiagnostic(
                    "Warning",
                    "TBRX_PROFILE_PATH_INVALID",
                    $"Skipped profiles.ini entry '{name}' because IsRelative={flag ?? "1"} does not match " +
                    $"path '{configuredPath}'. Use IsRelative=1 for relative paths and 0 for rooted paths."));
                isComplete = false;
                continue;
            }

            profiles.Add(Path.GetFullPath(relative
                ? Path.Combine(root, configuredPath.Replace('/', Path.DirectorySeparatorChar))
                : configuredPath));
        }

        return profiles;
    }

    private static Dictionary<string, Dictionary<string, string>> ParseIni(string text)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? current = null;
        foreach (string rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] is '#' or ';')
                continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                result[line[1..^1]] = current;
                continue;
            }

            int equals = line.IndexOf('=');
            if (current is not null && equals > 0)
                current[line[..equals].Trim()] = line[(equals + 1)..].Trim();
        }

        return result;
    }

    internal static Dictionary<string, string> ParseStringPreferences(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (!line.StartsWith("user_pref(", StringComparison.Ordinal) ||
                !line.EndsWith(");", StringComparison.Ordinal))
            {
                continue;
            }

            int index = "user_pref(".Length;
            if (!TryReadJavaScriptString(line, ref index, out string key))
                continue;
            SkipWhitespace(line, ref index);
            if (index >= line.Length || line[index++] != ',')
                continue;
            SkipWhitespace(line, ref index);
            if (!TryReadJavaScriptString(line, ref index, out string value))
                continue;
            SkipWhitespace(line, ref index);
            if (line.AsSpan(index).SequenceEqual(");"))
                result[key] = value;
        }

        return result;
    }

    private static bool TryReadJavaScriptString(
        string text,
        ref int index,
        out string value)
    {
        value = "";
        if (index >= text.Length || text[index++] != '"')
            return false;

        var builder = new StringBuilder();
        while (index < text.Length)
        {
            char character = text[index++];
            if (character == '"')
            {
                value = builder.ToString();
                return true;
            }

            if (character != '\\' || index >= text.Length)
            {
                builder.Append(character);
                continue;
            }

            char escaped = text[index++];
            builder.Append(escaped switch
            {
                '\\' => '\\',
                '"' => '"',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                _ => escaped
            });
        }

        return false;
    }

    private static void SkipWhitespace(string text, ref int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;
    }

    private static string DecodeUtf8(byte[] bytes, string path)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException($"Thunderbird file is not valid UTF-8: {path}", exception);
        }
    }

    private static string? FullPathOrNull(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static IReadOnlyList<string> DefaultDiscoveryRoots() =>
        DefaultDiscoveryRoots(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            OperatingSystem.IsWindows());

    internal static IReadOnlyList<string> DefaultDiscoveryRoots(
        string? appData,
        string? home,
        bool isWindows)
    {
        var result = new List<string>();
        if (isWindows && !string.IsNullOrWhiteSpace(appData))
            result.Add(Path.Combine(appData, "Thunderbird"));

        if (!isWindows && !string.IsNullOrWhiteSpace(home))
        {
            result.Add(Path.Combine(home, ".thunderbird"));
            result.Add(Path.Combine(home, ".var", "app", "org.mozilla.Thunderbird", ".thunderbird"));
            result.Add(Path.Combine(home, "snap", "thunderbird", "common", ".thunderbird"));
        }

        return result;
    }
}
