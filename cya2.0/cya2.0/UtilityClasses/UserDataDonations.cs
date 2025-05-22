using System.Diagnostics.CodeAnalysis;

namespace newcya.UtilityClasses
{
    public class UserDataDonations
    {
        public required string Account { get; set; } // DonationsDataModel "fund"

        public required string Donor { get; set; } // DonaitonsDataModel "accountname"

        public required DateTime Date { get; set; }

        public required double Amount { get; set; }

        public required string Frequency { get; set; }

        public required string TransactionType { get; set; }

        [SetsRequiredMembers]
        public UserDataDonations(string account, string donor, DateTime date, double amount, string frequency, string transaction)
        {
            Account = account;
            Donor = donor;
            Date = date;
            Amount = amount;
            Frequency = frequency;
            TransactionType = transaction;

        }
    }
}
