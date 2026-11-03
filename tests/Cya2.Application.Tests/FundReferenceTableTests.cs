using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Application.Services;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class FundReferenceTableTests
{
    [Fact]
    public void BuildRows_IncludesBothSubfundTypesWithParentValuesWithoutMutatingAccounts()
    {
        var parent = new Account { AccountId = 7, Fund = "Primary", AccountingClass = "Class A", AccountNumber = "123", Overhead = 12 };
        var rows = FundReferenceTableService.BuildRows([parent],
        [
            new SubAccount(7, "Merged", "Merged"),
            new SubAccount(7, "Separate", "Separate"),
            new SubAccount(99, "Orphan", "Merged")
        ]);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(7, row.AccountId);
            Assert.Equal("Class A", row.AccountingClass);
            Assert.Equal("123", row.AccountNumber);
            Assert.Equal(12m, row.Overhead);
        });
        Assert.Equal(new[] { "Merged", "Primary", "Separate" }, rows.Select(row => row.Fund));
        Assert.Equal("Primary", parent.Fund);
    }

    [Fact]
    public void BuildRows_UsesCurrentParentByAccountId()
    {
        var first = new Account { AccountId = 1, Fund = "Same", AccountingClass = "First" };
        var second = new Account { AccountId = 2, Fund = "Same", AccountingClass = "Second" };
        var sub = new SubAccount(1, "Child", "Separate");
        Assert.Equal("First", FundReferenceTableService.BuildRows([first, second], [sub]).Single(row => row.Fund == "Child").AccountingClass);
        sub.AccountId = 2;
        Assert.Equal("Second", FundReferenceTableService.BuildRows([first, second], [sub]).Single(row => row.Fund == "Child").AccountingClass);
        Assert.Equal(2, FundReferenceTableService.BuildRows([first, second], []).Count);
    }

    [Fact]
    public async Task AdminPreload_ReusesReferenceCacheAndRefreshesAfterMetadataChange()
    {
        var version = new CacheInvalidationVersion();
        var references = new CachedReferences(version);
        var shared = new AdminSharedCache();
        AdminPreloadService CreatePreload() => new(
            null!, null!, null!, shared, new AccountMetadataCache(), null!, references,
            version, null!, null!, null!, NullLogger<AdminPreloadService>.Instance);

        var first = await CreatePreload().GetAccountsAsync();
        var second = await CreatePreload().GetAccountsAsync();
        Assert.Equal("Class 1", first.Single().AccountingClass);
        Assert.Equal("Class 1", second.Single().AccountingClass);
        Assert.Equal(1, references.Loads);

        version.Invalidate(CacheDataDomains.AccountMetadata);
        var refreshed = await CreatePreload().GetAccountsAsync();
        Assert.Equal("Class 2", refreshed.Single().AccountingClass);
        Assert.Equal(2, references.Loads);
    }

    private sealed class CachedReferences(CacheInvalidationVersion version) : IFundReferenceService
    {
        private readonly FundReferenceCache _cache = new();
        public int Loads { get; private set; }
        public Task<IReadOnlyList<FundReferenceDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
            _cache.GetOrCreateAsync(version.AccountMetadata, _ =>
            {
                Loads++;
                return Task.FromResult<IReadOnlyList<FundReferenceDto>>(
                    [new FundReferenceDto { AccountId = 1, Fund = "Primary", AccountingClass = $"Class {Loads}" }]);
            }, cancellationToken);

        public IReadOnlyList<FundReferenceDto> FilterForUser(IReadOnlyList<FundReferenceDto> references, int userId, bool canAccessAllAccounts) => references;
    }
}
