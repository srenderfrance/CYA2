namespace Cya2.Core.Services;

/// <summary>
/// Represents a missing gift alert for a monthly donor.
/// </summary>
public sealed class MissingGiftAlert
{
    public string DonorName { get; init; } = string.Empty;
    /// <summary>The first day of the month where a gift was expected but not received.</summary>
    public DateTime ExpectedMonth { get; init; }
    /// <summary>Human-readable label e.g. "March 2026".</summary>
    public string ExpectedMonthLabel => ExpectedMonth.ToString("MMMM yyyy");
}

/// <summary>
/// Pure domain service for detecting missing monthly gifts.
/// No infrastructure or framework dependencies.
/// </summary>
public class DonorMissingGiftService
{
    private const int AlertWindowMonths = 3;

    /// <summary>
    /// Returns missing gift alerts for a single monthly donor.
    /// Looks back AlertWindowMonths from the reference date.
    /// Catch-up donations are taken into account: if a large gift covers a missed month
    /// that month is not flagged.
    /// </summary>
    /// <param name="donorName">Name used to populate alert records.</param>
    /// <param name="history">Full donation history for this donor (used for catch-up detection).</param>
    /// <param name="referenceDate">The date to calculate the alert window from (usually today).</param>
    public List<MissingGiftAlert> GetMissingGiftAlerts(
        string donorName,
        IReadOnlyList<DonorGiftRecord> history,
        DateTime referenceDate)
    {
        var alerts = new List<MissingGiftAlert>();

        if (string.IsNullOrWhiteSpace(donorName) || history == null || history.Count < 2)
            return alerts;

        var sorted = history.OrderBy(g => g.Date).ToList();
        // Build set of months that are covered (either by a gift, catch-up, or pre-payment).
        // Uses month-level totals so split gifts in the same month are evaluated together.
        var coveredMonths = BuildCoveredMonths(sorted);

        // Check each month in the alert window.
        for (var i = 1; i <= AlertWindowMonths; i++)
        {
            // We check completed months only (not the current partial month).
            var checkMonth = new DateTime(referenceDate.Year, referenceDate.Month, 1)
                .AddMonths(-i);

            if (!coveredMonths.Contains(checkMonth))
            {
                alerts.Add(new MissingGiftAlert
                {
                    DonorName = donorName,
                    ExpectedMonth = checkMonth
                });
            }
        }

        return alerts;
    }

    /// <summary>
    /// Runs missing gift detection across all monthly donors in a collection.
    /// </summary>
    public List<MissingGiftAlert> GetMissingGiftAlertsForAll(
        IEnumerable<(string DonorName, IReadOnlyList<DonorGiftRecord> History)> monthlyDonors,
        DateTime referenceDate)
    {
        var all = new List<MissingGiftAlert>();
        foreach (var (name, history) in monthlyDonors)
        {
            all.AddRange(GetMissingGiftAlerts(name, history, referenceDate));
        }
        return all;
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static HashSet<DateTime> BuildCoveredMonths(List<DonorGiftRecord> sorted)
    {
        var covered = new HashSet<DateTime>();

        var monthlyTotals = sorted
            .GroupBy(g => new DateTime(g.Date.Year, g.Date.Month, 1))
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var giftMonths = monthlyTotals.Keys
            .OrderBy(m => m)
            .ToList();

        // Every month with at least one gift is covered.
        foreach (var month in giftMonths)
            covered.Add(month);

        // Evaluate each month-to-month gap and absorb missed months from either side:
        // - catch-up at the later month
        // - pre-payment at the earlier month
        for (var i = 0; i < giftMonths.Count - 1; i++)
        {
            var monthA = giftMonths[i];
            var monthB = giftMonths[i + 1];
            var gapSize = MonthsBetween(monthA, monthB) - 1;
            if (gapSize <= 0) continue;

            var amountAtA = monthlyTotals[monthA];
            var amountAtB = monthlyTotals[monthB];

            var priorToB = giftMonths
                .Where(m => m < monthB)
                .Select(m => monthlyTotals[m])
                .ToList();

            var afterA = giftMonths
                .Where(m => m > monthA)
                .Select(m => monthlyTotals[m])
                .ToList();

            var catchUpAbsorbed = 0;
            if (priorToB.Count > 0)
            {
                var avgPriorToB = priorToB.Average();
                if (avgPriorToB > 0)
                {
                    var monthsCoveredByCatchUp = (int)Math.Round((double)(amountAtB / avgPriorToB));
                    catchUpAbsorbed = Math.Min(gapSize, Math.Max(0, monthsCoveredByCatchUp - 1));
                }
            }

            var prePayAbsorbed = 0;
            if (afterA.Count > 0)
            {
                var avgAfterA = afterA.Average();
                if (avgAfterA > 0)
                {
                    var monthsCoveredByPrePay = (int)Math.Round((double)(amountAtA / avgAfterA));
                    prePayAbsorbed = Math.Min(gapSize, Math.Max(0, monthsCoveredByPrePay - 1));
                }
            }

            // Prefer whichever side explains the gap better and mark those months covered.
            if (prePayAbsorbed >= catchUpAbsorbed && prePayAbsorbed > 0)
            {
                for (var m = 1; m <= prePayAbsorbed; m++)
                    covered.Add(monthA.AddMonths(m));
            }
            else if (catchUpAbsorbed > 0)
            {
                for (var m = 1; m <= catchUpAbsorbed; m++)
                    covered.Add(monthB.AddMonths(-m));
            }
        }

        return covered;
    }

    private static int MonthsBetween(DateTime from, DateTime to)
    {
        return Math.Abs(((to.Year - from.Year) * 12) + to.Month - from.Month);
    }
}
