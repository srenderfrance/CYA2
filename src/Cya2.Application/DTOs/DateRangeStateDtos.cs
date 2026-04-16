namespace Cya2.Application.DTOs;

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

public class DateRangeStateDto
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime PendingStartDate { get; set; }
    public DateTime PendingEndDate { get; set; }
    public DateRangePreset SelectedPreset { get; set; }
    public bool IsCustomRange { get; set; }
}

public class DateRangePresetOptionDto
{
    public string Text { get; set; } = string.Empty;
    public DateRangePreset Value { get; set; }
}
