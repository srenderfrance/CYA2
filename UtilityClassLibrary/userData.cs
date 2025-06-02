
using System.Diagnostics.CodeAnalysis;
using ModelsLibrary;
public class UserData
    {
        
        public int Id { get; }
        public string Email { get; set; }
        public string Name { get; set; }
        public string Language { get; set; }
        public string AuthLevel { get; set; }
        public string DefaultAccount { get; set; }
        public List<Account> Accounts { get; set; }
        public string Prefrence { get; set; } = "default"; // Default preference        

    // Parameterless constructor
        public UserData() { }
    
    // Constructor with required members

        [SetsRequiredMembers]

        public UserData(int id, string email, string name, string language, string authLevel, string defaultAccount, List<Account> accounts, string prefrence)
        {
            Id =id;
            Email = email;
            Name = name;
            Language = language;
            AuthLevel = authLevel;
            DefaultAccount = defaultAccount;
            Accounts = accounts;
            Prefrence = prefrence;
    }
    }