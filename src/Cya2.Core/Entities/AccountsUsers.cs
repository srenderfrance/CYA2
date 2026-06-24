namespace Cya2.Core.Entities;

public class AccountsUsers : BaseEntity
{
    public int UserId { get; set; }
    public int AccountId { get; set; }

    // Parameterless constructor
    public AccountsUsers() { }

    // Constructor for creating new user-account relationships
    public AccountsUsers(int userId, int accountId)
    {
        UserId = userId;
        AccountId = accountId;
    }

    // Simple validation for admin forms
    public bool IsValid()
    {
        return UserId > 0 && AccountId > 0;
    }

    public List<string> GetValidationErrors()
    {
        var errors = new List<string>();
        
        if (UserId <= 0)
            errors.Add("User ID is required");
            
        if (AccountId <= 0)
            errors.Add("Account ID is required");
        
        return errors;
    }

    // Helper methods for admin UI
    public void UpdateAssociation(int newUserId, int newAccountId)
    {
        UserId = newUserId;
        AccountId = newAccountId;
        SetModified();
    }

    // Display helper for admin debugging
    public string GetDisplayInfo()
    {
        return $"User {UserId} -> Account {AccountId}";
    }
}