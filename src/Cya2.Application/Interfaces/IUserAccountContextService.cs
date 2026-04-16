namespace Cya2.Application.Interfaces;

public interface IUserAccountContextService
{
    Task<UserAccountContext?> GetContextAsync(string userId, bool isAdminOrViewerHint = false);
    UserAccountContextAccount? ResolveSelectedAccount(UserAccountContext context, string? preferredFund);
}

public sealed class UserAccountContext
{
    public int UserId { get; set; }
    public bool IsAdminOrViewer { get; set; }
    public int? DefaultAccountId { get; set; }
    public List<UserAccountContextAccount> Accounts { get; set; } = new();
}

public sealed class UserAccountContextAccount
{
    public int AccountId { get; set; }
    public string Fund { get; set; } = string.Empty;
    public string AccountingClass { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public decimal Overhead { get; set; }
    public string SoftCredit { get; set; } = string.Empty;
    public decimal BalanceAdjustment { get; set; }
    public bool OtherFunds { get; set; }
}
