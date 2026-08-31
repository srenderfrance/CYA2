namespace Cya2.Core.Entities;

/// <summary>
/// Database model for SubAccounts table.
/// </summary>
public class SubAccount : BaseEntity
{
    public int AccountId { get; set; }

    // UI refers to this as "Fund" for sub-funds; DB column name is SubFund
    public string SubFund { get; set; } = string.Empty;

    // UI refers to this as "Type"; stored as string in DB (e.g., "Merged", "Separate")
    public string Kind { get; set; } = string.Empty;

    // Parameterless constructor
    public SubAccount() { }

    // Constructor for creating new sub-accounts
    public SubAccount(int accountId, string subFund, string kind)
    {
        AccountId = accountId;
        SubFund = subFund?.Trim() ?? string.Empty;
        Kind = kind?.Trim() ?? string.Empty;
    }

    // Business logic helper methods
    public bool IsSeparate()
    {
        return string.Equals(Kind, "Separate", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsMerged()
    {
        return string.Equals(Kind, "Merged", StringComparison.OrdinalIgnoreCase);
    }

    // Update methods with change tracking
    public void UpdateSubFund(string newSubFund)
    {
        SubFund = newSubFund?.Trim() ?? string.Empty;
        SetModified();
    }

    public void UpdateKind(string newKind)
    {
        Kind = newKind?.Trim() ?? string.Empty;
        SetModified();
    }

    // Validation helpers for admin forms
    public bool IsValid()
    {
        return AccountId > 0 &&
               !string.IsNullOrWhiteSpace(SubFund) &&
               !string.IsNullOrWhiteSpace(Kind) &&
               IsValidKind();
    }

    public bool IsValidKind()
    {
        return IsMerged() || IsSeparate();
    }

    // Helper for your SubAccountHelper class integration
    public bool MatchesKind(string kindToMatch)
    {
        return string.Equals(Kind, kindToMatch, StringComparison.OrdinalIgnoreCase);
    }
}