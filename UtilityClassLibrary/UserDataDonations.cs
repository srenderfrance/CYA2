using System.Diagnostics.CodeAnalysis;

namespace UtilityClassLibrary
{
    public class UserDataDonations
    {
        public required string Account { get; set; } // DonationsDataModel "fund"

        public required string Donor { get; set; } // DonationsDataModel "accountname"

        public required DateTime Date { get; set; }

        public required double Amount { get; set; }

        public required string Frequency { get; set; }

        public required string TransactionType { get; set; }

        // Personal donor information (may be blank if IsAnonymous is true)
        public string Email { get; set; } = string.Empty;
        public string PhoneFixed { get; set; } = string.Empty;
        public string PhoneMobile { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string PostalCode { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string SoftCreditName { get; set; } = string.Empty;

        // Preserve anonymity flag on the display model
        public bool IsAnonymous { get; set; } = false;

        [SetsRequiredMembers]
        public UserDataDonations(
            string account,
            string donor,
            DateTime date,
            double amount,
            string frequency,
            string transaction,
            string email = "",
            string phoneFixed = "",
            string phoneMobile = "",
            string address = "",
            string city = "",
            string state = "",
            string postalCode = "",
            string country = "",
            string softCreditName = "",
            bool isAnonymous = false)
        {
            Account = account;
            Donor = donor;
            Date = date;
            Amount = amount;
            Frequency = frequency;
            TransactionType = transaction;
            Email = email;
            PhoneFixed = phoneFixed;
            PhoneMobile = phoneMobile;
            Address = address;
            City = city;
            State = state;
            PostalCode = postalCode;
            Country = country;
            SoftCreditName = softCreditName;
            IsAnonymous = isAnonymous;
        }
    }
}
