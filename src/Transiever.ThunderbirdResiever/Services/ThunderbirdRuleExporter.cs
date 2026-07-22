using System.Text;
using Transiever.SieveRuler.Models;

namespace Transiever.ThunderbirdResiever.Services;

public sealed class ThunderbirdRuleExporter(IStableFileReader? fileReader = null)
    : IThunderbirdRuleExporter
{
    private readonly IStableFileReader reader = fileReader ?? new StableFileReader();

    public ThunderbirdRuleExportResult Export(ThunderbirdRuleSource source)
    {
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(
                reader.ReadAllBytes(source.FiltersFile));
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                "msgFilterRules.dat must be valid UTF-8.",
                exception);
        }

        ParsedFilterList parsed = Parse(text);
        var rules = new List<RuleDefinition>();
        var diagnostics = new List<ThunderbirdExportDiagnostic>();
        int enabledCount = 0;
        int skippedCount = 0;

        for (int index = 0; index < parsed.Rules.Count; index++)
        {
            RawRule raw = parsed.Rules[index];
            if (!raw.Enabled)
                continue;

            enabledCount++;
            if (TryMapRule(raw, source, index, out RuleDefinition? mapped, out string? reason))
            {
                rules.Add(mapped!);
            }
            else
            {
                skippedCount++;
                diagnostics.Add(new ThunderbirdExportDiagnostic(
                    "Warning",
                    "TBRX_RULE_SKIPPED",
                    reason ?? "The rule could not be mapped without changing meaning.",
                    raw.Name));
            }
        }

        return new ThunderbirdRuleExportResult(
            source,
            rules,
            diagnostics,
            enabledCount,
            skippedCount);
    }

    private static ParsedFilterList Parse(string text)
    {
        int? version = null;
        var rules = new List<RawRule>();
        RawRule? current = null;

        foreach ((string rawLine, int lineNumber) in text
            .Split('\n')
            .Select((line, index) => (line.TrimEnd('\r'), index + 1)))
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                continue;

            (string key, string value) = ParseAttribute(rawLine, lineNumber);
            if (key == "version")
            {
                if (current is not null || version is not null || !int.TryParse(value, out int parsedVersion))
                    throw ParseError(lineNumber, "Invalid or misplaced filter version.");
                version = parsedVersion;
                continue;
            }

            if (key == "logging")
            {
                if (current is not null || value is not ("yes" or "no"))
                    throw ParseError(lineNumber, "Invalid or misplaced logging setting.");
                continue;
            }

            if (key == "name")
            {
                if (current is not null)
                    rules.Add(current);
                current = new RawRule(value);
                continue;
            }

            if (current is null)
                throw ParseError(lineNumber, $"Attribute '{key}' appears before the first rule name.");

            switch (key)
            {
                case "enabled":
                    current.Enabled = value switch
                    {
                        "yes" => true,
                        "no" => false,
                        _ => throw ParseError(lineNumber, "enabled must be yes or no.")
                    };
                    current.HasEnabled = true;
                    break;
                case "description":
                    break;
                case "type":
                    if (!int.TryParse(value, out int type))
                        throw ParseError(lineNumber, "Rule type is not an integer.");
                    current.Type = type;
                    break;
                case "action":
                    current.Actions.Add(new RawAction(value));
                    break;
                case "actionValue":
                    if (current.Actions.Count == 0 || current.Actions[^1].Value is not null)
                        throw ParseError(lineNumber, "actionValue has no matching action.");
                    current.Actions[^1].Value = value;
                    break;
                case "condition":
                    if (current.Condition is not null)
                        throw ParseError(lineNumber, "Rule contains more than one condition field.");
                    current.Condition = value;
                    break;
                case "customId":
                case "scriptName":
                    current.UnsupportedFields.Add(key);
                    break;
                default:
                    throw ParseError(lineNumber, $"Unknown filter field '{key}'.");
            }
        }

        if (current is not null)
            rules.Add(current);
        if (version != 9)
            throw new InvalidDataException($"Unsupported Thunderbird filter version {version?.ToString() ?? "<missing>"}; only version 9 is accepted.");
        if (rules.Any(rule => !rule.HasEnabled || rule.Type is null || rule.Condition is null))
            throw new InvalidDataException("One or more Thunderbird rules omit enabled, type, or condition fields.");

        return new ParsedFilterList(rules);
    }

    private static (string Key, string Value) ParseAttribute(string line, int lineNumber)
    {
        int equals = line.IndexOf('=');
        if (equals <= 0)
            throw ParseError(lineNumber, "Expected key=\"value\".");
        string key = line[..equals].Trim();
        string encoded = line[(equals + 1)..].Trim();
        if (encoded.Length < 2 || encoded[0] != '"')
            throw ParseError(lineNumber, "Expected a quoted value.");

        var value = new StringBuilder();
        int index = 1;
        bool closed = false;
        while (index < encoded.Length)
        {
            char character = encoded[index++];
            if (character == '"')
            {
                closed = true;
                break;
            }

            if (character == '\\' && index < encoded.Length)
            {
                char next = encoded[index++];
                if (next is '"' or '\\')
                {
                    value.Append(next);
                    continue;
                }

                value.Append('\\').Append(next);
                continue;
            }

            value.Append(character);
        }

        if (!closed || encoded[index..].Trim().Length != 0)
            throw ParseError(lineNumber, "Malformed quoted value.");
        return (key, value.ToString());
    }

    private static bool TryMapRule(
        RawRule raw,
        ThunderbirdRuleSource source,
        int order,
        out RuleDefinition? definition,
        out string? reason)
    {
        definition = null;
        reason = null;
        if (raw.Type != 1)
            return Unsupported($"Filter context type '{raw.Type}' is not an inbox-only rule.", out reason);
        if (raw.UnsupportedFields.Count > 0)
            return Unsupported($"Unsupported fields: {string.Join(", ", raw.UnsupportedFields)}.", out reason);
        if (raw.Actions.Count == 0)
            return Unsupported("The enabled rule has no actions.", out reason);

        if (!TryParseConditions(raw.Condition!, out RuleConditionMode mode, out List<ParsedTerm>? terms, out reason))
            return false;

        var conditions = new List<RuleCondition>();
        var exceptions = new List<RuleCondition>();
        foreach (ParsedTerm term in terms!)
        {
            bool negative = term.Operator == "doesn't contain";
            if (!TryMapTerm(term, out RuleCondition? condition, out reason))
                return false;
            (negative ? exceptions : conditions).Add(condition!);
        }

        if (exceptions.Count > 0 && (mode != RuleConditionMode.All || conditions.Count == 0))
            return Unsupported("Negative conditions require an AND rule with at least one positive condition.", out reason);
        if (conditions.Count == 0)
            return Unsupported("The enabled rule has no supported positive conditions.", out reason);

        var actions = new List<RuleAction>();
        string targetFolder = "";
        foreach (RawAction rawAction in raw.Actions)
        {
            if (!TryMapAction(rawAction, source, out RuleAction? action, out string? folder, out reason))
                return false;
            actions.Add(action!);
            if (action!.Type == RuleActionType.FileInto && targetFolder.Length == 0)
                targetFolder = folder!;
        }

        definition = new RuleDefinition
        {
            Name = raw.Name,
            TargetFolder = targetFolder,
            ConditionMode = mode,
            Conditions = conditions,
            Exceptions = exceptions,
            Actions = actions,
            OriginalOrder = order
        };
        return true;
    }

    private static bool TryParseConditions(
        string text,
        out RuleConditionMode mode,
        out List<ParsedTerm>? terms,
        out string? reason)
    {
        mode = default;
        terms = null;
        reason = null;
        if (text.Trim().Equals("ALL", StringComparison.Ordinal))
            return Unsupported("Match-all filters are not supported.", out reason);

        var parsed = new List<ParsedTerm>();
        int index = 0;
        string? booleanWord = null;
        while (index < text.Length)
        {
            SkipSpaces(text, ref index);
            string currentWord;
            if (text.AsSpan(index).StartsWith("AND ("))
                currentWord = "AND";
            else if (text.AsSpan(index).StartsWith("OR ("))
                currentWord = "OR";
            else
                return Unsupported("Condition list is not a flat AND/OR expression.", out reason);

            if (booleanWord is not null && booleanWord != currentWord)
                return Unsupported("Mixed or grouped Boolean expressions are not supported.", out reason);
            booleanWord = currentWord;
            index += currentWord.Length + 2;
            int start = index;
            bool quoted = false;
            bool escaped = false;
            while (index < text.Length)
            {
                char character = text[index];
                if (escaped)
                    escaped = false;
                else if (character == '\\')
                    escaped = true;
                else if (character == '"')
                    quoted = !quoted;
                else if (character == ')' && !quoted)
                    break;
                index++;
            }

            if (index >= text.Length || quoted)
                return Unsupported("Condition term is unterminated.", out reason);
            string termText = text[start..index++];
            int firstComma = termText.IndexOf(',');
            int secondComma = firstComma < 0 ? -1 : termText.IndexOf(',', firstComma + 1);
            if (firstComma <= 0 || secondComma <= firstComma)
                return Unsupported("Condition term is malformed.", out reason);

            string attribute = termText[..firstComma].Trim().ToLowerInvariant();
            string op = termText[(firstComma + 1)..secondComma].Trim().ToLowerInvariant();
            string value = DecodeTermValue(termText[(secondComma + 1)..].Trim());
            parsed.Add(new ParsedTerm(attribute, op, value));
        }

        if (parsed.Count == 0 || booleanWord is null)
            return Unsupported("The enabled rule has no conditions.", out reason);
        mode = booleanWord == "AND" ? RuleConditionMode.All : RuleConditionMode.Any;
        terms = parsed;
        return true;
    }

    private static bool TryMapTerm(
        ParsedTerm term,
        out RuleCondition? condition,
        out string? reason)
    {
        condition = null;
        reason = null;
        if (term.Attribute == "has attachment status")
        {
            if (term.Operator != "is" || !term.Value.Equals("true", StringComparison.OrdinalIgnoreCase))
                return Unsupported("Only 'has attachment status is true' is supported.", out reason);
            condition = new RuleCondition { Type = RuleConditionType.HasAttachment };
            return true;
        }

        if (term.Operator is not ("contains" or "doesn't contain") ||
            string.IsNullOrWhiteSpace(term.Value))
        {
            return Unsupported($"Condition '{term.Attribute} {term.Operator}' is unsupported.", out reason);
        }

        RuleConditionType? type = term.Attribute switch
        {
            "from" => RuleConditionType.SenderContains,
            "to or cc" => RuleConditionType.ReceiverContains,
            "subject" => RuleConditionType.SubjectContains,
            "body" => RuleConditionType.BodyContains,
            _ => null
        };
        if (type is null)
            return Unsupported($"Condition attribute '{term.Attribute}' is unsupported.", out reason);

        condition = new RuleCondition
        {
            Type = type.Value,
            Values = [term.Value]
        };
        return true;
    }

    private static bool TryMapAction(
        RawAction raw,
        ThunderbirdRuleSource source,
        out RuleAction? action,
        out string? folder,
        out string? reason)
    {
        action = null;
        folder = null;
        reason = null;
        switch (raw.Name)
        {
            case "Move to folder":
            case "Copy to folder":
                if (!TryMapFolder(raw.Value, source, out folder, out reason))
                    return false;
                action = new RuleAction
                {
                    Type = raw.Name == "Move to folder"
                        ? RuleActionType.FileInto
                        : RuleActionType.CopyInto,
                    Values = [folder!]
                };
                return true;
            case "Mark read" when raw.Value is null:
                action = new RuleAction
                {
                    Type = RuleActionType.SetFlags,
                    Values = ["\\Seen"]
                };
                return true;
            case "Stop execution" when raw.Value is null:
                action = new RuleAction { Type = RuleActionType.Stop };
                return true;
            case "Forward":
                return Unsupported("Thunderbird Forward is not equivalent to Sieve redirect.", out reason);
            case "Delete":
                return Unsupported("Delete is deferred until account trash semantics are proven.", out reason);
            default:
                return Unsupported($"Action '{raw.Name}' is unsupported.", out reason);
        }
    }

    private static bool TryMapFolder(
        string? value,
        ThunderbirdRuleSource source,
        out string? folder,
        out string? reason)
    {
        folder = null;
        reason = null;
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            !uri.Scheme.Equals("imap", StringComparison.OrdinalIgnoreCase))
        {
            return Unsupported("Folder action does not target an absolute IMAP URI.", out reason);
        }

        string uriUser = Uri.UnescapeDataString(uri.UserInfo);
        if (!uri.Host.TrimEnd('.').Equals(source.Hostname.Trim().TrimEnd('.'), StringComparison.OrdinalIgnoreCase) ||
            !uriUser.Equals(source.Username.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Unsupported("Folder action targets a different or ambiguous IMAP account.", out reason);
        }

        string decoded = Uri.UnescapeDataString(uri.AbsolutePath).Trim('/');
        if (decoded.Length == 0)
            return Unsupported("Folder action has no mailbox path.", out reason);
        folder = decoded;
        return true;
    }

    private static string DecodeTermValue(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1];
        return value.Replace("\\\"", "\"", StringComparison.Ordinal);
    }

    private static void SkipSpaces(string text, ref int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;
    }

    private static bool Unsupported(string message, out string? reason)
    {
        reason = message;
        return false;
    }

    private static InvalidDataException ParseError(int line, string message) =>
        new($"msgFilterRules.dat line {line}: {message}");

    private sealed record ParsedFilterList(IReadOnlyList<RawRule> Rules);

    private sealed class RawRule(string name)
    {
        public string Name { get; } = name;
        public bool Enabled { get; set; }
        public bool HasEnabled { get; set; }
        public int? Type { get; set; }
        public string? Condition { get; set; }
        public List<RawAction> Actions { get; } = [];
        public List<string> UnsupportedFields { get; } = [];
    }

    private sealed class RawAction(string name)
    {
        public string Name { get; } = name;
        public string? Value { get; set; }
    }

    private sealed record ParsedTerm(string Attribute, string Operator, string Value);
}
