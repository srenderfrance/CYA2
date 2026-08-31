# CYA2 Cache Architecture

## Purpose

The application uses several coordinated cache layers to reduce duplicate database work in Blazor Server circuits while preserving correct account, date-range, and authorization behavior.

The design has two goals:

1. Reuse data when the same account and request are loaded repeatedly.
2. Ensure all derived data is discarded when underlying account or financial data changes.

A cache hit is only correct when the cached value still represents the current user authorization, account metadata, data range, and database state.

## Cache layers

### 1. Account snapshot cache

**Primary types:**

- `IAccountSnapshotCache`
- `AccountSnapshotCache`
- `IAccountSnapshotLoader`
- `AccountSnapshotLoader`
- `AccountDataSnapshot`
- `AccountSnapshotKey`

**Registration:** singleton.

The account snapshot is the shared, complete account-level data set used by the main Donations, Expenses, and Donors paths. A snapshot can contain:

- Donations
- Accounting records
- Subaccounts
- The normalized account snapshot key

`AccountSnapshotCache` provides:

- Normalized account keys.
- Single-flight loading so concurrent requests for the same key share one load.
- Bounded storage of up to 64 entries and approximately 64 MB.
- Least-recently-used eviction.
- In-flight load cancellation when the cache is invalidated.
- Protection against a stale in-flight load repopulating the cache after invalidation.

The snapshot loader is the single place to create complete snapshots. Do not create partial snapshots in individual feature services.

`AccountSnapshotWarmupService` uses an existing snapshot as the data source for derived-cache warming; a snapshot hit must not short-circuit dashboard, donation, expense, or donor-summary cache population. This preserves the cache-first behavior for subsequent Home and feature-page requests.
Warmup requests for the same account are single-flight across initial loading and account selection: an active warmup is joined before an existing snapshot is used to start derived-cache warming. This prevents concurrent Home requests from duplicating derived cache loads.
On Home initialization, the warmup coordinator loads the default account plus up to four non-default accounts. Selecting an account while this preload is running prioritizes that account but does not cancel the remaining automatic preload.

The cache data-version monitor runs in the host as a background coordinator, but it reads source-data markers through `ICacheDataVersionProvider`. The MySQL implementation, including table-marker SQL, is owned by Infrastructure so database mechanics do not leak into the Blazor host.

### 2. Session data caches

**Primary types:**

- `ISessionDonationDataCacheService`
- `ISessionExpenseDataCacheService`
- `ISessionDonorSummaryCacheService`
- `ISessionMissingGiftCacheService`
- `ISessionDashboardDtoCacheService`

The session caches store derived or presentation-oriented values. They are separate from the canonical account snapshot because not every request can use a snapshot.

The current registrations are:

| Cache | Lifetime | Typical key/data |
|---|---|---|
| Donation data | Singleton | User, fund, donation DTO |
| Expense data | Singleton | User, fund, date range, expense DTO |
| Donor summaries | Singleton | Funds signature, date range, summaries |
| Missing-gift warnings | Singleton | Account ID, normalized fund, date range, warning donors |
| Dashboard DTO | Singleton | User, fund, dashboard DTO |

All of these caches expose `InvalidateAll()` and are cleared by `ImportCacheInvalidator`.

`SessionDonorSummaryCacheService` is also the cache-aware boundary for the Donors page. Donor summaries are keyed by the normalized funds signature and requested date range. `DonorService` performs a cache lookup before entering the per-request single-flight query lock, then checks again after acquiring the lock so concurrent requests do not duplicate repository work.

`SessionMissingGiftCacheService` stores the already-filtered Home warning result, keyed by account identity and requested date range. `AccountSnapshotWarmupService` populates it immediately after warming donor summaries, so account selection can reuse the result without repeating the donor-summary/database path. It is invalidated by `ImportCacheInvalidator` together with the other shared caches.

Dashboard cache diagnostics identify DTO-cache hits and misses separately from scoped account-data cache hits, misses, and loads. Dashboard service logs also identify whether a request used the complete cache-backed path or the summary-direct path, making cold loads distinguishable from duplicate cache work.

The Donations page may reuse an embedded `SelectedAccountDonations` payload from the dashboard DTO cache before calling `DonationService`. This is a delivery-layer integration optimization: `FinancialDashboardService` and `AccountSnapshotWarmupService` populate the dashboard DTO cache, while `DonationService` remains responsible for donation-specific session-cache, snapshot, and repository policy. Do not move dashboard DTO precedence into `DonationService` or create a second cache policy there.

The Home missing-gift alert path uses `IDonorService.GetMissingGiftDonorsAsync`. This method routes account requests through the snapshot-aware donor-summary path, allowing a broad account snapshot to serve narrower reporting ranges. Missing-gift freshness is determined while summaries are built from the loaded dataset; Home does not independently inspect session donation data or apply a second freshness rule.

The Donors UI prepares the cached summary rows once after loading by populating `DisplayName`, `DisplayAddress`, and `FrequencyLabel`. The grid uses property-only columns for these values rather than repeating formatting logic in cell templates.

### 3. Scoped dashboard account-data cache

**Primary types:**

- `ISessionAccountDataCacheService`
- `DashboardSessionCacheService`

**Registration:** scoped.

This cache is used by `FinancialDashboardService` for Home and embedded Account Summaries dashboard data. It is scoped to a Blazor circuit and retains only the default account and the most recently used non-default account.

Because it is scoped, a singleton invalidation method cannot directly clear every existing instance. `DashboardSessionCacheService` therefore compares its local generation with the singleton `ICacheInvalidationVersion` before serving cached data. When the generation changes, it clears its local values and reloads on demand.

### 4. Account-context cache

**Primary type:** `UserAccountContextService`.

The service maintains a process-wide cached account context keyed by user identity. The context includes account metadata such as:

- Default account.
- Fund name.
- Accounting class and account number.
- Overhead.
- Soft-credit settings.
- `BalanceAdjustment`.

The cached context also stores the cache generation. When the global generation changes, the service reloads the user and account metadata instead of returning the old context.

This is important because a refreshed donation or expense snapshot alone does not refresh stale account metadata.

### 5. Admin preload caches

**Primary types:**

- `IAdminPreloadService` / `AdminPreloadService`
- `IAdminRecentAccountSnapshotService` / `AdminRecentAccountSnapshotService`

These are scoped Admin-specific caches and are intentionally separate from the regular account snapshot path.

`AdminPreloadService` supports bounded popup data such as:

- Account lists.
- Subaccounts.
- Staff data.
- Accounts Overview aggregates.
- Fund References data.

`AdminRecentAccountSnapshotService` warms snapshots for the five most recently selected non-default accounts. If a default account is configured, it is warmed separately and does not consume one of the five recent-account positions.

Admin preload data must not be used as a reason to warm every account at startup.

## Invalidation architecture

### Central invalidation coordinator

**Primary types:**

- `IImportCacheInvalidator`
- `ImportCacheInvalidator`
- `ICacheInvalidationVersion`
- `CacheInvalidationVersion`

`ImportCacheInvalidator.InvalidateAll()` is the central application operation for invalidating account-derived data. It:

1. Clears donation session data.
2. Clears donor summary data.
3. Clears expense session data.
4. Clears dashboard DTO data.
5. Clears all account snapshots.
6. Advances the global cache generation.

Advancing the generation causes:

- Existing scoped dashboard caches to clear on their next access.
- Cached user account contexts to reload current account metadata.
- New snapshot requests to use fresh data.

### When to invalidate

Call the central invalidator after every successful operation that changes data consumed by account or dashboard views, including:

- Donation imports.
- Accounting imports.
- Rollbacks.
- Primary account creation, update, or deletion.
- Subaccount creation, update, or deletion when the change affects displayed account data.
- Changes to account metadata such as `BalanceAdjustment`, `Overhead`, accounting class, account number, fund name, or access-related metadata.

Invalidate only after the database operation succeeds. Do not invalidate for a failed operation unless the operation may have partially changed the database and the failure-handling design requires it.

## Date-range and snapshot rules

The shared snapshot represents a broad supported date range. A narrower request may use that snapshot when the requested range is fully contained within the snapshot range.

The UI does not need to select a special canonical date-range label. The decision is based on date containment.

Correct behavior:

- Load the broad snapshot once.
- Filter its immutable values in memory for narrower contained ranges.
- Reuse the snapshot for repeated requests for the same account.
- Preserve repository/session-cache fallbacks for ranges outside snapshot coverage or unsupported request shapes.

Do not require the selected UI range to equal a special canonical range before using a snapshot.

## Page usage guidance

### Donations

Use the account snapshot for the supported selected-account range after authorization. Derive the requested date range from the immutable snapshot where possible. Preserve existing fallbacks for unsupported ranges, explicit subaccounts, searches, donor-specific queries, and other paths not represented by the snapshot.

### Expenses

Use the account snapshot for aggregate, expense-only, transfer-only, and summary operations when the requested date range is contained in the snapshot range. Filter the broad accounting data in memory. Preserve the existing repository path for unsupported or out-of-coverage requests.

### Donors

Use the shared complete account snapshot for the normal bounded account-range path. The snapshot supplies the primary and subaccount fund metadata and donation records; derive `All`, `Primary`, and valid explicit subaccount selections by filtering the immutable snapshot data in memory. Cache the resulting donor summaries by funds signature and requested date range. Preserve the database path for `All Dates` (which must include donors outside snapshot coverage), out-of-coverage ranges, donor detail, search, and other unsupported request shapes.

The Donors page is cache-aware at the presentation boundary as well as in the application services:

- The initial page state does not force a loading boundary before the asynchronous initialization completes. This prevents a loading bar from being shown merely because a new page component instance was created.
- After the first result, the page retains the grid while account or date-range data is refreshed. The loading boundary is only active when `isLoading` is true and no donor result has yet been displayed.
- Cached donor summaries are still loaded through `IDonorService`; the page does not access the cache implementation directly.
- A cache hit avoids repository work, but the page still performs the normal authorization, account-context, subaccount, and component initialization lifecycle.

This distinction is important: cache hits reduce data access latency, while retaining the rendered grid prevents a cached refresh from replacing the grid with a loading indicator.

### Home and Account Summaries

Home uses dashboard DTO and session account-data caches. Account Summaries embeds the Home dashboard flow, so changes affecting account metadata or dashboard data must use the central invalidator. Clearing only Admin preload data or only the account snapshot is insufficient.

Home starts account warmup after its first render, once the initial Home load has completed. The default account is prioritized first; when that warmup completes, background warmup for the remaining bounded set of recent non-default accounts starts immediately. Warmup must not block the initial Home render, and it must not be implemented with a fixed timer-based delay.

When a default account exists, warmup includes the default account plus up to four recent non-default accounts. When no default account exists, it includes up to five accounts. The selected warning date range is passed through the complete derived-cache warmup for every account, including the default account; the warning cache is therefore ready when Home computes the alert. Selecting another account awaits that account's warmup, then the coordinator resumes the remaining background work afterward.

### Admin

Admin selection should be persisted through `IUserSelectionService` when the existing application behavior requires the selection to follow the user to Donations, Expenses, Donors, or Home. Scoped component state alone is not sufficient for cross-circuit propagation.

Admin popup data should use the bounded Admin preload services. Do not preload regular snapshots for every account.

## Rules for adding a new feature

Before adding a new data-loading feature, answer these questions:

1. Is the data account-scoped, user-scoped, date-range-scoped, or global?
2. Is it already present in `AccountDataSnapshot`?
3. Can the feature derive its result by filtering or transforming an existing immutable snapshot?
4. Does the request fit within the snapshot date range?
5. Is the request shape supported by the snapshot, or does it require a repository fallback?
6. What cache lifetime is correct: singleton, scoped, or no cache?
7. Which account metadata fields affect the result?
8. Which successful mutations make the cached result stale?
9. Does the feature need to participate in `ImportCacheInvalidator`?
10. How will a scoped instance observe global invalidation?

### Preferred implementation pattern

1. Perform authorization before accessing shared cached data.
2. Normalize account and date-range inputs.
3. Reuse `IAccountSnapshotCache` and `IAccountSnapshotLoader` when the data is represented by the snapshot.
4. Use `AccountSnapshotKey` consistently; do not invent a parallel key format.
5. Derive narrow results from immutable snapshot values when the requested range is contained.
6. Use the existing repository path when snapshot coverage does not apply.
7. Add a cache only when repeated work is demonstrated and the lifetime is safe.
8. Add the cache to centralized invalidation if it can become stale after a mutation.
9. If the cache is scoped, observe `ICacheInvalidationVersion` or use another existing cross-circuit invalidation pattern.
10. Add tests for cache reuse, invalidation, fallback behavior, and authorization.

## Common mistakes to avoid

- Loading a partial snapshot in one service and allowing another service to overwrite the same key with different data.
- Requiring a UI label such as `Canonical` instead of checking date containment.
- Reading shared caches before authorization.
- Adding a singleton cache containing user-specific data without including user identity in the key.
- Assuming a scoped cache is cleared when a singleton cache is invalidated.
- Clearing only Admin preload state after changing account metadata.
- Forgetting that `Home` and Account Summaries use dashboard/session caches in addition to regular snapshots.
- Preloading every account for an Admin user at startup.
- Removing repository fallbacks for unsupported ranges or special selections.
- Serving stale values after successful imports, rollback, or account metadata updates.

## Testing requirements

Every new cached feature should have automated coverage for:

- First request loads the expected source.
- Repeated equivalent request reuses the cache.
- A narrower contained date range uses the broad snapshot when applicable.
- An out-of-coverage or unsupported request uses the fallback path.
- A successful mutation invalidates all affected cache layers.
- A scoped cache reloads after the global generation changes.
- Account metadata changes, including `BalanceAdjustment`, are visible after invalidation.
- Unauthorized users cannot obtain data through a shared cache.

The current regression tests are in:

`tests/Cya2.Application.Tests`

Run them from the repository root with:

```powershell
dotnet test ".\tests\Cya2.Application.Tests\Cya2.Application.Tests.csproj"
```

## Operational validation

When validating a new feature, inspect logs for:

- Snapshot miss/load versus snapshot hit.
- Repository fallback versus snapshot-derived result.
- Session-cache hit/miss.
- Cache invalidation.
- Cache generation changes.
- Account-context source `db` versus `cache`.
- Donors page initialization and data-load elapsed time, including cache-hit loads.
- Whether a Donors loading boundary is being shown because there is no prior result, rather than because the donor query missed its cache.
- Page, operation, circuit, and scope correlation values.

A performance improvement is not valid if it produces stale or unauthorized data. Correctness and invalidation behavior take priority over reducing database calls.

## Service registrations

The primary cache lifetimes are centralized in:

`src/Cya2.Application/Extensions/ServiceCollectionExtensions.cs`

Current important registrations include:

- `IAccountSnapshotCache`: singleton.
- `IAccountSnapshotLoader`: scoped.
- `IUserAccountContextService`: scoped service with process-wide context storage.
- `ICacheInvalidationVersion`: singleton.
- `ISessionAccountDataCacheService`: scoped.
- `ISessionDashboardDtoCacheService`: singleton.
- `ISessionDonationDataCacheService`: singleton.
- `ISessionExpenseDataCacheService`: singleton.
- `ISessionDonorSummaryCacheService`: singleton.
- `IImportCacheInvalidator`: singleton.

Review this registration map whenever introducing a new cache or changing cache ownership.
