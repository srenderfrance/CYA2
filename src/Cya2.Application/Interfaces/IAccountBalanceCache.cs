namespace Cya2.Application.Interfaces;

public interface IAccountBalanceCache
{
    bool TryGet(
        int accountId,
        string accountingClass,
        string accountNumber,
        decimal balanceAdjustment,
        long generation,
        DateTime asOfDate,
        string userId,
        out decimal totalBalance);

    void Set(
        int accountId,
        string accountingClass,
        string accountNumber,
        decimal balanceAdjustment,
        long generation,
        DateTime asOfDate,
        string userId,
        decimal totalBalance);

    void RemoveStaleGeneration(long currentGeneration);

    void InvalidateAll();
    int Count { get; }
}
