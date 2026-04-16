namespace Cya2.Application.Interfaces;

public interface IExpensePresentationService
{
    string FormatDateRange(DateTime startDate, DateTime endDate, string defaultLabel);
}
