using Microsoft.Extensions.Localization;

namespace Cya2.Shared.Utilities;

/// <summary>
/// Static utility class for date range operations.
/// Contains pure date manipulation functions.
/// </summary>
public static class DateRangeHelper
{
    public enum DateRangePreset 
    { 
        AllDates,
        ThisYear, 
        Previous12Months, 
        LastYear, 
        ThisMonth,
        LastMonth,
        Custom
    }

    /// <summary>
    /// Get date range for a specific preset
    /// </summary>
    public static (DateTime start, DateTime end) GetPresetDateRange(DateRangePreset preset)
    {
        var now = DateTime.Now;
        
        return preset switch
        {
            DateRangePreset.AllDates => (new DateTime(1900, 1, 1), now),
            DateRangePreset.ThisYear => (new DateTime(now.Year, 1, 1), now),
            DateRangePreset.LastYear => (new DateTime(now.Year - 1, 1, 1), new DateTime(now.Year - 1, 12, 31)),
            DateRangePreset.ThisMonth => (new DateTime(now.Year, now.Month, 1), now),
            DateRangePreset.LastMonth => GetLastMonthRange(),
            DateRangePreset.Previous12Months => GetPrevious12MonthsRange(),
            _ => (now.AddDays(-30), now) // Default fallback
        };
    }

    /// <summary>
    /// Get the previous 12 complete months range
    /// </summary>
    public static (DateTime start, DateTime end) GetPrevious12MonthsRange()
    {
        var now = DateTime.Now;
        var lastCompleteMonth = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
        var end = new DateTime(lastCompleteMonth.Year, lastCompleteMonth.Month, 
                              DateTime.DaysInMonth(lastCompleteMonth.Year, lastCompleteMonth.Month));
        var start = end.AddMonths(-11);
        start = new DateTime(start.Year, start.Month, 1);
        return (start, end);
    }

    /// <summary>
    /// Get last month's date range
    /// </summary>
    public static (DateTime start, DateTime end) GetLastMonthRange()
    {
        var now = DateTime.Now;
        var firstDayOfLastMonth = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
        var lastDayOfLastMonth = new DateTime(firstDayOfLastMonth.Year, firstDayOfLastMonth.Month, 
                                             DateTime.DaysInMonth(firstDayOfLastMonth.Year, firstDayOfLastMonth.Month));
        return (firstDayOfLastMonth, lastDayOfLastMonth);
    }

    /// <summary>
    /// Determine which preset best matches the given date range
    /// </summary>
    public static DateRangePreset DetectPreset(DateTime start, DateTime end)
    {
        var presets = new[] 
        {
            DateRangePreset.AllDates,
            DateRangePreset.LastYear, // Check Last Year before Previous12Months for January clarity
            DateRangePreset.ThisYear,
            DateRangePreset.ThisMonth,
            DateRangePreset.LastMonth,
            DateRangePreset.Previous12Months
        };

        foreach (var preset in presets)
        {
            var (presetStart, presetEnd) = GetPresetDateRange(preset);
            if (start.Date == presetStart.Date && end.Date == presetEnd.Date)
                return preset;
        }

        return DateRangePreset.Custom;
    }

    /// <summary>
    /// Check if a date falls within a range (inclusive)
    /// </summary>
    public static bool IsDateInRange(DateTime date, DateTime start, DateTime end)
    {
        return date.Date >= start.Date && date.Date <= end.Date;
    }

    /// <summary>
    /// Get the number of days in a date range
    /// </summary>
    public static int GetDaysInRange(DateTime start, DateTime end)
    {
        return (end.Date - start.Date).Days + 1;
    }

    /// <summary>
    /// Get the number of complete months in a date range
    /// </summary>
    public static int GetMonthsInRange(DateTime start, DateTime end)
    {
        return ((end.Year - start.Year) * 12) + end.Month - start.Month + 1;
    }

    /// <summary>
    /// Format date range as string
    /// </summary>
    public static string FormatDateRange(DateTime start, DateTime end)
    {
        return $"{start:MM/dd/yyyy} - {end:MM/dd/yyyy}";
    }

    /// <summary>
    /// Check if two date ranges overlap
    /// </summary>
    public static bool DoRangesOverlap(DateTime start1, DateTime end1, DateTime start2, DateTime end2)
    {
        return start1 <= end2 && start2 <= end1;
    }

    /// <summary>
    /// Get common date range presets with localized names
    /// </summary>
    public static List<(DateRangePreset preset, string displayName)> GetPresetOptions(IStringLocalizer? localizer = null)
    {
        var options = new List<(DateRangePreset, string)>
        {
            (DateRangePreset.AllDates, localizer?["AllDates"] ?? "All Dates"),
            (DateRangePreset.ThisYear, localizer?["ThisYear"] ?? "This Year"),
            (DateRangePreset.Previous12Months, localizer?["Previous12Months"] ?? "Previous 12 Months"),
            (DateRangePreset.LastYear, localizer?["LastYear"] ?? "Last Year"),
            (DateRangePreset.ThisMonth, localizer?["ThisMonth"] ?? "This Month"),
            (DateRangePreset.LastMonth, localizer?["LastMonth"] ?? "Last Month"),
            (DateRangePreset.Custom, localizer?["CustomRange"] ?? "Custom Range")
        };

        return options;
    }

    /// <summary>
    /// Get quarter date ranges for a given year
    /// </summary>
    public static List<(string name, DateTime start, DateTime end)> GetQuartersForYear(int year)
    {
        return new List<(string, DateTime, DateTime)>
        {
            ($"Q1 {year}", new DateTime(year, 1, 1), new DateTime(year, 3, 31)),
            ($"Q2 {year}", new DateTime(year, 4, 1), new DateTime(year, 6, 30)),
            ($"Q3 {year}", new DateTime(year, 7, 1), new DateTime(year, 9, 30)),
            ($"Q4 {year}", new DateTime(year, 10, 1), new DateTime(year, 12, 31))
        };
    }
}