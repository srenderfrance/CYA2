using System.Diagnostics.CodeAnalysis;

namespace UtilityClassLibrary
{
    public class UserDataDonors
    {
        public required string Name { get; set; }
        public required string Email { get; set; }
        public required string PrimaryPhoneNumber { get; set; }
        public string SecondaryPhoneNumber { get; set; }
        public required string Address { get; set; }
        public required string City { get; set; }
        public required string State { get; set; }
        public required int PostalCode { get; set; }
        public required string Country { get; set; }


        [SetsRequiredMembers]
        public UserDataDonors(string name, string email, string primaryPhoneNumber, string secondaryPhoneNumber, string address, string city, string state, int postalCode, string country) 
        { 
            Name = name;
            Email = email;
            PrimaryPhoneNumber = primaryPhoneNumber;
            SecondaryPhoneNumber = secondaryPhoneNumber;
            Address = address;
            City = city;
            State = state;
            PostalCode = postalCode;
            Country = country;
        
        }

    }
}
