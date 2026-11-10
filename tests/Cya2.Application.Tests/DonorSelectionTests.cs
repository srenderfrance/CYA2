using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Application.Services;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Cya2.Core.Services;
using Cya2.Core.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class DonorSelectionTests
{
    [Fact]
    public async Task SharedDonorAcrossFundsIsOneRow_WhileSameNameDifferentIdsStaySeparate()
    {
        var repository = new DonationRepositoryStub();
        repository.Donations[0].DonorDisplayName = "Same Name";
        repository.Donations[1].DonorDisplayName = "Alternate Name";
        repository.Donations[1].DonorId = repository.Donations[0].DonorId;
        repository.Donations[2].DonorDisplayName = "Same Name";
        var result = await CreateService(repository).GetDonorSummariesForSelectionAsync(
            new AccountOptionDto { AccountId = 42, Fund = "Primary" }, "All", null);
        Assert.Equal(2, result.Count);
        Assert.Equal(20m, result.Single(row => row.DonorId == 1).Total);
        Assert.Equal(10m, result.Single(row => row.DonorId == 3).Total);
    }

    [Theory]
    [InlineData("Primary", false, "Primary,Merged")]
    [InlineData("Primary", true, "Primary,Merged")]
    [InlineData("All", false, "Primary,Merged,Separate")]
    [InlineData("All", true, "Primary,Merged,Separate")]
    [InlineData("Sub_2", false, "Separate")]
    [InlineData("Sub_2", true, "Separate")]
    [InlineData("Sub_1", false, "")]
    [InlineData("Sub_99", true, "")]
    public async Task Selection_UsesMergedFundsAsPrimary(string selection, bool ranged, string expected)
    {
        var repository = new DonationRepositoryStub();
        var service = CreateService(repository);
        var account = new AccountOptionDto { AccountId = 42, Fund = "Primary" };
        var range = ranged ? new DateRange(new DateTime(2025, 1, 1), new DateTime(2025, 12, 31)) : null;

        var result = await service.GetDonorSummariesForSelectionAsync(account, selection, range, "7");

        Assert.Equal(expected.Split(',', StringSplitOptions.RemoveEmptyEntries).OrderBy(x => x), result.Select(x => x.Name).OrderBy(x => x));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task All_WithOnlyMergedSubaccounts_IncludesMerged(bool ranged)
    {
        var repository = new DonationRepositoryStub();
        repository.SubAccounts.RemoveAll(s => s.IsSeparate());
        var service = CreateService(repository);
        var result = await service.GetDonorSummariesForSelectionAsync(
            new AccountOptionDto { AccountId = 42, Fund = "Primary" }, "All",
            ranged ? new DateRange(new DateTime(2025, 1, 1), new DateTime(2025, 12, 31)) : null, "7");
        Assert.Equal(new[] { "Merged", "Primary" }, result.Select(x => x.Name).OrderBy(x => x));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InternSelection_UsesInternIdentityNotFund(bool ranged)
    {
        var repository = new DonationRepositoryStub();
        var service = CreateService(repository);
        var account = new AccountOptionDto { AccountId = 42, Fund = "Shared", InternName = "Example Intern" };
        var result = await service.GetDonorSummariesForSelectionAsync(account, "All",
            ranged ? new DateRange(new DateTime(2025, 1, 1), new DateTime(2025, 12, 31)) : null, "7");
        Assert.Equal("Intern donor", Assert.Single(result).Name);
        if (!ranged) Assert.Equal("Example Intern", repository.LastInternName);
        Assert.Equal(0, repository.FundReadCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InternSelections_SharedSourceFundKeepDistinctCacheEntries(bool ranged)
    {
        var repository = new DonationRepositoryStub();
        repository.InternDonation.Fund = "Shared";
        repository.InternDonations["First Intern"] = repository.InternDonation;
        repository.InternDonations["Second Intern"] = new DonationRecord { Id = 5, DonorId = 5, AccountName = "Second donor", DonorDisplayName = "Second donor", Fund = "Shared", Date = new DateTime(2025, 3, 1), Amount = 20 };
        var service = CreateService(repository);
        var range = ranged ? new DateRange(new DateTime(2025, 1, 1), new DateTime(2025, 12, 31)) : null;

        var first = await service.GetDonorSummariesForSelectionAsync(new AccountOptionDto { AccountId = 42, Fund = "Shared", InternName = "First Intern" }, "All", range, "7");
        var second = await service.GetDonorSummariesForSelectionAsync(new AccountOptionDto { AccountId = 43, Fund = "Shared", InternName = "Second Intern" }, "All", range, "7");

        Assert.Equal("Intern donor", Assert.Single(first).Name);
        Assert.Equal("Second donor", Assert.Single(second).Name);
    }

    [Fact]
    public async Task AccountSummary_SharedFundAcrossInternIdentities_CurrentlyReusesFirstSummary()
    {
        var repository = new DonationRepositoryStub();
        repository.InternDonation.Fund = "Shared";
        repository.InternDonations["First Intern"] = repository.InternDonation;
        repository.InternDonations["Second Intern"] = new DonationRecord { Id = 5, DonorId = 5, AccountName = "Second donor", DonorDisplayName = "Second donor", Fund = "Shared", Date = new DateTime(2025, 3, 1), Amount = 20 };
        var service = CreateService(repository);
        var range = new DateRange(new DateTime(2025, 1, 1), new DateTime(2025, 12, 31));

        var first = await service.GetDonorSummariesForAccountAsync(new AccountOptionDto { AccountId = 42, Fund = "Shared", InternName = "First Intern" }, range, "7");
        var second = await service.GetDonorSummariesForAccountAsync(new AccountOptionDto { AccountId = 43, Fund = "Shared", InternName = "Second Intern" }, range, "8");

        Assert.Equal("Intern donor", Assert.Single(first).Name);
        Assert.Equal("Intern donor", Assert.Single(second).Name);
    }

    [Fact]
    public async Task InternSelection_AuthorizationGenerationChangeDoesNotReusePriorSummary()
    {
        var repository = new DonationRepositoryStub();
        var versions = new CacheInvalidationVersion();
        var service = CreateService(repository, versions);
        var account = new AccountOptionDto { AccountId = 42, Fund = "Intern: Example Intern", InternName = "Example Intern" };
        var range = new DateRange(new DateTime(2025, 1, 1), new DateTime(2025, 12, 31));

        var before = await service.GetDonorSummariesForSelectionAsync(account, "All", range, "7");
        repository.InternDonations["Example Intern"] = new DonationRecord { Id = 5, DonorId = 5, AccountName = "Replacement donor", DonorDisplayName = "Replacement donor", Fund = "Shared", Date = new DateTime(2025, 3, 1), Amount = 20 };
        versions.Invalidate(CacheDataDomains.Authorization);
        var after = await service.GetDonorSummariesForSelectionAsync(account, "All", range, "7");

        Assert.Equal("Intern donor", Assert.Single(before).Name);
        Assert.Equal("Replacement donor", Assert.Single(after).Name);
    }

    private static DonorService CreateService(DonationRepositoryStub repository, CacheInvalidationVersion? versions = null) => new(
        repository, new NoOpHydration(), new FactStub(repository), NullLogger<DonorService>.Instance,
        new DonorSummaryCache(), new MissingGiftCache(), versions ?? new CacheInvalidationVersion(),
        new DonorFrequencyService(), new DonorMissingGiftService(), new DonorIdentityResolver());

    private sealed class NoOpHydration : IDonorContactHydrationService
    {
        public Task HydrateAsync(IEnumerable<DonationRecord> donations, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FactStub(DonationRepositoryStub repository) : IAccountFactDataService
    {
        public Task<DonationCalculationData> GetDonationDataAsync(UserAccountContextAccount account, string userId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
            => Task.FromResult(new DonationCalculationData { Donations = string.IsNullOrWhiteSpace(account.InternName) ? repository.Donations : [repository.GetInternDonation(account.InternName)], SubAccounts = repository.SubAccounts });
        public Task<List<AccountingRecord>> GetAccountingDataAsync(UserAccountContextAccount account, string userId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default) => Task.FromResult(new List<AccountingRecord>());
    }

    private sealed class DonationRepositoryStub : IDonationReadRepository
    {
        public List<SubAccount> SubAccounts { get; } = [new(42, "Merged", "Merged") { Id = 1 }, new(42, "Separate", "Separate") { Id = 2 }];
        public List<DonationRecord> Donations { get; } = [Row(1, "Primary"), Row(2, "Merged"), Row(3, "Separate")];
        public DonationRecord InternDonation { get; } = Row(4, "Intern donor");
        public Dictionary<string, DonationRecord> InternDonations { get; } = new(StringComparer.Ordinal);
        public DonationRecord GetInternDonation(string name) => InternDonations.TryGetValue(name, out var donation) ? donation : InternDonation;
        public string? LastInternName { get; private set; }
        public int FundReadCount { get; private set; }
        private static DonationRecord Row(int id, string name) => new() { Id = id, DonorId = id, AccountName = name, DonorDisplayName = name, Fund = name, Date = new DateTime(2025, 3, 1), Amount = 10 };
        public Task<List<SubAccount>> GetSubAccountsByAccountIdAsync(int accountId) => Task.FromResult(SubAccounts);
        public Task<List<DonationRecord>> GetDonationsByFundsAsync(IEnumerable<string> funds) { FundReadCount++; return Task.FromResult(Donations.Where(d => funds.Contains(d.Fund, StringComparer.OrdinalIgnoreCase)).ToList()); }
        public Task<List<DonationRecord>> GetInternDonationsByInternNameAndDateRangeAsync(string internName, DateTime start, DateTime end) { LastInternName = internName; return Task.FromResult(new List<DonationRecord> { GetInternDonation(internName) }); }
        public Task<List<DonationRecord>> GetDonationsByAccountAsync(int id, string fund) => throw new NotImplementedException();
        public Task<List<DonationRecord>> GetDonationsByFundsAndDateRangeAsync(IEnumerable<string> funds, DateTime start, DateTime end) => throw new NotImplementedException();
        public Task<List<DonationRecord>> GetDonationsByFundNamesAndDateRangeAsync(IEnumerable<string> funds, DateTime start, DateTime end) => throw new NotImplementedException();
        public Task<List<DonationRecord>> GetDonationsByAccountAndDateRangeAsync(int id, string fund, DateTime start, DateTime end) => throw new NotImplementedException();
        public Task<List<DonationRecord>> GetDonationsForAccountTotalAsync(int id, string fund, DateTime start, DateTime end) => throw new NotImplementedException();
        public Task<List<DonationRecord>> GetDonationsByFundsAndDonorAsync(IEnumerable<string> funds, string name) => throw new NotImplementedException();
        public Task<List<DonationRecord>> GetDonationsByAccountAndDonorAsync(int id, string fund, string name) => throw new NotImplementedException();
        public Task<List<DonationRecord>> SearchDonationsByFundsAndDonorAsync(IEnumerable<string> funds, string name) => throw new NotImplementedException();
        public Task<List<DonationRecord>> SearchDonationsByAccountAndDonorAsync(int id, string fund, string name) => throw new NotImplementedException();
    }
}
