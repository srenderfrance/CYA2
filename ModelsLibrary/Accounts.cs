using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ModelsLibrary
{
    public class Account
    {
        public int AccountId { get; set; }
        public string Name { get; set; }
        public string AccountRef{ get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Parameterless constructor
        public Account() { }

        // Constructor with required members

        [SetsRequiredMembers]
        public Account(string name, string accountRef)
        {
            Name = name;
            AccountRef = accountRef;
            CreatedAt = DateTime.Now;
        }
    }
}
