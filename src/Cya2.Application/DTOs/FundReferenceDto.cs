namespace Cya2.Application.DTOs;

public sealed class FundReferenceDto
{
    public int AccountId { get; init; }
    public string Fund { get; init; } = string.Empty;
    public string InternName { get; init; } = string.Empty;
    public string DisplayName => string.IsNullOrWhiteSpace(InternName) ? Fund : InternName;
    public DateTime CreatedAt { get; init; }
    public string AccountingClass { get; init; } = string.Empty;
    public string AccountNumber { get; init; } = string.Empty;
    public decimal Overhead { get; init; }
    public string SoftCredit { get; init; } = string.Empty;
    public decimal BalanceAdjustment { get; init; }
    public bool OtherFunds { get; init; }
    public IReadOnlySet<int> UserIds { get; init; } = new HashSet<int>();

    public bool IsAccessibleBy(int userId, bool canAccessAllAccounts) =>
        canAccessAllAccounts || UserIds.Contains(userId);
}