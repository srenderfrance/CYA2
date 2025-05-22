using System.Diagnostics.CodeAnalysis;

namespace UtilityClassLibrary
{
    public class AccountNameOptions
    {
        public required string WholeString { get; set; }
        public required string Name { get; set; }
        public required string Id { get; set; }

        [SetsRequiredMembers]
        public AccountNameOptions(string wholeString, string name, string id)
        {
            WholeString = wholeString;
            Name = name;
            Id = id;
        }
    }
}
