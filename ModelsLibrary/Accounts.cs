using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ModelsLibrary
{
    internal class Account
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string AccountRef{ get; set; }
        public DateTime DateCreated { get; set; } = DateTime.UtcNow;

        // Parameterless constructor
        public Account() { }

        // Constructor with required members

        [SetsRequiredMembers]
        public Account(string name, string accountRef)
        {
            Name = name;
            AccountRef = accountRef;
            DateCreated = DateTime.Now;
        }
    }
}
