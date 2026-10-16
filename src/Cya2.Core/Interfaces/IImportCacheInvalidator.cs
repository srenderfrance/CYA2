namespace Cya2.Core.Interfaces;

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

/// <summary>
/// Invalidates all session data caches after a DB import or rollback.
/// Implemented in Application, called from the host import/rollback services.
/// </summary>
public interface IImportCacheInvalidator
{
    void InvalidateAll();
    void Invalidate(CacheDataDomains domains);
}
