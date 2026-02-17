using Cya2.Core.Enums;

namespace Cya2.Core.Entities;

public class Donation : BaseEntity
{
    public DateTime Date { get; private set; }
    public string AccountName { get; private set; } = string.Empty; // Donor name
    public string PaymentMethod { get; private set; } = string.Empty; // Keep as string like current
    public string GiftType { get; private set; } = string.Empty; // Keep as string like current
    public double Amount { get; private set; } // Keep as double like current
    public string Fund { get; private set; } = string.Empty; // Account fund code
    public string? SoftCreditName { get; private set; }
    public string? Address { get; private set; }
    public string? City { get; private set; }
    public string? State { get; private set; }
    public string? PostalCode { get; private set; }
    public string? Country { get; private set; }
    public string? Email { get; private set; }
    public string? PhoneFixed { get; private set; }
    public string? PhoneMobile { get; private set; }
    public bool IsAnonymous { get; private set; } = false;

    // Import tracking for error reporting
    public int? ImportRowNumber { get; private set; }

    // Navigation properties
    public Account? Account { get; private set; }
    public int? AccountId { get; private set; }

    // Private constructor for EF Core
    private Donation() { }

    public Donation(double amount, DateTime date, string accountName, string fund, 
                   string paymentMethod, string giftType, bool isAnonymous = false)
    {
        // No validation - accept any amount just like current system
        Amount = amount;
        Date = date;
        AccountName = accountName?.Trim() ?? string.Empty;
        Fund = fund?.Trim() ?? string.Empty;
        PaymentMethod = paymentMethod?.Trim() ?? string.Empty;
        GiftType = giftType?.Trim() ?? string.Empty;
        IsAnonymous = isAnonymous;
        
        // Apply anonymity protection immediately if needed
        if (isAnonymous)
        {
            EnsureAnonymityProtection();
        }
    }

    // Factory method for import with validation
    public static Donation CreateFromImportRow(double amount, DateTime date, string accountName, 
                                             string fund, string paymentMethod, string giftType, 
                                             bool isAnonymous, int? excelRowNumber = null)
    {
        var donation = new Donation(amount, date, accountName, fund, paymentMethod, giftType, isAnonymous);
        donation.ImportRowNumber = excelRowNumber;
        
        return donation;
    }

    // Enhanced anonymity protection with double safeguards
    public void EnsureAnonymityProtection()
    {
        if (!IsAnonymous) return;
        
        // Double-check and clear any personal data (defensive programming)
        AccountName = "Anonymous";
        Email = null;
        PhoneFixed = null;
        PhoneMobile = null;
        Address = null;
        City = null;
        State = null;
        PostalCode = null;
        Country = null;
        SoftCreditName = null;
        
        SetModified();
    }

    public void MarkAsAnonymous()
    {
        IsAnonymous = true;
        EnsureAnonymityProtection(); // Apply the double protection
        SetModified();
    }

    public string GetSafeDisplayName()
    {
        // Always check both the flag AND the data for safety (double protection)
        if (IsAnonymous || AccountName == "Anonymous")
            return "Anonymous";
        
        return string.IsNullOrWhiteSpace(AccountName) ? "Unknown" : AccountName;
    }

    public bool HasAnyPersonalData()
    {
        // Helper to identify donations that might need anonymization
        return !string.IsNullOrWhiteSpace(Email) ||
               !string.IsNullOrWhiteSpace(PhoneMobile) ||
               !string.IsNullOrWhiteSpace(PhoneFixed) ||
               !string.IsNullOrWhiteSpace(Address) ||
               !string.IsNullOrWhiteSpace(SoftCreditName);
    }

    // Import validation for Excel error reporting
    public ImportValidationResult ValidateForImport()
    {
        var result = new ImportValidationResult { RowNumber = ImportRowNumber };
        
        // Critical errors that should stop import
        if (string.IsNullOrWhiteSpace(Fund))
            result.Errors.Add("Missing fund information");
        
        if (Date == default)
            result.Errors.Add("Missing date information");
            
        if (Date < new DateTime(1900, 1, 1))
            result.Errors.Add("Date is not formatted correctly or invalid");
        
        // Warnings that user should review but don't stop import
        if (Date > DateTime.Today)
            result.Warnings.Add("Future date");
        
        if (Amount == 0)
            result.Warnings.Add("Zero amount donation");
        
        if (string.IsNullOrWhiteSpace(AccountName) && !IsAnonymous)
            result.Warnings.Add("Missing donor name (not marked anonymous)");
            
        // Critical: Check for anonymity data leaks (should be error)
        if (IsAnonymous && HasAnyPersonalData())
            result.Errors.Add("Anonymous donation contains personal data");
        
        return result;
    }

    // Query helpers that match current filtering logic
    public bool MatchesFundCriteria(IEnumerable<string> acceptableFundNames)
    {
        return acceptableFundNames.Contains(Fund, StringComparer.OrdinalIgnoreCase);
    }
    
    public bool IsInDateRange(DateTime start, DateTime end)
    {
        return Date.Date >= start.Date && Date.Date <= end.Date;
    }
    
    public bool MatchesDonorSearch(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm)) return true;
        
        var displayName = GetSafeDisplayName();
        return displayName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);
    }

    public void ChangeAmount(double newAmount)
    {
        // No validation - just update the amount
        Amount = newAmount;
        SetModified();
    }

    public void SetSoftCredit(string? softCreditName)
    {
        // Apply anonymity protection if needed
        if (IsAnonymous)
        {
            SoftCreditName = null;
        }
        else
        {
            SoftCreditName = softCreditName?.Trim();
        }
        SetModified();
    }

    public void UpdateContactInfo(string? email, string? phoneFixed, string? phoneMobile,
                                 string? address, string? city, string? state, 
                                 string? postalCode, string? country)
    {
        // Apply anonymity protection if needed
        if (IsAnonymous)
        {
            EnsureAnonymityProtection();
            return;
        }

        Email = email?.Trim();
        PhoneFixed = phoneFixed?.Trim();
        PhoneMobile = phoneMobile?.Trim();
        Address = address?.Trim();
        City = city?.Trim();
        State = state?.Trim();
        PostalCode = postalCode?.Trim();
        Country = country?.Trim();
        SetModified();
    }

    public void AssignToAccount(Account account)
    {
        Account = account ?? throw new ArgumentNullException(nameof(account));
        AccountId = account.Id;
        SetModified();
    }

    public void ToggleAnonymous()
    {
        IsAnonymous = !IsAnonymous;
        if (IsAnonymous)
        {
            EnsureAnonymityProtection();
        }
        SetModified();
    }

    public bool IsRecent(DateTime comparisonDate, int days = 30)
    {
        return Date >= comparisonDate.AddDays(-days);
    }
}

// Supporting class for import validation reporting
public class ImportValidationResult
{
    public int? RowNumber { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public bool IsValid => !Errors.Any();
    public bool HasWarnings => Warnings.Any();
    
    public string GetErrorReport()
    {
        var report = new List<string>();
        if (RowNumber.HasValue)
            report.Add($"Row {RowNumber}:");
            
        if (Errors.Any())
            report.AddRange(Errors.Select(e => $"  ERROR: {e}"));
            
        if (Warnings.Any())
            report.AddRange(Warnings.Select(w => $"  WARNING: {w}"));
            
        return string.Join(Environment.NewLine, report);
    }
    
    public string GetWarningReport()
    {
        var report = new List<string>();
        if (RowNumber.HasValue && HasWarnings)
        {
            report.Add($"Row {RowNumber}:");
            report.AddRange(Warnings.Select(w => $"  WARNING: {w}"));
        }
        return string.Join(Environment.NewLine, report);
    }
}