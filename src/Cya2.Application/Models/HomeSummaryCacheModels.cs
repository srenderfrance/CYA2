using Cya2.Application.DTOs;

namespace Cya2.Application.Models;

public readonly record struct HomeSummaryCacheKey(
    int AccountId,
    string Fund,
    string InternName,
    long Generation,
    int Year,
    int Month)
{
    public HomeSummaryCacheKey(int accountId, string fund, long generation, int year, int month)
        : this(accountId, fund, string.Empty, generation, year, month)
    {
    }

    public HomeSummaryCacheKey Normalize() => this with
    {
        Fund = Fund?.Trim() ?? string.Empty,
        InternName = InternName?.Trim() ?? string.Empty
    };
}

public sealed class HomeSummaryCacheEntry
{
    public FinancialSummaryDto CurrentMonth { get; init; } = new();
    public FinancialSummaryDto PriorMonth { get; init; } = new();
    public FinancialSummaryDto CurrentYear { get; init; } = new();
    public FinancialSummaryDto PriorYear { get; init; } = new();
}
