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

The snapshot loader is the single place to create complete snapshots. Do not create partial snapshots in individual feature services. `DashboardSessionCacheService` consumes this shared snapshot path and only maps the raw snapshot into its scoped compatibility payload; it does not issue independent accounting, donation, or subaccount queries.

All bounded account-scoped AccountingData reads use the shared account snapshot, whose canonical loader calls `IExpenseReadRepository.GetAccountingDataForAccountAsync`. The shared query matches the account's `AccountingClass` or `AccountNumber`, applies the broad snapshot date range, and excludes `Prepaids` case-insensitively before data is placed in snapshots or derived caches. `AccountCalculationService` and `ExpenseService` filter the snapshot to the requested inclusive date range before processing. Expense totals on Expenses and Home use the same `ExpenseClassificationService` result and preserve the stored amount sign. Requests outside snapshot coverage, including unrestricted historical balance calculations, retain the direct repository fallback.

Home donation totals use `IAccountCalculationService.CalculateDonationTotalsAsync` or its preloaded-data equivalent. For normal accounts within the snapshot coverage window, the calculation consumes the shared account snapshot and derives the primary-fund plus merged-subaccount totals in memory; separate subaccount totals are retained separately until Home exposes them. Intern funds use the shared intern-designation matching query and include all matched donation rows. Intern and out-of-coverage requests retain the repository fallback because their query semantics or date range are not represented by the normal account snapshot.

Donation and donor account paths use the same boundary. `DonationService` and the account-specific `DonorService.GetDonorSummariesForAccountAsync` path reuse the canonical account snapshot for authorized normal accounts within the coverage window and filter donations in memory for narrower ranges. Generic fund-set queries, all-history queries, intern-designation queries, donor detail/current contact projections, and selected separate-subaccount queries remain repository-backed because they have different scope or projection semantics.

On a direct Home load, the dashboard requests one broad donation window covering all displayed periods. The account-total repository query returns the primary fund and merged subaccounts in one database query; Home derives the month, year, and visualization totals from that data in memory. This avoids one query per period or per fund without changing the separate-subaccount rule.

The Home Custom Date Range card is lazy: it does not load custom summary data during initial page load or account selection. When the user selects Apply, the dashboard first reuses a session account-data cache entry whose window contains the requested range and derives the summary in memory. If no containing cached window exists, it loads only the requested range into the same cache path. This preserves broad shared-snapshot reuse for narrower custom ranges while avoiding unnecessary initial or duplicate queries.

`AccountSnapshotWarmupService` uses an existing snapshot as the data source for derived-cache warming; a snapshot hit must not short-circuit dashboard, donation, expense, or donor-summary cache population. This preserves the cache-first behavior for subsequent Home and feature-page requests.
Warmup requests for the same account are single-flight across initial loading and account selection: an active warmup is joined before an existing snapshot is used to start derived-cache warming. This prevents concurrent Home requests from duplicating derived cache loads.
On Home initialization, the warmup coordinator loads the default account plus up to four non-default accounts. Selecting an account while this preload is running prioritizes that account but does not cancel the remaining automatic preload.

The cache data-version monitor runs in the host as a background coordinator, but it reads source-data markers through `ICacheDataVersionProvider`. The MySQL implementation, including table-marker SQL, is owned by Infrastructure so database mechanics do not leak into the Blazor host.

Cache invalidation maintains domain generations for donations, accounting, account metadata, donor/contact projections, and authorization while retaining the aggregate generation for compatibility. Account snapshots use a composite donations/accounting/account-metadata generation because they contain all three raw data domains. A change in any of those domains therefore creates a new snapshot key without changing broad-to-narrow date-range reuse.

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

## Cache decision matrix

The following matrix records the current implementation contract. A cache's registration lifetime is not, by itself, an authorization boundary; account authorization must be performed by the application service before shared account data is returned.

| Cache | Contents | Scope | Shareable? | Key dimensions | TTL / capacity | Eviction | Invalidation | Authorization check |
|---|---|---|---|---|---|---|---|---|
| Account snapshot | Complete donations, accounting rows, and subaccounts | Process-wide singleton | Yes, by normalized account key and data generation | Account ID, normalized fund, generation | No TTL; maximum 64 entries and approximately 64 MB | LRU | Import, accounting/donation changes, rollback, account metadata changes through the central invalidator; generation changes | Current user/account authorization occurs before service access; snapshot contents contain no user-specific authorization state |
| Dashboard account data | Account donations, accounting rows, and subaccounts for a date window | Blazor circuit (`scoped`) | No cross-circuit sharing | Account fund, containing window, local invalidation version | No TTL; retains default plus most recent non-default account | Removes older non-default entries | Local generation check observes the singleton invalidation version | The dashboard service revalidates context/account access before loading or using the data |
| Donation DTO | Presentation donation data | Process-wide singleton partitioned by user | User-partitioned only | User ID, normalized fund | No TTL; maximum 30 funds per user | Per-user LRU | Central invalidator | User ID is a partition, not authorization; current account authorization is still required before reads |
| Dashboard DTO | Presentation dashboard data | Process-wide singleton partitioned by user | User-partitioned only | User ID, normalized fund | No TTL; maximum 15 funds per user | Per-user LRU | Central invalidator | User/account context is revalidated before cache use |
| Expense DTO | Presentation expense/accounting data | Process-wide singleton | User/account/date-range partitioned | User ID, normalized fund, start date, end date | No TTL or explicit capacity limit currently implemented | None currently implemented | Central invalidator | Account authorization is performed by the expense service before reads |
| Donor summaries | Derived donor summary rows | Process-wide singleton | Shareable by normalized fund signature and date range after authorization | Funds signature, start date, end date | No TTL or explicit capacity limit currently implemented | None currently implemented | Central invalidator | Donor service performs account authorization before cache access |
| Missing-gift warnings | Filtered missing-gift donor summaries | Process-wide singleton | Shareable by account/date dimensions after authorization | Account ID, normalized fund, start date, end date | No TTL or explicit capacity limit currently implemented | None currently implemented | Central invalidator | Home/account service authorization precedes the snapshot-aware donor path |
| Account balance | Cumulative balance through an as-of date | Process-wide singleton | Yes, after authorization | Account ID, accounting class, account number, balance adjustment, generation, as-of date | No TTL; maximum 256 entries | LRU | Central invalidator | Home/Admin authorization occurs before balance access |
| User account context | Account metadata and accessible account list | Process-wide static cache partitioned by user | No direct cross-user sharing | User identity, cache invalidation generation, current role/viewer state | No TTL or capacity limit currently implemented | Explicit per-user removal only | Central invalidator generation; missing users are removed when resolved | User is re-resolved from the database and role/access state is compared before a cached context is returned |
| Selected account | UI selection only | Process-wide memory cache partitioned by user | No financial data sharing | User ID | 24-hour absolute expiration | Memory-cache expiration | Explicit user removal; process restart | Selection is only a preference; selected account is resolved against current accessible accounts |
| Selected date range | UI date-range preference only | Process-wide memory cache partitioned by user | No financial data sharing | User ID | 24-hour absolute expiration | Memory-cache expiration | Explicit user removal; process restart | Not authorization-sensitive |
| Import preview | Temporary uploaded preview and ownership metadata | Process-wide singleton | Never shareable across users | Preview ID, authenticated owner, import type | Maximum 15-minute retention with bounded preview capacity | Expiration/capacity cleanup | Removal after import, failure, abandonment, or expiration | Owner and current administrator authorization are required |
| Import progress | Temporary import status and progress | Process-wide host service | Never shareable across users | Progress ID, authenticated owner | Maximum 15-minute retention for terminal/abandoned records | Expiration/cleanup | Completion, failure, abandonment, or expiration | Owner and current administrator authorization are required |

### Current cache gaps and follow-up requirements

The matrix documents current behavior; it does not imply that every cache already meets the target architecture. The following remain implementation follow-up items:

- Add explicit limits or a documented bounded policy for expense DTO, donor-summary, missing-gift, and user-account-context caches.
- Add separate configurable freshness and capacity settings rather than relying only on hard-coded limits.
- Add cache counters for hits, misses, evictions, expirations, invalidations, and authorization rejections without logging donor values.
- Add expiration-focused tests for caches that currently have no TTL.
- Add integration coverage for authorization changes across shared cache entries.

After a successful import, rollback, or related mutation, central invalidation queues a best-effort Admin preload warmup in a fresh dependency-injection scope. The warmup does not block the mutation and does not make startup dependent on database availability. Admin overview balance totals are published to the shared account-balance cache as numeric cumulative values; Home can reuse exact as-of balances without caching the transaction result lists.

These changes must preserve central invalidation and broad snapshot reuse for narrower contained date ranges.

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

This cache is used by `FinancialDashboardService` for Home and embedded Account Summaries dashboard data. It is scoped to a Blazor circuit and retains only the default account and the most recently used non-default account. Its raw payload is loaded through `IAccountSnapshotCache` and `IAccountSnapshotLoader`, so separate dashboard circuits reuse one canonical account snapshot even though each circuit retains its own compatibility payload.

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

The current process-wide context cache has no time-based expiration or global capacity limit. It is therefore an identified boundedness follow-up item. Do not add an independent context TTL or authorization rule without preserving the current database revalidation and invalidation-generation checks.

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

Accounts Overview displays the account rows before financial aggregates complete. Its aggregate load uses one batched accounting query and one batched non-intern donation query, then applies the existing account and subaccount calculation rules in memory. Intern-designated accounts retain their specialized donation query path.

For non-admin users, the initial account warmup can warm the configured default account plus a bounded set of recent non-default accounts. For Admin users, the initial warmup stops after the configured default account; it does not automatically warm any additional account snapshots. Explicit account selection may still warm the selected account on demand.

Admin preload data must not be used as a reason to warm every account at startup.
The Admin page prioritizes account, staff, and subaccount data needed by its controls, renders those collections, and only then starts default-account and admin preload warmups. Database latency and the overview's per-account calculations must not block the first interactive response. Overview warmup is bounded to two accounts concurrently so it does not monopolize the shared database connection pool. Preload timing and item counts are logged to distinguish cold-load latency from later cache reuse.

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

## Lifetime, restart, and limited-mode behavior

All caches documented here are in-process memory caches. They are lost on application restart, deployment, process recycle, or host replacement; no cache contents are persisted in browser storage or the database. A restart therefore repopulates data through the normal authorization and database paths.

When the database is unavailable, the startup probe and availability monitor place the application in limited mode. Cache services must not turn a database outage into an application-startup failure or unbounded retry/cache growth. Existing cached values may be used only where the surrounding service permits it; new database-dependent loads remain subject to the database guard.

Cache logs may record non-personal counts, ranges, generations, durations, and cache outcomes. They must not record donor names, the `DonationData.Fund` value, raw donor/accounting rows, uploaded workbook values, or credentials.

## Date-range and snapshot rules

The shared snapshot represents a broad supported date range. A narrower request may use that snapshot when the requested range is fully contained within the snapshot range.

The UI does not need to select a special canonical date-range label. The decision is based on date containment.

Donation date ranges are normalized to start at midnight on the selected start date and end at the final tick of the selected end date before repository queries or snapshot loading. This preserves the UI's inclusive date semantics for `DATETIME` donation rows while retaining broad-snapshot reuse.

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
