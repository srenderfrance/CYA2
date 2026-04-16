using Cya2.Application.Interfaces;

namespace Cya2.Application.Services;

public class ExpensePresentationService : IExpensePresentationService
{
    public string FormatDateRange(DateTime startDate, DateTime endDate, string defaultLabel)
    {
        try
        {
            var startFormatted = startDate.ToString("MMM d, yyyy");
            var endFormatted = endDate.ToString("MMM d, yyyy");
            return startDate.Date == endDate.Date ? startFormatted : $"{startFormatted} - {endFormatted}";
        }
        catch
        {
            return defaultLabel;
        }
    }
}
