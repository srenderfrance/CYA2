namespace Cya2.Core.Services;

public sealed record DonorIdentityInput(
    string Fund,
    string? AccountName,
    string? SoftCreditName,
    string? PrimaryAddressee,
    bool IsAnonymous = false);

public sealed record DonorIdentityResult(
    string Fund,
    string DisplayName,
    string IdentityKey,
    string ResolutionSource,
    int ResolutionVersion)
{
    public const int CurrentResolutionVersion = 1;
}

public sealed class DonorIdentityResolver
{
    public DonorIdentityResult Resolve(DonorIdentityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var fund = NormalizeWhitespace(input.Fund);
        var accountName = NormalizeDisplayName(input.AccountName);
        var softCreditName = NormalizeDisplayName(input.SoftCreditName);
        var primaryAddressee = NormalizeDisplayName(input.PrimaryAddressee);

        if (input.IsAnonymous)
        {
            return CreateResult(fund, "Anonymous", "Anonymous", "Anonymous");
        }

        if (!string.IsNullOrWhiteSpace(primaryAddressee))
        {
            if (!string.IsNullOrWhiteSpace(softCreditName) &&
                !ContainsName(primaryAddressee, softCreditName))
            {
                return CreateResult(fund, softCreditName, softCreditName, "SoftCredit");
            }

            return CreateResult(fund, primaryAddressee, primaryAddressee, "PrimaryAddressee");
        }

        if (!string.IsNullOrWhiteSpace(softCreditName))
        {
            return CreateResult(fund, softCreditName, softCreditName, "SoftCredit");
        }

        var displayName = string.IsNullOrWhiteSpace(accountName) ? "Unknown" : accountName;
        return CreateResult(fund, displayName, displayName, "AccountName");
    }

    public static string NormalizeDisplayName(string? value)
    {
        var normalized = NormalizeWhitespace(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (!normalized.Contains(','))
        {
            return normalized;
        }

        var parts = normalized
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return normalized;
        }

        var reordered = $"{parts[1]} {parts[0]}";
        if (parts.Length > 2)
        {
            reordered += $" {string.Join(' ', parts.Skip(2))}";
        }

        return NormalizeWhitespace(reordered);
    }

    public static string NormalizeIdentityKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = NormalizeDisplayName(value).ToLowerInvariant();
        var chars = normalized.Where(char.IsLetterOrDigit).ToArray();
        return new string(chars);
    }

    public static string NormalizeWhitespace(string? value)
    {
        return string.Join(' ', (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static bool ContainsName(string containingName, string possibleName)
    {
        var containingTokens = Tokenize(containingName);
        var possibleTokens = Tokenize(possibleName);
        if (possibleTokens.Count == 0 || possibleTokens.Count > containingTokens.Count)
        {
            return false;
        }

        for (var i = 0; i <= containingTokens.Count - possibleTokens.Count; i++)
        {
            if (possibleTokens.SequenceEqual(containingTokens.Skip(i).Take(possibleTokens.Count), StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> Tokenize(string value)
    {
        return value
            .Split(new[] { ' ', ',', '&', '/', '\\', '-', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => new string(token.Where(char.IsLetterOrDigit).ToArray()))
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();
    }

    private static DonorIdentityResult CreateResult(string fund, string displayName, string identityValue, string source)
    {
        var identityKey = NormalizeIdentityKey(identityValue);
        return new DonorIdentityResult(
            fund,
            displayName,
            identityKey,
            source,
            DonorIdentityResult.CurrentResolutionVersion);
    }
}
