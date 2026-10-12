namespace Cya2.Application.Interfaces;

public interface ICacheInvalidationVersion
{
    long Current { get; }
    long Donations { get; }
    long Accounting { get; }
    long AccountMetadata { get; }
    long DonorContacts { get; }
    long Authorization { get; }
    long GetGeneration(CacheDataDomains domains);
    long Invalidate();
    long Invalidate(CacheDataDomains domains);
}

[Flags]
public enum CacheDataDomains
{
    None = 0,
    Donations = 1,
    Accounting = 2,
    AccountMetadata = 4,
    DonorContacts = 8,
    Authorization = 16,
    All = Donations | Accounting | AccountMetadata | DonorContacts | Authorization
}
