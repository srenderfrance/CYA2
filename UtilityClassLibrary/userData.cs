using System.Diagnostics.CodeAnalysis;
using ModelsLibrary;

public class UserData
{
    public int Id { get; }
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string AuthLevel { get; set; } = string.Empty;
    public string DefaultAccount { get; set; } = string.Empty;
    public List<Account> Accounts { get; set; } = new List<Account>();
    public string Prefrence { get; set; } = "default"; // Default preference        

    // Parameterless constructor
    public UserData() { }
    
    // Constructor with required members
    [SetsRequiredMembers]
    public UserData(int id, string email, string name, string language, string authLevel, string defaultAccount, List<Account> accounts, string prefrence)
    {
        Id = id;
        Email = email;
        Name = name;
        Language = language;
        AuthLevel = authLevel;
        DefaultAccount = defaultAccount;
        Accounts = accounts;
        Prefrence = prefrence;
    }
}