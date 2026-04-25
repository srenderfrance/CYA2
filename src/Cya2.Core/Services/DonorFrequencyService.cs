using Cya2.Core.Enums;

namespace Cya2.Core.Services;

/// <summary>
/// Represents a single donation used for frequency classification.
/// Kept minimal so it can be populated from cache or DB without coupling.
/// </summary>
public sealed class DonorGiftRecord
{
    public DateTime Date { get; init; }
    public decimal Amount { get; init; }
}

/// <summary>
/// Result of classifying a single past gift.
/// Designed so it can also be persisted to the DB model in a future pipeline.
/// </summary>
public sealed class GiftClassificationResult
{
    public DonorFrequency Frequency { get; init; }
    public bool IsCatchUp { get; init; }
    public int CatchUpMonthsCovered { get; init; }
}

/// <summary>
/// Pure domain service for classifying donor giving frequency.
/// No infrastructure or framework dependencies.
/// </summary>
public class DonorFrequencyService
{
    // A donor is Monthly if missed months < 25% of the evaluation window.
    private const double MissedMonthTolerancePercent = 0.25;

    // Gifts are considered yearly if between 11 and 13 months apart.
    private const int YearlyMinMonths = 11;
    private const int YearlyMaxMonths = 13;

    // Evaluation window for Monthly classification.
    private const int MonthlyLookbackMonths = 12;

    /// <summary>
    /// Determines the current frequency classification for a donor.
    /// Uses cache-first: only the history provided is evaluated.
    /// Caller is responsible for supplying sufficient history:
    ///   - Last 12 months minimum for Monthly detection.
    ///   - 2+ years for Yearly detection if not Monthly.
    /// Classification can move backward (re-evaluated fresh each call).
    /// </summary>
    public DonorFrequency ClassifyDonor(IReadOnlyList<DonorGiftRecord> history)
    {
        if (history == null || history.Count == 0)
            return DonorFrequency.None;

        var sorted = history.OrderBy(g => g.Date).ToList();

        if (sorted.Count == 1)
            return DonorFrequency.OneTime;

        if (IsMonthly(sorted))
            return DonorFrequency.Monthly;

        if (IsYearly(sorted))
            return DonorFrequency.Yearly;

        return DonorFrequency.Sporadic;
    }

    /// <summary>
    /// Classifies a single past gift in context of the donor's surrounding history.
    /// Designed to also be usable from a future DB-write pipeline (import/update).
    /// historyAroundGift should include donations before AND after the gift date.
    /// </summary>
    public GiftClassificationResult ClassifyGift(DonorGiftRecord gift, IReadOnlyList<DonorGiftRecord> historyAroundGift)
    {
        if (gift == null)
            return new GiftClassificationResult { Frequency = DonorFrequency.None };

        var all = (historyAroundGift ?? Array.Empty<DonorGiftRecord>())
            .Where(g => g.Date != gift.Date)
            .Concat(new[] { gift })
            .OrderBy(g => g.Date)
            .ToList();

        var donorStatus = ClassifyDonor(all);

        // Check catch-up only for monthly donors
        if (donorStatus == DonorFrequency.Monthly)
        {
            var catchUp = DetectCatchUp(gift, historyAroundGift);
            if (catchUp.IsCatchUp)
            {
                return new GiftClassificationResult
                {
                    Frequency = DonorFrequency.Monthly,
                    IsCatchUp = true,
                    CatchUpMonthsCovered = catchUp.CatchUpMonthsCovered
                };
            }
        }

        return new GiftClassificationResult { Frequency = donorStatus };
    }

    /// <summary>
    /// Detects whether a gift is a catch-up payment covering missed months.
    /// Uses Option B: divide gift amount by average monthly amount and round to nearest integer.
    /// </summary>
    public (bool IsCatchUp, int CatchUpMonthsCovered) DetectCatchUp(
        DonorGiftRecord gift,
        IReadOnlyList<DonorGiftRecord> history)
    {
        if (gift == null || history == null || history.Count < 2)
            return (false, 1);

        var priorGifts = history
            .Where(g => g.Date < gift.Date)
            .OrderBy(g => g.Date)
            .ToList();

        if (priorGifts.Count == 0)
            return (false, 1);

        var averageMonthlyAmount = priorGifts.Average(g => g.Amount);
        if (averageMonthlyAmount <= 0)
            return (false, 1);

        var monthsCovered = (int)Math.Round((double)(gift.Amount / averageMonthlyAmount));
        monthsCovered = Math.Max(1, monthsCovered);

        return (monthsCovered > 1, monthsCovered);
    }

    public (bool IsPrePayment, int MonthsCovered) DetectPrePayment(DonorGiftRecord gift, IReadOnlyList<DonorGiftRecord> history) { if (gift == null || history == null || history.Count < 2) return (false, 1); var sub = history.Where(g => g.Date > gift.Date).OrderBy(g => g.Date).ToList(); var refs = sub.Count > 0 ? sub : history.Where(g => g.Date < gift.Date).OrderBy(g => g.Date).ToList(); if (!refs.Any()) return (false, 1); var avg = refs.Average(g => g.Amount); if (avg <= 0) return (false, 1); var mc = (int)Math.Round((double)(gift.Amount / avg)); mc = Math.Max(1, mc); return (mc > 1, mc); }

    // ── Private classification helpers ────────────────────────────────────────

    private bool IsMonthly(List<DonorGiftRecord> sorted)
    {
        var now = sorted.Last().Date;
        var windowStart = now.AddMonths(-MonthlyLookbackMonths);

        var giftsInWindow = sorted
            .Where(g => g.Date >= windowStart)
            .ToList();

        if (giftsInWindow.Count == 0)
            return false;

        // Build the set of year-month keys that have at least one gift.
        var giftMonths = giftsInWindow
            .Select(g => new DateTime(g.Date.Year, g.Date.Month, 1))
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        if (giftMonths.Count < 2)
            return false;

        // Evaluate months in the window from the first gift month to the last.
        var firstGiftMonth = giftMonths.First();
        var lastGiftMonth = giftMonths.Last();

        var totalMonthsSpan = MonthsBetween(firstGiftMonth, lastGiftMonth) + 1;
        if (totalMonthsSpan < 2)
            return false;

        // For each gap between consecutive gift months, check whether it is absorbed
        // by a catch-up payment (large gift AFTER the gap) or a pre-payment (large gift
        // BEFORE the gap). Credit whichever is larger; never exceed the actual gap size.
        var totalAbsorbed = 0;
        for (var i = 0; i < giftMonths.Count - 1; i++)
        {
            var monthA = giftMonths[i];
            var monthB = giftMonths[i + 1];
            var gapSize = MonthsBetween(monthA, monthB) - 1;
            if (gapSize <= 0) continue;

            var amountAtB = giftsInWindow.Where(g => g.Date.Year == monthB.Year && g.Date.Month == monthB.Month).Sum(g => g.Amount);
            var priorToB = giftsInWindow.Where(g => !(g.Date.Year == monthB.Year && g.Date.Month == monthB.Month) && g.Date < monthB).ToList();
            var catchUpAbsorbed = 0;
            if (priorToB.Count > 0) { var avgPB = priorToB.Average(g => g.Amount); if (avgPB > 0) { var cu = (int)Math.Round((double)(amountAtB / avgPB)); catchUpAbsorbed = Math.Min(gapSize, Math.Max(0, cu - 1)); } }

            var amountAtA = giftsInWindow.Where(g => g.Date.Year == monthA.Year && g.Date.Month == monthA.Month).Sum(g => g.Amount);
            var afterA = giftsInWindow.Where(g => !(g.Date.Year == monthA.Year && g.Date.Month == monthA.Month) && g.Date > monthA).ToList();
            var prePayAbsorbed = 0;
            if (afterA.Count > 0) { var avgAA = afterA.Average(g => g.Amount); if (avgAA > 0) { var pp = (int)Math.Round((double)(amountAtA / avgAA)); prePayAbsorbed = Math.Min(gapSize, Math.Max(0, pp - 1)); } }

            totalAbsorbed += Math.Min(gapSize, Math.Max(catchUpAbsorbed, prePayAbsorbed));
        }

        var effectiveMonthCount = giftMonths.Count + totalAbsorbed;
        var missedMonths = Math.Max(0, totalMonthsSpan - effectiveMonthCount);
        var missedRatio = (double)missedMonths / totalMonthsSpan;

        return missedRatio <= MissedMonthTolerancePercent;
    }

    private static bool IsYearly(List<DonorGiftRecord> sorted)
    {
        if (sorted.Count < 2)
            return false;

        // All consecutive gap pairs must fall within the yearly window.
        for (var i = 1; i < sorted.Count; i++)
        {
            var months = MonthsBetween(sorted[i - 1].Date, sorted[i].Date);
            if (months < YearlyMinMonths || months > YearlyMaxMonths)
                return false;
        }

        return true;
    }

    private static int MonthsBetween(DateTime from, DateTime to)
    {
        return Math.Abs(((to.Year - from.Year) * 12) + to.Month - from.Month);
    }
}
