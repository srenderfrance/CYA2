using Cya2.Application.Services;
using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Cya2.Core.Services;
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
        var service = CreateService(repository, cache);
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
        var service = CreateService(repository, new DonorContactCache());

        await service.HydrateAsync([new DonationRecord { DonorId = 2 }]);
        await service.HydrateAsync([new DonationRecord { DonorId = 2 }]);

        Assert.Equal(1, repository.CallCount);
    }

    [Fact]
    public async Task HydrateAsync_LeavesThirdPartyAddressBlankForSoftCreditDonor()
    {
        var repository = new FakeDonorContactRepository
        {
            Contacts =
            [
                Contact(3, "AddressLine1", "Third-party address"),
                Contact(3, "City", "Third-party city"),
                Contact(3, "State", "TP"),
                Contact(3, "PostalCode", "12345"),
                Contact(3, "Country", "Third-party country"),
                Contact(3, "Email", "donor@example.test")
            ]
        };
        var service = CreateService(repository, new DonorContactCache());
        var donation = new DonationRecord
        {
            DonorId = 3,
            Fund = "GENERAL",
            AccountName = "Employer Giving",
            Addressee = "Employer Giving",
            SoftCreditName = "Jane Donor"
        };

        await service.HydrateAsync([donation]);

        Assert.Equal(string.Empty, donation.Address);
        Assert.Equal(string.Empty, donation.City);
        Assert.Equal(string.Empty, donation.State);
        Assert.Equal(string.Empty, donation.PostalCode);
        Assert.Equal(string.Empty, donation.Country);
        Assert.Equal("donor@example.test", donation.Email);
    }

    [Fact]
    public async Task HydrateAsync_KeepsAddressForDirectSoftCreditOrFamilyDonation()
    {
        var repository = new FakeDonorContactRepository
        {
            Contacts = [Contact(4, "AddressLine1", "Donor address")]
        };
        var service = CreateService(repository, new DonorContactCache());
        var donation = new DonationRecord
        {
            DonorId = 4,
            Fund = "GENERAL",
            AccountName = "Smith Family",
            Addressee = "John and Jane Smith",
            SoftCreditName = "Jane Smith"
        };

        await service.HydrateAsync([donation]);

        Assert.Equal("Donor address", donation.Address);
    }

    private static DonorContactHydrationService CreateService(
        FakeDonorContactRepository repository,
        DonorContactCache cache)
        => new(repository, cache, new CacheInvalidationVersion(), new DonorIdentityResolver());

    [Fact]
    public async Task HydrateAsync_SharedSoftCreditIdentityUsesVerifiedDirectContacts()
    {
        var repository = new FakeDonorContactRepository { Contacts = [Contact(5, "AddressLine1", "Direct donor address")] };
        var donation = new DonationRecord { DonorId = 5, IsSoftCredit = true, AccountName = "Employer", Addressee = "Employer", SoftCreditName = "Alex Donor" };
        await CreateService(repository, new DonorContactCache()).HydrateAsync([donation]);
        Assert.Equal("Direct donor address", donation.Address);
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
