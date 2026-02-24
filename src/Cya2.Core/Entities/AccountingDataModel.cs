namespace Cya2.Core.Entities;

public class AccountingDataModel : BaseEntity
{
    public int Id { get; set; }
    public string AccountingClass { get; set; } = string.Empty; // Named "class" in QuickBooks CSV
    public DateTime Date { get; set; }
    public string Num { get; set; } = string.Empty;            // Transaction number
    public double Amount { get; set; }                         // Keep as double like current
    public string AccountNumber { get; set; } = string.Empty;  // Account number
    public string Account { get; set; } = string.Empty;        // Account description
    public string Type { get; set; } = string.Empty;           // Transaction type
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;

    // Parameterless constructor
    public AccountingDataModel() { }

    // Constructor for creating new entries
    public AccountingDataModel(string accountingClass, DateTime date, string num, 
                              double amount, string accountNumber, string account, string type)
    {
        AccountingClass = accountingClass?.Trim() ?? string.Empty;
        Date = date;
        Num = num?.Trim() ?? string.Empty;
        Amount = amount;
        AccountNumber = accountNumber?.Trim() ?? string.Empty;
        Account = account?.Trim() ?? string.Empty;
        Type = type?.Trim() ?? string.Empty;
        DateCreated = DateTime.UtcNow;
    }

    // Business logic helper methods
    public bool IsExpense()
    {
        return Amount < 0;
    }

    public bool IsIncome()
    {
        return Amount > 0;
    }

    public bool IsTransfer()
    {
        return string.Equals(Type, "Transfer", StringComparison.OrdinalIgnoreCase);
    }

    public decimal GetAbsoluteAmount()
    {
        return (decimal)Math.Abs(Amount);
    }

    // Update methods with change tracking
    public void UpdateAmount(double newAmount)
    {
        Amount = newAmount;
        SetModified();
    }

    public void UpdateAccountingClass(string newAccountingClass)
    {
        AccountingClass = newAccountingClass?.Trim() ?? string.Empty;
        SetModified();
    }

    public void UpdateAccount(string newAccount)
    {
        Account = newAccount?.Trim() ?? string.Empty;
        SetModified();
    }

    public void UpdateType(string newType)
    {
        Type = newType?.Trim() ?? string.Empty;
        SetModified();
    }

    // Validation for import/admin forms
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(AccountingClass) &&
               Date != default &&
               !string.IsNullOrWhiteSpace(AccountNumber) &&
               !string.IsNullOrWhiteSpace(Account);
    }

    public List<string> GetValidationErrors()
    {
        var errors = new List<string>();
        
        if (string.IsNullOrWhiteSpace(AccountingClass))
            errors.Add("Accounting Class is required");
            
        if (Date == default)
            errors.Add("Date is required");
            
        if (string.IsNullOrWhiteSpace(AccountNumber))
            errors.Add("Account Number is required");
            
        if (string.IsNullOrWhiteSpace(Account))
            errors.Add("Account description is required");

        if (Amount == 0)
            errors.Add("Amount cannot be zero");
        
        return errors;
    }

    // Display helpers
    public string GetDisplayAmount()
    {
        return Amount.ToString("C2");
    }

    public string GetTransactionType()
    {
        if (IsExpense()) return "Expense";
        if (IsIncome()) return "Income";
        return Type;
    }

    // Helper for matching with accounts (like your balance calculation logic)
    public bool MatchesAccount(string fundCode, string accountingClass, string accountNumber)
    {
        return string.Equals(AccountingClass, accountingClass, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(AccountNumber, accountNumber, StringComparison.OrdinalIgnoreCase);
    }

    // Date range filtering (matches your existing logic)
    public bool IsInDateRange(DateTime start, DateTime end)
    {
        return Date.Date >= start.Date && Date.Date <= end.Date;
    }
}