# Cache Architecture

## Admin shared cache

`IAdminSharedCache` stores the Admin page's account, sub-account, staff, and overview data. Entries are generation-aware and are read through `AdminPreloadService`.

User creation, updates, deletion, and account-access changes invalidate the `Authorization` domain. Authorization invalidation explicitly clears `IAdminSharedCache` so the Admin Manage Users dropdown and account selectors reload immediately from the database without requiring an application restart.