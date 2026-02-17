namespace Cya2.Shared.Utilities;

/// <summary>
/// Financial calculation utilities that work both client-side and server-side
/// </summary>
public static class FinancialCalculations
{
    /// <summary>
    /// Calculate overhead percentage amount
    /// </summary>
    public static decimal CalculateOverhead(decimal amount, decimal overheadPercentage)
    {
        return Math.Round(amount * (overheadPercentage / 100m), 2);
    }
    
    /// <summary>
    /// Calculate percentage of total
    /// </summary>
    public static decimal CalculatePercentage(decimal part, decimal total)
    {
        if (total == 0) return 0;
        return Math.Round((part / total) * 100, 2);
    }
    
    /// <summary>
    /// Format currency for display
    /// </summary>
    public static string FormatCurrency(decimal amount, bool showCents = true)
    {
        return showCents ? amount.ToString("C2") : amount.ToString("C0");
    }
    
    /// <summary>
    /// Calculate year-over-year growth percentage
    /// </summary>
    public static decimal CalculateGrowthPercentage(decimal currentAmount, decimal previousAmount)
    {
        if (previousAmount == 0) return 0;
        return Math.Round(((currentAmount - previousAmount) / previousAmount) * 100, 1);
    }
    
    /// <summary>
    /// Calculate average over time period
    /// </summary>
    public static decimal CalculateAverage(List<decimal> amounts)
    {
        if (!amounts.Any()) return 0;
        return Math.Round(amounts.Average(), 2);
    }
}