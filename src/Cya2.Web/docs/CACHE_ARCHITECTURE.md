# Cache Architecture

## Admin shared cache

`IAdminSharedCache` stores the Admin page's account, sub-account, staff, and overview data. Entries are generation-aware and are read through `AdminPreloadService`.

User creation, updates, deletion, and account-access changes invalidate the `Authorization` domain. Authorization invalidation explicitly clears `IAdminSharedCache` so the Admin Manage Users dropdown and account selectors reload immediately from the database without requiring an application restart.

Donation imports that reconcile donor contact projections invalidate both `Donations` and `DonorContacts`. This ensures hydrated donor contact data and donor summaries do not continue serving contact values from before the import.

## Fund References

`FundReferenceCache` coalesces primary-account/access-list loads for the metadata/authorization generation. Admin account preload maps these cached references into `IAdminSharedCache` instead of querying Accounts again. Subaccounts continue to load through `AdminPreloadService.GetSubAccountsAsync`, which reuses the shared Admin cache and populates the per-account metadata cache.

The Fund References dialog builds lightweight display rows from those cached primary accounts and subaccounts. Both merged and separate subfunds inherit Class, Fund Num, and Overhead from their parent by `AccountId`; cached entities are not modified. These display rows are not added to the primary-account selector reference cache. Opening the dialog does not introduce a separate repository loader or cache. Account/subaccount writes and account imports invalidate `AccountMetadata`, clearing the shared metadata/reference caches so subsequent rows reflect parent edits, reparenting, and deleted subfunds.
