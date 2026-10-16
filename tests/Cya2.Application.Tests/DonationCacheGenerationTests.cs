using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Application.Services;
using Cya2.Core.Interfaces;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class DonationCacheGenerationTests
{
    [Fact]
    public void DonationDataStoresIndependentDonationAndContactGenerations()
    {
        var data = new DonationDataDto
        {
            CachedStartDate = new DateTime(2024, 1, 1),
            CachedEndDate = new DateTime(2025, 12, 31),
            DonationsGeneration = 7,
            DonorContactsGeneration = 3
        };

        Assert.Equal(7, data.DonationsGeneration);
        Assert.Equal(3, data.DonorContactsGeneration);
        Assert.True(data.CachedStartDate <= new DateTime(2025, 1, 1));
        Assert.True(data.CachedEndDate >= new DateTime(2025, 1, 31));
    }

    [Fact]
    public void ContactInvalidationAdvancesContactGenerationWithoutAdvancingDonationGeneration()
    {
        var version = new CacheInvalidationVersion();
        var donationsGeneration = version.Donations;
        var contactsGeneration = version.DonorContacts;

        version.Invalidate(CacheDataDomains.DonorContacts);

        Assert.Equal(donationsGeneration, version.Donations);
        Assert.NotEqual(contactsGeneration, version.DonorContacts);
    }

    [Fact]
    public void AccountSnapshotGenerationChangesWhenDonationDataChanges()
    {
        var version = new CacheInvalidationVersion();
        var domains = CacheDataDomains.Donations
            | CacheDataDomains.Accounting
            | CacheDataDomains.AccountMetadata;
        var initialGeneration = version.GetGeneration(domains);

        version.Invalidate(CacheDataDomains.Donations);

        Assert.NotEqual(initialGeneration, version.GetGeneration(domains));
    }
}
