using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;

namespace Cya2.Application.Services;

public sealed class DonorContactHydrationService : IDonorContactHydrationService
{
    private readonly IDonorContactReadRepository _donorContactReadRepository;
    private readonly IDonorContactCache _donorContactCache;
    private readonly ICacheInvalidationVersion _cacheInvalidationVersion;

    public DonorContactHydrationService(
        IDonorContactReadRepository donorContactReadRepository,
        IDonorContactCache donorContactCache,
        ICacheInvalidationVersion cacheInvalidationVersion)
    {
        _donorContactReadRepository = donorContactReadRepository;
        _donorContactCache = donorContactCache;
        _cacheInvalidationVersion = cacheInvalidationVersion;
    }

    public async Task HydrateAsync(
        IEnumerable<DonationRecord> donations,
        CancellationToken cancellationToken = default)
    {
        var rows = (donations ?? Enumerable.Empty<DonationRecord>()).ToList();
        var donorIds = rows
            .Select(donation => donation.DonorId)
            .Where(donorId => donorId > 0)
            .Distinct()
            .ToArray();

        if (donorIds.Length == 0)
        {
            return;
        }

        var generation = _cacheInvalidationVersion.DonorContacts;
        var missingIds = donorIds
            .Where(donorId => !_donorContactCache.TryGet(donorId, generation, out _))
            .ToArray();

        if (missingIds.Length > 0)
        {
            var loaded = await _donorContactReadRepository
                .GetByDonorIdsAsync(missingIds, cancellationToken);

            foreach (var donorId in missingIds)
            {
                _donorContactCache.Set(
                    donorId,
                    generation,
                    loaded.Where(contact => contact.DonorId == donorId));
            }
        }

        foreach (var donation in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_donorContactCache.TryGet(donation.DonorId, generation, out var contacts))
            {
                continue;
            }

            donation.Address = JoinContactValues(contacts.Where(contact => IsContactType(contact, "AddressLine1")).Select(contact => contact.ContactValue));
            donation.City = JoinContactValues(contacts.Where(contact => IsContactType(contact, "City")).Select(contact => contact.ContactValue));
            donation.State = JoinContactValues(contacts.Where(contact => IsContactType(contact, "State")).Select(contact => contact.ContactValue));
            donation.PostalCode = JoinContactValues(contacts.Where(contact => IsContactType(contact, "PostalCode")).Select(contact => contact.ContactValue));
            donation.Country = JoinContactValues(contacts.Where(contact => IsContactType(contact, "Country")).Select(contact => contact.ContactValue));
            donation.Email = JoinContactValues(contacts.Where(contact => IsContactType(contact, "Email")).Select(contact => contact.ContactValue));
            donation.PhoneFixed = JoinContactValues(contacts.Where(contact => IsContactType(contact, "HomePhone")).Select(contact => contact.ContactValue));
            donation.PhoneMobile = JoinContactValues(contacts.Where(contact => IsContactType(contact, "MobilePhone")).Select(contact => contact.ContactValue));
        }
    }

    private static bool IsContactType(DonorContactRecord contact, string expectedType)
        => string.Equals(contact.ContactType, expectedType, StringComparison.OrdinalIgnoreCase);

    private static string JoinContactValues(IEnumerable<string?> values)
        => string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));
}
