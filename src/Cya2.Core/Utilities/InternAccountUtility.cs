namespace Cya2.Core.Utilities;

public static class InternAccountUtility
{
    // Intern identity is encoded in the fund value because it is used for account lifecycle and access, while donation lookup uses InternName.
    public const string InternFundPrefix = "Intern: ";

    public static bool IsInternFund(string? fund)
    {
        return !string.IsNullOrWhiteSpace(fund) &&
               fund.TrimStart().StartsWith(InternFundPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static InternDesignationCriteria CreateDesignationCriteria(string? internDesignationName)
    {
        var normalizedDesignation = NormalizeWhitespace(internDesignationName);
        var alternateDesignation = GetAlternateDesignationName(normalizedDesignation);
        var hasNameTokens = TryGetFirstAndLastName(normalizedDesignation, out var firstName, out var lastName);

        return new InternDesignationCriteria(
            normalizedDesignation,
            alternateDesignation,
            hasNameTokens,
            firstName,
            lastName,
            BuildLookupKey(normalizedDesignation),
            BuildLookupKey(alternateDesignation));
    }

    public static bool TryGetInternDesignationName(string? fund, out string internDesignationName)
    {
        internDesignationName = string.Empty;
        if (!IsInternFund(fund))
        {
            return false;
        }

        var raw = fund!.Trim();
        var suffix = raw.Length > InternFundPrefix.Length
            ? raw[InternFundPrefix.Length..]
            : string.Empty;

        internDesignationName = suffix.Trim();
        return !string.IsNullOrWhiteSpace(internDesignationName);
    }

    public static string GetInternNameFromUserName(string? userName)
    {
        var normalized = NormalizeWhitespace(userName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var parts = normalized.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2
            ? normalized
            : $"{parts[^1]}, {string.Join(' ', parts[..^1])}";
    }

public sealed record InternDesignationCriteria(
    string InternDesignationName,
    string AlternateDesignation,
    bool HasNameTokens,
    string FirstName,
    string LastName,
    string DesignationLookupKey,
    string AlternateLookupKey)
{
    public bool HasAlternateDesignation => !string.IsNullOrWhiteSpace(AlternateDesignation);
    public bool HasAlternateLookupKey => !string.IsNullOrWhiteSpace(AlternateLookupKey);
}

    public static string BuildInternFund(string internDesignationName)
    {
        var normalized = (internDesignationName ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? InternFundPrefix.TrimEnd()
            : $"{InternFundPrefix}{normalized}";
    }

    public static string GetAlternateDesignationName(string? internDesignationName)
    {
        var normalized = NormalizeWhitespace(internDesignationName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (normalized.Contains(','))
        {
            var parts = normalized
                .Split(',', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2)
            {
                return string.Empty;
            }

            return NormalizeWhitespace($"{parts[1]} {parts[0]}");
        }

        var nameParts = normalized
            .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (nameParts.Length < 2)
        {
            return string.Empty;
        }

        var firstAndMiddle = string.Join(' ', nameParts.Take(nameParts.Length - 1));
        var last = nameParts[^1];
        return NormalizeWhitespace($"{last}, {firstAndMiddle}");
    }

    public static bool TryGetFirstAndLastName(string? internDesignationName, out string firstName, out string lastName)
    {
        firstName = string.Empty;
        lastName = string.Empty;

        var normalized = NormalizeWhitespace(internDesignationName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (normalized.Contains(','))
        {
            var parts = normalized
                .Split(',', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2)
            {
                return false;
            }

            lastName = NormalizeWhitespace(parts[0]);
            var firstSegment = NormalizeWhitespace(parts[1]);
            firstName = firstSegment.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(firstName) && !string.IsNullOrWhiteSpace(lastName);
        }

        var nameParts = normalized
            .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (nameParts.Length < 2)
        {
            return false;
        }

        firstName = nameParts[0];
        lastName = nameParts[^1];
        return !string.IsNullOrWhiteSpace(firstName) && !string.IsNullOrWhiteSpace(lastName);
    }

    public static string BuildLookupKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();

        return new string(chars);
    }

    public static string GetDisplayFundName(string? fund)
    {
        if (string.IsNullOrWhiteSpace(fund))
        {
            return string.Empty;
        }

        var separatorIndex = fund.IndexOf(':');
        return (separatorIndex >= 0 ? fund[..separatorIndex] : fund).Trim();
    }

    public static string GetAccountSelectorDisplayName(string? fund)
    {
        if (TryGetInternDesignationName(fund, out var internName))
        {
            // Keep the stored identity unchanged, but display intern accounts with the same last-name-first convention used for named accounts.
            return $"{GetInternAccountDisplayName(internName)} (Intern)";
        }

        return GetDisplayFundName(fund);
    }

    public static string GetAccountSelectorDisplayName(string? fund, string? internName)
    {
        return string.IsNullOrWhiteSpace(internName)
            ? GetAccountSelectorDisplayName(fund)
            : GetAccountSelectorDisplayName(BuildInternFund(internName));
    }

    public static string GetAccountSelectorSortKey(string? fund)
    {
        // Sort on the display value so intern accounts are grouped by surname instead of the internal "Intern:" prefix.
        return GetAccountSelectorDisplayName(fund);
    }

    private static string GetInternAccountDisplayName(string internName)
    {
        var normalizedName = NormalizeWhitespace(internName);
        if (normalizedName.Contains(','))
        {
            return normalizedName;
        }

        var reversedName = GetAlternateDesignationName(normalizedName);
        return string.IsNullOrWhiteSpace(reversedName)
            ? normalizedName
            : reversedName;
    }

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(' ', value
            .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }
}
