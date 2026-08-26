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

### 2. Session data caches

**Primary types:**

- `ISessionDonationDataCacheService`
- `ISessionExpenseDataCacheService`
- `ISessionDonorSummaryCacheService`
- `ISessionDashboardDtoCacheService`

The session caches store derived or presentation-oriented values. They are separate from the canonical account snapshot because not every request can use a snapshot.

The current registrations are:

| Cache | Lifetime | Typical key/data |
|---|---|---|
| Donation data | Singleton | User, fund, donation DTO |
| Expense data | Singleton | User, fund, date range, expense DTO |
| Donor summaries | Singleton | Funds signature, date range, summaries |
| Dashboard DTO | Singleton | User, fund, dashboard DTO |

All of these caches expose `InvalidateAll()` and are cleared by `ImportCacheInvalidator`.

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

Use the shared donation snapshot for the normal account-range summary path. Preserve separate queries for `All`, explicit subaccounts, donor detail, search, and other paths whose data is not included in the primary account snapshot.

### Home and Account Summaries

Home uses dashboard DTO and session account-data caches. Account Summaries embeds the Home dashboard flow, so changes affecting account metadata or dashboard data must use the central invalidator. Clearing only Admin preload data or only the account snapshot is insufficient.

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
