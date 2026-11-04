# Cache Architecture

This is the Web/Admin supplement to [the main cache architecture](../../../docs/CACHE_ARCHITECTURE.md). See [Production review](../../../docs/PRODUCTION_REVIEW.md) for known correctness gaps and acceptance status.

## Admin shared cache

`IAdminSharedCache` stores the Admin page's account, sub-account, staff, and overview data. Entries are generation-aware and are read through `AdminPreloadService`.

User creation, updates, deletion, and account-access changes invalidate the `Authorization` domain. Authorization invalidation explicitly clears `IAdminSharedCache` so the Admin Manage Users dropdown and account selectors reload immediately from the database without requiring an application restart.

Donation imports that reconcile contacts invalidate both `Donations` and `DonorContacts` immediately after confirmed publication. Unknown commit outcomes conservatively invalidate both; validation rejection and confirmed pre-commit failures leave generations unchanged. Accounting publication invalidates `Accounting`. Existing generation-aware summary/overview rebuild scheduling is unchanged. Processor tests verify the emitted flags; warm-cache UI and enabled MySQL acceptance remain required. See [import safety](../../../docs/IMPORT_SAFETY.md).

## Fund References

Admin rollback completion displays warnings separately from success and explicitly identifies partial committed changes. The orchestration layer requests invalidation on partial and uncertain outcomes even if the overall rollback result is unsuccessful. A cache-refresh exception does not reverse or relabel committed database work.

`FundReferenceCache` coalesces primary-account/access-list loads for the metadata/authorization generation. Admin account preload maps these cached references into `IAdminSharedCache` instead of querying Accounts again. Subaccounts continue to load through `AdminPreloadService.GetSubAccountsAsync`, which reuses the shared Admin cache and populates the per-account metadata cache.

The Fund References dialog builds lightweight display rows from those cached primary accounts and subaccounts. Both merged and separate subfunds inherit Class, Fund Num, and Overhead from their parent by `AccountId`; cached entities are not modified. These display rows are not added to the primary-account selector reference cache. Opening the dialog does not introduce a separate repository loader or cache. Account/subaccount writes and account imports invalidate `AccountMetadata`, clearing the shared metadata/reference caches so subsequent rows reflect parent edits, reparenting, and deleted subfunds.
