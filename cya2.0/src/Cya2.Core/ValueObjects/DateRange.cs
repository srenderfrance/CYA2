namespace Cya2.Core.ValueObjects;

public record DateRange(DateTime StartDate, DateTime EndDate)
{
    public DateRange() : this(DateTime.Today.AddYears(-1), DateTime.Today) { }

    public bool Contains(DateTime date) => date >= StartDate && date <= EndDate;

    public int DurationInDays => (EndDate - StartDate).Days + 1;

    public bool IsValid() => EndDate >= StartDate;

    public static DateRange CurrentYear() => 
        new(new DateTime(DateTime.Now.Year, 1, 1), new DateTime(DateTime.Now.Year, 12, 31));

    public static DateRange LastYear() => 
        new(new DateTime(DateTime.Now.Year - 1, 1, 1), new DateTime(DateTime.Now.Year - 1, 12, 31));

    public static DateRange CurrentMonth() => 
        new(new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1), 
            new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.DaysInMonth(DateTime.Now.Year, DateTime.Now.Month)));

    public static DateRange LastMonth()
    {
        var lastMonth = DateTime.Now.AddMonths(-1);
        return new(new DateTime(lastMonth.Year, lastMonth.Month, 1),
                  new DateTime(lastMonth.Year, lastMonth.Month, DateTime.DaysInMonth(lastMonth.Year, lastMonth.Month)));
    }

    public static DateRange Last30Days() => new(DateTime.Today.AddDays(-30), DateTime.Today);

    public override string ToString() => $"{StartDate:MM/dd/yyyy} - {EndDate:MM/dd/yyyy}";
}