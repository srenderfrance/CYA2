using Cya2.Core.Enums;

namespace Cya2.Core.Entities;

public class User : BaseEntity
{
    public string GoogleId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public string AuthLevel { get; set; } = "User"; // "User", "Intern", "Viewer", "Admin"
    public int? DefaultAccount { get; set; } = null;
    public string Prefrence { get; set; } = "default";

    // Parameterless constructor
    public User() { }

    // Constructor with required members
    public User(string googleId, string email, string name, string language, string authLevel, int? defaultAccount)
    {
        GoogleId = googleId?.Trim() ?? string.Empty;
        Email = email?.Trim() ?? string.Empty;
        Name = name?.Trim() ?? string.Empty;
        Language = language?.Trim() ?? "en";
        AuthLevel = authLevel?.Trim() ?? "User";
        DefaultAccount = defaultAccount;
        DateCreated = DateTime.UtcNow;
        Prefrence = "default";
    }

    // Simple helper methods for authorization checks
    public bool IsAdmin()
    {
        return string.Equals(AuthLevel, "Admin", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsViewer()
    {
        return string.Equals(AuthLevel, "Viewer", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsUser()
    {
        return string.Equals(AuthLevel, "User", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsIntern()
    {
        return string.Equals(AuthLevel, "Intern", StringComparison.OrdinalIgnoreCase);
    }

    public bool CanViewAllAccounts()
    {
        return IsAdmin() || IsViewer();
    }

    public bool CanManageUsers()
    {
        return IsAdmin();
    }

    public bool CanManageAccounts()
    {
        return IsAdmin();
    }

    public bool CanPerformRollback()
    {
        return IsAdmin();
    }

    // Profile update methods
    public void UpdateProfile(string name, string language)
    {
        Name = name?.Trim() ?? string.Empty;
        Language = language?.Trim() ?? "en";
        SetModified();
    }

    public void UpdateEmail(string email)
    {
        Email = email?.Trim() ?? string.Empty;
        SetModified();
    }

    public void UpdateAuthLevel(string authLevel)
    {
        AuthLevel = authLevel?.Trim() ?? "User";
        SetModified();
    }

    public void UpdateDefaultAccount(int? accountId)
    {
        DefaultAccount = accountId;
        SetModified();
    }

    public void UpdatePreferences(string preferences)
    {
        Prefrence = preferences?.Trim() ?? "default";
        SetModified();
    }

    // Helper methods for Google OAuth
    public bool HasGoogleAccount()
    {
        return !string.IsNullOrWhiteSpace(GoogleId);
    }

    public bool IsValidGoogleUser()
    {
        return HasGoogleAccount() && !string.IsNullOrWhiteSpace(Email);
    }

    // Validation helpers
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(Email) &&
               !string.IsNullOrWhiteSpace(Name) &&
               !string.IsNullOrWhiteSpace(GoogleId);
    }

    public List<string> GetValidationErrors()
    {
        var errors = new List<string>();
        
        if (string.IsNullOrWhiteSpace(Email))
            errors.Add("Email is required");
            
        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Name is required");
            
        if (string.IsNullOrWhiteSpace(GoogleId))
            errors.Add("Google ID is required");

        if (!IsValidEmail())
            errors.Add("Email format is invalid");
        
        return errors;
    }

    private bool IsValidEmail()
    {
        if (string.IsNullOrWhiteSpace(Email))
            return false;

        try
        {
            var addr = new System.Net.Mail.MailAddress(Email);
            return addr.Address == Email;
        }
        catch
        {
            return false;
        }
    }

    // Display helpers
    public string GetDisplayName()
    {
        return !string.IsNullOrWhiteSpace(Name) ? Name : Email;
    }

    public string GetAuthLevelDisplay()
    {
        return AuthLevel switch
        {
            "Admin" => "Administrator",
            "Viewer" => "Viewer",
            "Intern" => "Intern",
            "User" => "User",
            _ => "Unknown"
        };
    }

    // For admin UI - check if user has specific account access
    public bool HasDefaultAccount()
    {
        return DefaultAccount.HasValue && DefaultAccount.Value > 0;
    }
}