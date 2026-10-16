using Cya2.Application.Services;
using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class DonorContactHydrationTests
{
    [Fact]
    public async Task HydrateAsync_LoadsAndCachesCurrentContacts()
    {
        var repository = new FakeDonorContactRepository
        {
            Contacts =
            [
                Contact(1, "Email", "donor@example.test"),
                Contact(1, "MobilePhone", "555-0100"),
                Contact(1, "AddressLine1", "1 Main St"),
                Contact(1, "City", "Austin"),
                Contact(1, "State", "TX"),
                Contact(1, "PostalCode", "78701")
            ]
        };
        var cache = new DonorContactCache();
        var service = new DonorContactHydrationService(repository, cache, new CacheInvalidationVersion());
        var donation = new DonationRecord { DonorId = 1 };

        await service.HydrateAsync([donation]);
        await service.HydrateAsync([new DonationRecord { DonorId = 1 }]);

        Assert.Equal("donor@example.test", donation.Email);
        Assert.Equal("555-0100", donation.PhoneMobile);
        Assert.Equal("1 Main St", donation.Address);
        Assert.Equal("Austin", donation.City);
        Assert.Equal("TX", donation.State);
        Assert.Equal("78701", donation.PostalCode);
        Assert.Equal(1, repository.CallCount);
    }

    [Fact]
    public async Task HydrateAsync_CachesEmptyContactResults()
    {
        var repository = new FakeDonorContactRepository();
        var service = new DonorContactHydrationService(
            repository,
            new DonorContactCache(),
            new CacheInvalidationVersion());

        await service.HydrateAsync([new DonationRecord { DonorId = 2 }]);
        await service.HydrateAsync([new DonationRecord { DonorId = 2 }]);

        Assert.Equal(1, repository.CallCount);
    }

    private static DonorContactRecord Contact(long donorId, string type, string value)
        => new() { DonorId = donorId, ContactType = type, ContactValue = value };

    private sealed class FakeDonorContactRepository : IDonorContactReadRepository
    {
        public List<DonorContactRecord> Contacts { get; init; } = [];
        public int CallCount { get; private set; }

        public Task<List<DonorContactRecord>> GetByDonorIdsAsync(
            IEnumerable<long> donorIds,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            var ids = donorIds.ToHashSet();
            return Task.FromResult(Contacts.Where(contact => ids.Contains(contact.DonorId)).ToList());
        }
    }
}
