namespace Cya2.Core.Interfaces;

/// <summary>
/// Invalidates all session data caches after a DB import or rollback.
/// Implemented in Application, called from the host import/rollback services.
/// </summary>
public interface IImportCacheInvalidator
{
    void InvalidateAll();
}
