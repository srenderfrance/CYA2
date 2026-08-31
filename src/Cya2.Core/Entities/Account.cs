using Cya2.Core.Enums;

namespace Cya2.Core.Entities;

public class Account : BaseEntity
{
    public int AccountId { get; set; }
    
    public string Fund { get; set; } = string.Empty; // Corresponds to "Fund Notes" from Donation DataTable
    
    public string AccountingClass { get; set; } = string.Empty; // Corresponds to "Class" from the Accounting DataTable
    
    public string AccountNumber { get; set; } = string.Empty; // Corresponds to Account Number from the Accounting DataTable
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Overhead percentage stored as whole number (12 = 12%)
    public decimal Overhead { get; set; }
    
    // Additional properties
    public string SoftCredit { get; set; } = string.Empty;
    public decimal BalanceAdjustment { get; set; }
    public bool OtherFunds { get; set; } = false;

    // Parameterless constructor
    public Account() { }

    // Constructor with required members
    public Account(string fund, string accountingClass, string accountNumber, decimal overhead)
    {
        Fund = fund?.Trim() ?? string.Empty;
        AccountingClass = accountingClass?.Trim() ?? string.Empty;
        AccountNumber = accountNumber?.Trim() ?? string.Empty;
        CreatedAt = DateTime.Now;
        Overhead = overhead;
    }

    // Simple business methods
    public decimal CalculateOverheadAmount(decimal donationTotal)
    {
        return donationTotal * (Overhead / 100);
    }

    public bool HasBalanceAdjustment()
    {
        return BalanceAdjustment != 0;
    }

    public void UpdateFund(string newFund)
    {
        Fund = newFund?.Trim() ?? string.Empty;
        SetModified();
    }

    public void UpdateAccountingClass(string newAccountingClass)
    {
        AccountingClass = newAccountingClass?.Trim() ?? string.Empty;
        SetModified();
    }

    public void UpdateAccountNumber(string newAccountNumber)
    {
        AccountNumber = newAccountNumber?.Trim() ?? string.Empty;
        SetModified();
    }

    public void UpdateOverhead(decimal newOverhead)
    {
        Overhead = newOverhead;
        SetModified();
    }

    public void UpdateBalanceAdjustment(decimal newAdjustment)
    {
        BalanceAdjustment = newAdjustment;
        SetModified();
    }

    public void UpdateSoftCredit(string newSoftCredit)
    {
        SoftCredit = newSoftCredit?.Trim() ?? string.Empty;
        SetModified();
    }

    // Validation helper for admin forms
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(Fund) &&
               !string.IsNullOrWhiteSpace(AccountingClass) &&
               !string.IsNullOrWhiteSpace(AccountNumber);
    }

}