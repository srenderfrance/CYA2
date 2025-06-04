using System.Diagnostics.CodeAnalysis;

namespace ModelsLibrary;
    public class User
    {
        public int Id { get; set; }
        public string GoogleId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Language { get; set; } = "en";
        public string AuthLevel { get; set; } = "User";
        public int? DefaultAccount { get; set; } = null;
        public string Prefrence { get; set; } = "default"; // Default preference
        public DateTime DateCreated { get; set; } = DateTime.UtcNow;

    // Parameterless constructor
        public User() { }
    
    // Constructor with required members

        [SetsRequiredMembers]
        public User(string googleId, string email, string name, string language, string authLevel, int? defaultAccount)
        {
            GoogleId = googleId;
            Email = email;
            Name = name;
            Language = language;
            AuthLevel = authLevel;
            DefaultAccount = defaultAccount;
            DateCreated = DateTime.Now;
            Prefrence = "default"; // Default preference
    }
    }
