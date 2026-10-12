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
        out decimal totalBalance);

    void Set(
        int accountId,
        string accountingClass,
        string accountNumber,
        decimal balanceAdjustment,
        long generation,
        DateTime asOfDate,
        decimal totalBalance);

    void InvalidateAll();
    int Count { get; }
}
