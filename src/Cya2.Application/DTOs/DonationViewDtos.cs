namespace Cya2.Application.DTOs;

public class DonationPivotRowDto
{
    public string Donor { get; set; } = string.Empty;
    public Dictionary<DateTime, decimal> Monthly { get; set; } = new();
    public decimal Total => Monthly.Values.Sum();
}

public class DonationPivotResultDto
{
    public List<DateTime> MonthColumns { get; set; } = new();
    public List<DonationPivotRowDto> Rows { get; set; } = new();
    public Dictionary<DateTime, decimal> MonthTotals { get; set; } = new();
    public decimal GrandTotal { get; set; }
}
