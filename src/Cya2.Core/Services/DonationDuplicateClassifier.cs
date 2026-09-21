namespace Cya2.Core.Services;

public sealed record DonationDuplicateCandidate(
    string? GiftImportId,
    string Fund,
    DateTime Date,
    decimal Amount,
    string PaymentMethod,
    string GiftType,
    string DonorIdentityKey);

public sealed record DonationDuplicateKey(
    string GiftImportId,
    string Fund,
    DateTime Date,
    decimal Amount,
    string PaymentMethod,
    string GiftType,
    string DonorIdentityKey);

public static class DonationDuplicateClassifier
{
    public static bool TryCreateKey(DonationDuplicateCandidate candidate, out DonationDuplicateKey key)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var giftImportId = Normalize(candidate.GiftImportId);
        if (string.IsNullOrWhiteSpace(giftImportId))
        {
            key = null!;
            return false;
        }

        key = new DonationDuplicateKey(
            giftImportId,
            Normalize(candidate.Fund),
            candidate.Date.Date,
            decimal.Round(candidate.Amount, 2, MidpointRounding.AwayFromZero),
            Normalize(candidate.PaymentMethod),
            Normalize(candidate.GiftType),
            Normalize(candidate.DonorIdentityKey));
        return true;
    }

    public static bool IsSameAllocation(DonationDuplicateCandidate left, DonationDuplicateCandidate right)
    {
        return TryCreateKey(left, out var leftKey) &&
               TryCreateKey(right, out var rightKey) &&
               leftKey == rightKey;
    }

    private static string Normalize(string? value)
    {
        return DonorIdentityResolver.NormalizeWhitespace(value).ToUpperInvariant();
    }
}
