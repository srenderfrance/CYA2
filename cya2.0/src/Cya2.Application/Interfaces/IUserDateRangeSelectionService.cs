namespace Cya2.Application.Interfaces;

public sealed class UserDateRangeSelection
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Preset { get; set; } = "ThisMonth";
}

public interface IUserDateRangeSelectionService
{
    void SetDateRange(string userId, DateTime startDate, DateTime endDate, string preset, TimeSpan? ttl = null);
    bool TryGetDateRange(string userId, out UserDateRangeSelection selection);
    void RemoveDateRange(string userId);
}
