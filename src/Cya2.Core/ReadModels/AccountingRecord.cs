namespace Cya2.Core.ReadModels;

public class AccountingRecord
{
    public int Id { get; set; }
    public string AccountingClass { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Num { get; set; } = string.Empty;
    public double Amount { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public string Account { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTime DateCreated { get; set; }
}
