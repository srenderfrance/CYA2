namespace Cya2.Application.DTOs;

public sealed class AdminAccountOverviewDto
{
    public string Fund { get; init; } = string.Empty;
    public decimal CurrentBalance { get; init; }
    public decimal Last12MonthsDonations { get; init; }
}
