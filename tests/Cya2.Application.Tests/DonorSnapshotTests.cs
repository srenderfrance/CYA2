using Cya2.Application.Services;
using Cya2.Application.Interfaces;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Cya2.Core.ValueObjects;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class DonorSnapshotTests
{
    [Fact]
    public async Task AccountSnapshotLoader_IncludesPrimaryAndSubaccountDonations()
    {
        var donations = new TrackingDonationRepository
        {
            SubAccounts = [new SubAccount(42, "FUND-A-MERGED", "Merged")],
            AccountTotalDonations =
            [
                CreateDonation(1, "FUND-A", "Primary donor"),
                CreateDonation(2, "FUND-A-MERGED", "Merged donor")
            ]
        };
        var loader = new AccountSnapshotLoader(
            donations,
            new EmptyExpenseRepository());

        var snapshot = await loader.LoadAsync(
            new UserAccountContextAccount
            {
                AccountId = 42,
                Fund = "FUND-A",
                AccountingClass = "CLASS-A"
            },
            new DateRange(DateTime.UtcNow.Date.AddYears(-1), DateTime.UtcNow.Date),
            new Cya2.Application.Models.AccountSnapshotKey(42, "FUND-A", 0));

        Assert.Equal(2, snapshot.Donations.Count);
        Assert.Contains(snapshot.Donations, donation => donation.Fund == "FUND-A");
        Assert.Contains(snapshot.Donations, donation => donation.Fund == "FUND-A-MERGED");
        Assert.True(donations.AccountTotalRequested);
    }

    private static DonationRecord CreateDonation(int id, string fund, string donorName) => new()
    {
        Id = id,
        Date = DateTime.UtcNow.Date,
        Fund = fund,
        AccountName = donorName,
        Amount = 10,
        DateCreated = DateTime.UtcNow
    };

    private sealed class TrackingDonationRepository : IDonationReadRepository
    {
        public List<DonationRecord> AccountTotalDonations { get; init; } = [];
        public bool AccountTotalRequested { get; private set; }
        public List<SubAccount> SubAccounts { get; init; } = [];
        public List<DonationRecord> AccountDonations { get; init; } = [];
        public List<DonationRecord> FundDonations { get; init; } = [];
        public List<List<string>> FundRequests { get; } = [];

        public Task<List<SubAccount>> GetSubAccountsByAccountIdAsync(int accountId) => Task.FromResult(SubAccounts);
        public Task<List<DonationRecord>> GetDonationsByFundsAsync(IEnumerable<string> fundNames) => Task.FromResult(FundDonations);
        public Task<List<DonationRecord>> GetDonationsByAccountAsync(int accountId, string fundName) => Task.FromResult(AccountDonations);
        public Task<List<DonationRecord>> GetDonationsForAccountTotalAsync(int accountId, string fundName, DateTime startDate, DateTime endDate)
        {
            AccountTotalRequested = true;
            return Task.FromResult(AccountTotalDonations);
        }
        public Task<List<DonationRecord>> GetDonationsByFundsAndDateRangeAsync(IEnumerable<string> fundNames, DateTime startDate, DateTime endDate)
        {
            FundRequests.Add(fundNames.ToList());
            return Task.FromResult(AccountDonations.Concat(FundDonations).ToList());
        }
        public Task<List<DonationRecord>> GetDonationsByFundNamesAndDateRangeAsync(IEnumerable<string> fundNames, DateTime startDate, DateTime endDate)
        {
            FundRequests.Add(fundNames.ToList());
            return Task.FromResult(AccountDonations.Concat(FundDonations).ToList());
        }
        public Task<List<DonationRecord>> GetDonationsByAccountAndDateRangeAsync(int accountId, string fundName, DateTime startDate, DateTime endDate) => Task.FromResult(AccountDonations);
        public Task<List<DonationRecord>> GetDonationsByFundsAndDonorAsync(IEnumerable<string> fundNames, string donorName) => Task.FromResult(new List<DonationRecord>());
        public Task<List<DonationRecord>> GetDonationsByAccountAndDonorAsync(int accountId, string fundName, string donorName) => Task.FromResult(new List<DonationRecord>());
        public Task<List<DonationRecord>> SearchDonationsByFundsAndDonorAsync(IEnumerable<string> fundNames, string searchTerm) => Task.FromResult(new List<DonationRecord>());
        public Task<List<DonationRecord>> SearchDonationsByAccountAndDonorAsync(int accountId, string fundName, string searchTerm) => Task.FromResult(new List<DonationRecord>());
        public Task<List<DonationRecord>> GetInternDonationsByDesignationAndDateRangeAsync(string internDesignationName, DateTime startDate, DateTime endDate) => Task.FromResult(new List<DonationRecord>());
    }

    private sealed class EmptyExpenseRepository : IExpenseReadRepository
    {
        public Task<IReadOnlyDictionary<int, List<AccountingRecord>>> GetAccountingDataForAccountsAsync(IReadOnlyList<(int AccountId, string AccountingClass, string AccountNumber)> accounts, DateTime startDate, DateTime endDate) => Task.FromResult<IReadOnlyDictionary<int, List<AccountingRecord>>>(new Dictionary<int, List<AccountingRecord>>());
        public Task<List<AccountingRecord>> GetAccountingDataForAccountAsync(string accountingClass, string accountNumber, DateTime startDate, DateTime endDate) => Task.FromResult(new List<AccountingRecord>());
    }
}
