namespace Cya2.Core.Entities;

public class AccountingEntry : BaseEntity
{
    public DateTime Date { get; private set; }
    public decimal Amount { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string AccountFund { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    
    // Navigation properties
    public Account? Account { get; private set; }
    public int? AccountId { get; private set; }

    // Private constructor for EF Core
    private AccountingEntry() { }

    public AccountingEntry(DateTime date, decimal amount, string description, string accountFund, string category = "")
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required", nameof(description));
        
        if (string.IsNullOrWhiteSpace(accountFund))
            throw new ArgumentException("Account fund is required", nameof(accountFund));

        Date = date;
        Amount = amount;
        Description = description.Trim();
        AccountFund = accountFund.Trim();
        Category = category?.Trim() ?? string.Empty;
    }

    public void UpdateAmount(decimal newAmount)
    {
        Amount = newAmount;
        SetModified();
    }

    public void UpdateDescription(string newDescription)
    {
        if (string.IsNullOrWhiteSpace(newDescription))
            throw new ArgumentException("Description is required", nameof(newDescription));

        Description = newDescription.Trim();
        SetModified();
    }

    public void UpdateCategory(string newCategory)
    {
        Category = newCategory?.Trim() ?? string.Empty;
        SetModified();
    }

    public bool IsExpense() => Amount < 0;
    public bool IsIncome() => Amount > 0;

    public void AssignToAccount(Account account)
    {
        Account = account ?? throw new ArgumentNullException(nameof(account));
        AccountId = account.Id;
        SetModified();
    }
}