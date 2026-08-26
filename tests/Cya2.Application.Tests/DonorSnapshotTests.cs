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
            SubAccounts = [new SubAccount(42, "FUND-A-SUB", "Separate")],
            AccountDonations = [CreateDonation(1, "FUND-A", "Primary donor")],
            FundDonations = [CreateDonation(2, "FUND-A-SUB", "Subaccount donor")]
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
        Assert.Contains(snapshot.Donations, donation => donation.Fund == "FUND-A-SUB");
        Assert.Single(donations.FundRequests);
        Assert.Equal(["FUND-A", "FUND-A-SUB"], donations.FundRequests[0]);
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
        public List<SubAccount> SubAccounts { get; init; } = [];
        public List<DonationRecord> AccountDonations { get; init; } = [];
        public List<DonationRecord> FundDonations { get; init; } = [];
        public List<List<string>> FundRequests { get; } = [];

        public Task<List<SubAccount>> GetSubAccountsByAccountIdAsync(int accountId) => Task.FromResult(SubAccounts);
        public Task<List<DonationRecord>> GetDonationsByFundsAsync(IEnumerable<string> fundNames) => Task.FromResult(FundDonations);
        public Task<List<DonationRecord>> GetDonationsByAccountAsync(int accountId, string fundName) => Task.FromResult(AccountDonations);
        public Task<List<DonationRecord>> GetDonationsByFundsAndDateRangeAsync(IEnumerable<string> fundNames, DateTime startDate, DateTime endDate)
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
        public Task<List<AccountingRecord>> GetAccountingDataByClassAndDateAsync(string accountingClass, DateTime startDate, DateTime endDate) => Task.FromResult(new List<AccountingRecord>());
        public Task<List<AccountingRecord>> GetAccountingDataByClassOrAccountNumberAndDateAsync(string accountingClass, string accountNumber, DateTime startDate, DateTime endDate) => Task.FromResult(new List<AccountingRecord>());
    }
}
