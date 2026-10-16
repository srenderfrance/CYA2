using Cya2.Core.ValueObjects;

namespace Cya2.Application.Models;

public static class AccountFactCacheWindow
{
    public static DateRange GetStandardWindow(DateTime now)
        => new(new DateTime(now.Year - 1, 1, 1), now.Date);

    public static IEnumerable<DateRange> GetMissingRanges(DateTime requestedStart, DateTime requestedEnd, DateRange coveredRange)
    {
        var start = requestedStart.Date;
        var end = requestedEnd.Date;
        var coveredStart = coveredRange.StartDate.Date;
        var coveredEnd = coveredRange.EndDate.Date;

        if (start < coveredStart)
            yield return new DateRange(start, coveredStart.AddDays(-1));
        if (end > coveredEnd)
            yield return new DateRange(coveredEnd.AddDays(1), end);
    }
}
