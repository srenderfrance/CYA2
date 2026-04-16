namespace Cya2.Application.DTOs;

public class AdminFundUpsertDto
{
    public string Fund { get; set; } = string.Empty;
    public string AccountingClass { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string SoftCredit { get; set; } = string.Empty;
    public decimal BalanceAdjustment { get; set; }
    public decimal Overhead { get; set; }
}

public class AdminFundOperationDto
{
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
}
