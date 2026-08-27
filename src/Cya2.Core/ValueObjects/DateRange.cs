namespace Cya2.Core.ValueObjects;

public record DateRange(DateTime StartDate, DateTime EndDate)
{
    public DateRange() : this(DateTime.Today.AddYears(-1), DateTime.Today) { }

    public bool Contains(DateTime date) => date >= StartDate && date <= EndDate;

    public int DurationInDays => (EndDate - StartDate).Days + 1;

    public bool IsValid() => EndDate >= StartDate;

    public override string ToString() => $"{StartDate:MM/dd/yyyy} - {EndDate:MM/dd/yyyy}";
}