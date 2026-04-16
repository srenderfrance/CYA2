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
        var frequencyService = new DonorFrequencyService();

        // Build set of months that are covered (either by a gift or catch-up).
        var coveredMonths = BuildCoveredMonths(sorted, frequencyService);

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

    private static HashSet<DateTime> BuildCoveredMonths(
        List<DonorGiftRecord> sorted,
        DonorFrequencyService frequencyService)
    {
        var covered = new HashSet<DateTime>();

        foreach (var gift in sorted)
        {
            // Each gift covers its own month.
            var giftMonth = new DateTime(gift.Date.Year, gift.Date.Month, 1);
            covered.Add(giftMonth);

            // If it is a catch-up, absorb additional prior months.
            var (isCatchUp, monthsCovered) = frequencyService.DetectCatchUp(gift, sorted);
            if (isCatchUp && monthsCovered > 1)
            {
                for (var m = 1; m < monthsCovered; m++)
                {
                    covered.Add(giftMonth.AddMonths(-m));
                }
            }
        }

        return covered;
    }
}
