# CYA2 Cache Architecture

## Purpose

The application uses several coordinated cache layers to reduce duplicate database work in Blazor Server circuits while preserving correct account, date-range, and authorization behavior.

The design has two goals:

1. Reuse data when the same account and request are loaded repeatedly.
2. Ensure all derived data is discarded when underlying account or financial data changes.

A cache hit is only correct when the cached value still represents the current user authorization, account metadata, data range, and database state.

## Cache layers

### Admin overview warmup and fallback

`IAdminCacheWarmupCoordinator` queues the central rebuild after invalidation. Admin Overview waits for an active rebuild before attempting a fallback, so a temporary shared-cache miss does not immediately trigger account-by-account database work. Requests received while a rebuild is active coalesce into one guaranteed follow-up rebuild after the active rebuild completes, including when the active rebuild fails; if any coalesced request requires Home summary warming, the follow-up warms summaries. If the rebuild is unavailable or fails, `AdminPreloadService` loads the overview donation and accounting facts in bulk, calculates the DTOs in memory through `RebuildOverviewFromDataAsync`, and writes the result to the shared overview cache. The Blazor overview remains in its loading state until those financial DTOs are available; it does not render zero-valued placeholder rows.

The application-start rebuild uses the same bulk snapshot for shared facts, Admin Overview, and Home summary entries. Consequently, opening Account Summaries after startup reuses the four-period entries already created from that snapshot instead of rebuilding each account through the user-facing dashboard path. Donation, accounting, or account-metadata invalidation also regenerates these summary entries in bulk.

### 1. Authoritative donation/donor and accounting caches

**Primary types:**

- `IDonationDataCache` / `DonationDataCache`
- `IAccountingDataCache` / `AccountingDataCache`
- `IDonationDataCacheLoader` / `DonationDataCacheLoader`
- `IAccountingDataCacheLoader` / `AccountingDataCacheLoader`
- `DonationDataCacheKey` / `AccountingDataCacheKey`

**Registration:** singleton.

The two authoritative caches are shared, compact account-level fact stores used by Home, Donations, Expenses, Donors, and account calculations. They intentionally avoid complete page DTOs, page state, totals, authorization state, and duplicated current contact projections.

The donation/donor cache contains one entry per account and standard date range, regardless of the selected fund:

- Donations
- Subaccounts

The cached donation records retain their original `Fund`. Primary and merged-subfund rows are available for combined account totals; separate-subfund rows remain identifiable for separate views. Account-scoped repository loading includes the primary fund and every configured subfund without allowing identical fund names from another account to enter the cache.

The accounting cache contains accounting transaction facts. Intern accounts use the donation/donor cache only; accounting data is not loaded for intern funds.

Each bounded split cache provides:

- Normalized account keys.
- Single-flight loading so concurrent requests for the same key share one load.
- Bounded storage of up to 64 entries and approximately 64 MB.
- Least-recently-used eviction.
- In-flight load cancellation when the cache is invalidated.
- Protection against a stale in-flight load repopulating the cache after invalidation.

Loaders project only reusable facts. Feature services project page-specific DTOs per request. Home post-render warmup checks both authoritative entries and loads only missing broad-window entries.

The shared standard window is January 1 of the previous calendar year through the current date. Requests outside that window use two bounded singleton user-scoped overflow caches:

- `IUserDonationDataCache` / `UserDonationDataCache`
- `IUserAccountingDataCache` / `UserAccountingDataCache`

Overflow keys include user ID, account/domain identity, and generation; the date range is stored as mergeable coverage within the entry rather than as part of the key. Each domain cache allows up to 256 stable user/account keys globally, while each key retains at most three merged historical coverage segments. Overlapping or adjacent requests reuse and extend the same entry, so a user normally has one entry per account/domain rather than one entry per request. Overflow data is populated only for dates outside the shared window, remains reusable until LRU eviction or logout, and is never used as a substitute for authorization. Requests combine shared facts, that user's existing overflow facts, and only still-missing database ranges, then deduplicate by record ID. Concurrent callers share one load: cancellation of an individual waiter does not cancel the shared factory, while user/global invalidation cancels matching factories and fences their results so stale data cannot be inserted after invalidation. In-flight cleanup is identity-safe, so an older waiter cannot remove a newer load for the same request. User transitions call `IUserScopedCacheInvalidator.RemoveUser`; import invalidation prunes stale overflow generations.

`DonationCacheRecord` stores donation facts and donor identity only. Current email, phone, and address values remain in the separate `IDonorContactCache` projection.

All bounded account-scoped AccountingData reads use the accounting cache, whose loader calls `IExpenseReadRepository.GetAccountingDataForAccountAsync`. The query matches the account's `AccountingClass` or `AccountNumber` and applies the broad cache date range. `IAccountFactDataService` is the only asynchronous fact-loading boundary for `AccountCalculationService`; normal calculation calls require the authenticated user ID. It filters cached records to the requested inclusive range. If a request extends outside the broad window, `AccountFactDataService` loads only uncovered date segments into the user-scoped accounting overflow cache and deduplicates the combined facts by record ID. `ExpenseService` uses this single fact-data boundary for all expense, transfer, and summary projections.

Home donation totals use `IAccountCalculationService.CalculateDonationTotalsAsync` or its preloaded-data equivalent. Normal asynchronous calculations require the authenticated user ID and consume facts supplied by `IAccountFactDataService`; derived-cache rebuilds use the pure preloaded-data methods. For normal accounts, the calculation consumes cached donation facts and derives the primary-fund plus merged-subaccount totals in memory; separate subaccount totals remain distinguishable. If the requested range extends outside the broad cache window, cached and repository donation rows are combined and deduplicated. Intern funds retain their intern-designation repository query semantics.

Donation and donor account paths use the same boundary. `DonationService` and the account-specific `DonorService.GetDonorSummariesForAccountAsync` path reuse compact donation facts for authorized normal accounts and filter donations in memory for narrower ranges and selected primary, merged, or separate subaccounts. Intern accounts continue to use `InternName` identity through `AccountFactDataService`; donor detail/current contact projections remain separate.

Current donor contact values are hydrated through `IDonorContactHydrationService` before both `DonationRowDto` and `DonorSummaryDto` projection. The hydrator uses the generation-aware `IDonorContactCache`, batch-loads cache misses through `IDonorContactReadRepository`, and caches empty contact results so donor selection and contact display do not require click-time queries. This applies equally to repository-loaded records and records filtered from authoritative donation facts. `DonationReadRepository` therefore selects donation facts and donor identity only; it does not duplicate the current contact projection with correlated `DonorContacts` subqueries.

The donation page keeps donation rows focused on transaction data and identifies donors by authoritative `DonorId`. `DonationDataDto.DonorContacts` contains one current contact projection per donor ID. `Donations.razor` uses that map for local donor dialogs and never resolves a donor by display name when an ID is available. The duplicated contact fields have been removed from `DonationRowDto` and `DonationSnapshot`; raw `DonationRecord` contact fields remain as mutable hydration targets and for current-contact summary/export compatibility.

`DonationDataDto` records the donations generation and donor-contact generation used to build its rows and contact projection. It is a request projection, not a persistent cache entry. Covered normal-account requests reuse authoritative donation facts, while unsupported scopes use repository fallbacks. Broad cache windows remain eligible for narrower requests.

On a direct Home load, the dashboard requests one broad donation window covering all displayed periods. The account-total repository query returns the primary fund and merged subaccounts in one database query; Home derives the month, year, and visualization totals from that data in memory. This avoids one query per period or per fund without changing the separate-subaccount rule.

The Home Custom Date Range card is lazy: it does not load custom summary data during initial page load or account selection. When the user selects Apply, the dashboard reuses containing authoritative cache entries and derives the summary in memory, supplementing uncovered dates from the repository.

`AccountSnapshotWarmupService` warms the authoritative donation and accounting fact caches before derived-cache warming. A hit in one cache does not imply a hit in the other; intern accounts intentionally warm donations only.
Warmup requests for the same account are single-flight across initial loading and account selection: an active warmup is joined before an existing snapshot is used to start derived-cache warming. This prevents concurrent Home requests from duplicating derived cache loads.

`FinancialSummaryDto` values are stored in the bounded `HomeSummaryCache` as four standard summaries per account and calendar period. Normal accounts use account/fund identity with an empty intern key; intern summaries use `DonationData.InternName` exclusively, with donation-only calculations and no Fund or accounting association. Home complete-cache reads use the same identity split as central warmup, so opening the embedded Account Summaries dialog reuses warmed entries instead of creating a per-account fallback entry. `CacheRebuildService` is the single application-level rebuild owner invoked by startup and database invalidation through `AdminCacheWarmupCoordinator`; it coordinates Admin preload/Overview, normal summaries, and intern summaries. `AccountSnapshotWarmupService` does not load intern summaries. Donation-only invalidation advances the donations generation and removes stale donation facts, donor summaries, missing-gift results, balances, and summary entries. Accounting-only invalidation advances the accounting generation and removes stale accounting facts, balances, and affected normal summaries.

`CacheRebuildService` loads metadata, normal-account donations, InternName-keyed donations, and accounting rows in bulk over the broad rebuild window. It stores those rows in one `CacheRebuildData` context and passes that context to the normal summary, intern summary, and Admin Overview builders. These builders calculate their DTOs in memory and populate the existing caches, avoiding repeated per-account and per-intern database queries. The Admin Overview writer and reader share the UTC trailing-year range, preventing a dialog-open cache-key miss. Intern bulk loading is keyed only by `DonationData.InternName`; Fund is not used for intern selection or association.
On Home initialization, the warmup coordinator loads the default account plus up to four non-default accounts. Selecting an account while this preload is running prioritizes that account but does not cancel the remaining automatic preload.

The cache data-version monitor runs in the host as a background coordinator, but it reads source-data markers through `ICacheDataVersionProvider`. The MySQL implementation, including table-marker SQL, is owned by Infrastructure so database mechanics do not leak into the Blazor host.

Cache invalidation maintains independent generations for donations, accounting, account metadata, donor/contact projections, and authorization while retaining the aggregate generation for compatibility. Donation and accounting cache keys use their respective generations, allowing independent invalidation without rebuilding unrelated fact data.

### 2. Derived caches

Donation and expense DTOs are not cached. They are projected per request from authoritative fact caches plus user-scoped overflow facts needed outside the broad cache window.
`DonorSummaryCache` and `MissingGiftCache` are bounded, process-wide, generation-aware caches for reusable derived results.

The current registrations are:

| Cache | Lifetime | Typical key/data |
|---|---|---|
| Donor summaries | Singleton | Scope, date range, source generations, summaries |
| Missing-gift warnings | Singleton | Account ID, fund, date range, source generations, warning donors |

Both derived caches expose `InvalidateAll()` and are cleared by `ImportCacheInvalidator`.

## Cache decision matrix

The following matrix records the current implementation contract. A cache's registration lifetime is not, by itself, an authorization boundary; account authorization must be performed by the application service before shared account data is returned.

| Cache | Contents | Scope | Shareable? | Key dimensions | TTL / capacity | Eviction | Invalidation | Authorization check |
|---|---|---|---|---|---|---|---|---|
| Donation/donor facts | Compact donation identity, display, fund, and subaccount facts | Process-wide singleton | Yes, after authorization | Account ID, normalized fund, donations generation, broad window | No TTL; maximum 64 entries and approximately 64 MB | LRU | Donation/metadata generation pruning; rollback and unknown changes clear all entries | Services authorize before shared-cache access |
| Accounting facts | Compact accounting transaction facts | Process-wide singleton | Yes, after authorization | Account ID, accounting identity, accounting generation, broad window | No TTL; maximum 64 entries and approximately 64 MB | LRU | Accounting/metadata generation pruning; rollback and unknown changes clear all entries | Services authorize before shared-cache access |
| Home summary DTO | Four standard Home financial summary DTOs for one normal account or intern identity and calendar period | Process-wide singleton | Yes, after authorization | Normal: account ID and normalized fund; intern: InternName; applicable Donations/Accounting/AccountMetadata generation, year, month | No TTL; maximum 256 entries | LRU | Replaced/pruned on financial or account-metadata generation changes; startup and database-update summary rebuild | Dashboard service revalidates current user/account access before projection |
| Dashboard calculation projection | Snapshot rows mapped for dashboard calculations | Request-local | No | Account fund, requested window | No retention | Request lifetime | None; source snapshot generation controls freshness | Dashboard service revalidates context/account access |
| Donation DTO | Presentation donation data | Request-local | No | Request scope and date range | No retention | Request lifetime | None; source snapshot/repository data is reloaded as required | Donation service authorizes before reads |
| Expense DTO | Presentation expense/accounting data | Request-local | No | Request scope and date range | No retention | Request lifetime | None; source snapshot/repository data is reloaded as required | Expense service authorizes before reads |
| Donor summaries | Derived donor summary rows | Process-wide singleton | Shareable after authorization | Scope, start date, end date, donation/contact/authorization generations | No TTL; maximum 256 entries | LRU | Central invalidator and generation mismatch | Donor service authorizes before cache access |
| Missing-gift warnings | Filtered missing-gift donor summaries | Process-wide singleton | Shareable after authorization | Account ID, fund, start date, end date, donation/contact/authorization generations | No TTL; maximum 256 entries | LRU | Central invalidator and generation mismatch | Donor service authorizes before cache access |
| Account balance | Cumulative balance through an as-of date | Process-wide singleton | Yes, after authorization | Account ID, accounting class, account number, balance adjustment, generation, as-of date | No TTL; maximum 256 entries | LRU | Domain-specific changes prune stale generations; rollback and unknown changes clear all entries | Home/Admin authorization occurs before balance access |
| Account metadata | Account and subaccount metadata used by admin preload | Process-wide singleton | Yes for administrative reads | Account ID, account-metadata generation | No TTL; currently populated for all accounts during admin preload | Explicit invalidation | Central invalidator and generation mismatch | Must not replace user account-membership authorization |
| Fund references / selector access | Full account reference metadata plus assigned user IDs | Process-wide singleton | Selector filtering only | Account metadata and authorization generation | No TTL; one generation-wide snapshot | Generation replacement; central invalidator | Account metadata or authorization changes; full invalidation | Cached access filters the selector only; selection and financial data services revalidate current access |
| User account context | Account metadata and accessible account list | Process-wide static cache partitioned by user | No direct cross-user sharing | User identity, account-metadata and authorization generations, current role/viewer state | No TTL; maximum 256 entries | LRU | Account metadata/authorization generation changes and explicit per-user removal | User is re-resolved from the database and role/access state is compared before a cached context is returned |
| Selected account | UI selection only | Process-wide memory cache partitioned by user | No financial data sharing | User ID | 24-hour absolute expiration | Memory-cache expiration | Explicit user removal; process restart | Selection is only a preference; selected account is resolved against current accessible accounts |
| Selected date range | UI date-range preference only | Process-wide memory cache partitioned by user | No financial data sharing | User ID | 24-hour absolute expiration | Memory-cache expiration | Explicit user removal; process restart | Not authorization-sensitive |
| Import preview | Temporary uploaded preview and ownership metadata | Process-wide singleton | Never shareable across users | Preview ID, authenticated owner, import type | Maximum 15-minute retention with bounded preview capacity | Expiration/capacity cleanup | Removal after import, failure, abandonment, or expiration | Owner and current administrator authorization are required |
| Import progress | Temporary import status and progress | Process-wide host service | Never shareable across users | Progress ID, authenticated owner | Maximum 15-minute retention for terminal/abandoned records | Expiration/cleanup | Completion, failure, abandonment, or expiration | Owner and current administrator authorization are required |

### Current cache gaps and follow-up requirements

The matrix documents current behavior; it does not imply that every cache already meets the target architecture. The following remain implementation follow-up items:

- Add separate configurable freshness and capacity settings rather than relying only on hard-coded limits.
- Add cache counters for hits, misses, evictions, expirations, invalidations, and authorization rejections without logging donor values.
- Add expiration-focused tests for caches that currently have no TTL.
- Add integration coverage for authorization changes across shared cache entries.

`AccountMetadataCache` is intentionally an administrative preload cache. It does not replace the authorized account-membership query used to build `UserAccountContext`; cached metadata must never be treated as proof that a user may access an account.

Admin overview balance totals are published to the shared account-balance cache as numeric cumulative values; Home can reuse exact as-of balances without caching the transaction result lists.

These changes must preserve central invalidation and broad snapshot reuse for narrower contained date ranges.

`DonorSummaryCache` is the cache-aware boundary for the Donors page. Donor summaries are keyed by normalized scope, requested date range, and donation/contact/authorization generations. `DonorService` performs a cache lookup before entering the per-request single-flight query lock, then checks again after acquiring the lock so concurrent requests do not duplicate repository work.

`MissingGiftCache` stores the already-filtered Home warning result, keyed by account identity, fund, requested date range, and source generations. Central `CacheRebuildService` populates it for every normal account from the shared `CacheRebuildData` donation snapshot using the Home six-month warning range, bounded concurrency, donor-contact hydration, and the same donor-summary/missing-gift rules used by on-demand requests. `AccountSnapshotWarmupService` remains a fallback for account-specific warmup paths, while account selection can normally reuse the centrally prepared result without repeating the donor-summary/database path. It is invalidated by `ImportCacheInvalidator` together with the other shared caches.

Dashboard diagnostics identify authoritative fact-cache hits, misses, and loads. Dashboard service logs also identify whether a request used the complete cache-backed path or the summary-direct path.

The Donations page obtains its request projection from `DonationService`. `DonationService` uses `IAccountFactDataService` as its single authoritative donation-fact boundary, combining shared broad-window facts with user-scoped overflow coverage and filtering the returned facts in memory for the selected subaccount. It does not maintain a presentation DTO cache or duplicate snapshot/repository orchestration.

The normal request method still performs the generation-aware cache lookup and routes misses through the donor-summary path, allowing the shared donation fact window and user-scoped overflow coverage to serve narrower reporting ranges. Missing-gift freshness is determined while summaries are built from the loaded dataset; Home does not independently inspect donation data or apply a second freshness rule.

The Donors UI prepares the cached summary rows once after loading by populating `DisplayName`, `DisplayAddress`, and `FrequencyLabel`. The grid uses property-only columns for these values rather than repeating formatting logic in cell templates.

### 3. Dashboard projections

`FinancialDashboardService` uses `IAccountFactDataService` and the authoritative donation/accounting caches, then maps the resulting facts into `DashboardAccountCacheData` for the duration of the calculation. The projection is not retained as a separate scoped cache. The former combined account snapshot cache and loader have been removed; dashboard calculations no longer depend on that duplicate cache path.

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

The process-wide context cache is bounded to 256 entries with LRU eviction and has no time-based expiration. It uses only account-metadata and authorization generations because donation and accounting changes do not alter the accessible account list. The service still re-resolves the user and compares the current role/viewer state before returning a cached context; the cache is not an authorization boundary.

### 4a. Fund reference and selector-access cache

**Primary types:** `FundReferenceDto`, `IFundReferenceCache`, and `FundReferenceService`.

The fund-reference cache stores the complete account selector metadata together with the user IDs assigned to each account. It is populated through bulk account, bulk access, and current `DonationData.InternName` reads, then reused by `UserAccountContextService` so normal account-selector construction does not issue per-user account-list queries. Admin preload also warms this cache.

The assigned user IDs are only a selector-filtering hint. `AccountSelectionCoordinator` revalidates the selected account against current authorization before warming or loading account data, and the financial services continue to authorize before returning data. Account metadata and authorization mutations advance the relevant generations and invalidate the reference cache.

### 5. Admin preload caches

**Primary types:**

- `IAdminPreloadService` / `AdminPreloadService`
- `IAdminRecentAccountSnapshotService` / `AdminRecentAccountSnapshotService`

These are scoped Admin-specific caches and are intentionally separate from the regular authoritative fact-cache path.

`AdminPreloadService` supports bounded popup data such as:

- Account lists.
- Subaccounts.
- Staff data.
- Accounts Overview aggregates.
- Fund References data.

Accounts Overview displays the account rows before financial aggregates complete. Its aggregate load uses one batched accounting query and one batched non-intern donation query, then applies the existing account and subaccount calculation rules in memory. Intern-designated accounts retain their specialized donation query path.

For non-admin users, the initial account warmup can warm the configured default account plus a bounded set of recent non-default accounts. For Admin users, the initial warmup stops after the configured default account; it does not automatically warm additional accounts. Explicit account selection may still warm the selected account on demand.

Admin preload data must not be used as a reason to warm every account at startup.
The Admin page prioritizes account, staff, and subaccount data needed by its controls, renders those collections, and only then starts default-account and admin preload warmups. Database latency and the overview's per-account calculations must not block the first interactive response. Overview warmup is bounded to two accounts concurrently so it does not monopolize the shared database connection pool. Preload timing and item counts are logged to distinguish cold-load latency from later cache reuse.

### Database availability recovery

Central cache creation is queued after application startup when the initial database probe succeeds. If the probe fails, the application remains in limited mode and the availability monitor continues checking the database. When the monitor later reports a connected state, the application enables MySQL loading and queues the same central warmup. The warmup coordinator rejects overlapping requests, so a recovery transition cannot create concurrent rebuilds.

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
5. Clears all authoritative fact-cache entries.
6. Advances the global cache generation.

The donor-contact cache is also cleared during central invalidation. The next donor or donation projection therefore reloads current contact values rather than retaining contact fields embedded in older derived DTOs.

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

### Mutation-to-cache dependency map

| Mutation | Invalidation domain | Central rebuild behavior |
|---|---|---|
| `DonationData` import or rollback | `Donations` | Prunes donation, donor, missing-gift, balance, and Home summary generations, then rebuilds Admin Overview and Home summaries from one bulk snapshot. |
| Accounting import or rollback | `Accounting` | Prunes accounting, balance, and Home summary generations, then rebuilds Admin Overview and Home summaries from one bulk snapshot. |
| Account or subaccount create/update/delete, including `BalanceAdjustment` | `AccountMetadata` | Invalidates account metadata and Admin shared caches, prunes donation/accounting facts plus balance and Home summaries, then rebuilds metadata, Admin Overview, and Home summaries. |
| User profile, account-access, create, or delete | `Authorization` | Advances authorization/context generations and invalidates selector-reference data; it does not rebuild financial facts because user changes do not change donations or accounting. |

The application write services issue these invalidations after successful database writes. UI components may refresh their local view state afterward, but cache correctness does not depend on a particular Blazor page calling the invalidator. Authorization changes must still be revalidated at financial-data access boundaries.

Invalidate only after the database operation succeeds. Do not invalidate for a failed operation unless the operation may have partially changed the database and the failure-handling design requires it.

## Lifetime, restart, and limited-mode behavior

All caches documented here are in-process memory caches. They are lost on application restart, deployment, process recycle, or host replacement; no cache contents are persisted in browser storage or the database. A restart therefore repopulates data through the normal authorization and database paths.

When the database is unavailable, the startup probe and availability monitor place the application in limited mode. Cache services must not turn a database outage into an application-startup failure or unbounded retry/cache growth. Existing cached values may be used only where the surrounding service permits it; new database-dependent loads remain subject to the database guard.

Cache logs may record non-personal counts, ranges, generations, durations, and cache outcomes. They must not record donor names, the `DonationData.Fund` value, raw donor/accounting rows, uploaded workbook values, or credentials.

## Date-range and snapshot rules

The shared snapshot represents a broad supported date range. A narrower request may use that snapshot when the requested range is fully contained within the snapshot range.

The UI does not need to select a special canonical date-range label. The decision is based on date containment.

Donation date ranges are normalized to start at midnight on the selected start date and end at the final tick of the selected end date before repository queries or authoritative fact loading. This preserves the UI's inclusive date semantics for `DATETIME` donation rows while retaining broad-window cache reuse.

Correct behavior:

- Load the broad snapshot once.
- Filter its immutable values in memory for narrower contained ranges.
- Reuse the snapshot for repeated requests for the same account.
- Preserve repository/session-cache fallbacks for ranges outside snapshot coverage or unsupported request shapes.

Do not require the selected UI range to equal a special canonical range before using a snapshot.

## Historical balance anchors

Home summary balances use the existing inclusive, end-of-day balance convention. When a generation-matched Home summary is cached, the prior-year summary starting balance is used as the earliest end-of-day anchor (the day before the prior-year period begins). The anchor balance is then moved forward or backward using the existing accounting classification and signed-transaction rules; `BalanceAdjustment` is not applied a second time. If no cached anchor exists, the existing forward calculation path remains the fallback.

## Page usage guidance

### Donations

Use the account snapshot for the supported selected-account range after authorization. Derive the requested date range from the immutable snapshot where possible. Preserve existing fallbacks for unsupported ranges, explicit subaccounts, searches, donor-specific queries, and other paths not represented by the snapshot.

### Expenses

Use `IAccountFactDataService.GetAccountingDataAsync` for aggregate, expense-only, transfer-only, and summary operations. It reuses the shared accounting cache for the standard window, filters broad facts in memory, and stores only uncovered custom-range segments in the user-scoped accounting overflow cache. `ExpenseService` then applies authorization and projects the categorized DTOs per request.

### Donors

Use `IAccountFactDataService.GetDonationDataAsync` for the normal account-range path. It combines shared broad-window donation facts with user-scoped overflow coverage, supplies subaccount metadata, and returns date-filtered, deduplicated facts. `DonorService` derives `All`, `Primary`, and valid explicit subaccount selections by filtering those facts in memory, hydrates current contacts, and caches the resulting donor summaries by funds signature and requested date range. Preserve repository-backed paths for `All Dates`, donor detail, search, and other unsupported request shapes.

The Donors page is cache-aware at the presentation boundary as well as in the application services:

- The initial page state does not force a loading boundary before the asynchronous initialization completes. This prevents a loading bar from being shown merely because a new page component instance was created.
- After the first result, the page retains the grid while account or date-range data is refreshed. The loading boundary is only active when `isLoading` is true and no donor result has yet been displayed.
- Cached donor summaries are still loaded through `IDonorService`; the page does not access the cache implementation directly.
- A cache hit avoids repository work, but the page still performs the normal authorization, account-context, subaccount, and component initialization lifecycle.

This distinction is important: cache hits reduce data access latency, while retaining the rendered grid prevents a cached refresh from replacing the grid with a loading indicator.

### Home and Account Summaries

Home and Account Summaries use direct dashboard projections backed by authoritative donation/accounting facts and derived summary caches. Changes affecting account metadata or dashboard data must use the central invalidator. Clearing only Admin preload data or only one fact cache is insufficient.

Home starts account warmup after its first render, once the initial Home load has completed. The default account is prioritized first; when that warmup completes, background warmup for the remaining bounded set of recent non-default accounts starts immediately. Warmup must not block the initial Home render, and it must not be implemented with a fixed timer-based delay.

When a default account exists, warmup includes the default account plus up to four recent non-default accounts. When no default account exists, it includes up to five accounts. The selected warning date range is passed through the complete derived-cache warmup for every account, including the default account; the warning cache is therefore ready when Home computes the alert. Selecting another account awaits that account's warmup, then the coordinator resumes the remaining background work afterward.

### Admin

Admin selection should be persisted through `IUserSelectionService` when the existing application behavior requires the selection to follow the user to Donations, Expenses, Donors, or Home. Scoped component state alone is not sufficient for cross-circuit propagation.

Admin popup data should use the bounded Admin preload services. Do not preload regular snapshots for every account.

## Rules for adding a new feature

Before adding a new data-loading feature, answer these questions:

1. Is the data account-scoped, user-scoped, date-range-scoped, or global?
2. Is it already present in an authoritative donation or accounting fact cache?
3. Can the feature derive its result by filtering or transforming an existing immutable snapshot?
4. Does the request fit within the authoritative cache date range?
5. Is the request shape supported by the authoritative cache, or does it require a repository fallback?
6. What cache lifetime is correct: singleton, scoped, or no cache?
7. Which account metadata fields affect the result?
8. Which successful mutations make the cached result stale?
9. Does the feature need to participate in `ImportCacheInvalidator`?
10. How will a scoped instance observe global invalidation?

### Preferred implementation pattern

1. Perform authorization before accessing shared cached data.
2. Normalize account and date-range inputs.
3. Reuse `IDonationDataCache` or `IAccountingDataCache` when facts are represented by the corresponding authoritative cache.
4. Use the domain-specific normalized cache key consistently.
5. Derive narrow results from immutable authoritative-cache values when the requested range is contained.
6. Use the existing repository path when snapshot coverage does not apply.
7. Add a cache only when repeated work is demonstrated and the lifetime is safe.
8. Add the cache to centralized invalidation if it can become stale after a mutation.
9. If the cache is scoped, observe `ICacheInvalidationVersion` or use another existing cross-circuit invalidation pattern.
10. Add tests for cache reuse, invalidation, fallback behavior, and authorization.

## Common mistakes to avoid

- Creating a page DTO cache that duplicates the authoritative fact caches.
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

- Authoritative fact-cache miss/load versus cache hit.
- Repository fallback versus fact-cache-derived result.
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

- `IDonationDataCache` / `IAccountingDataCache`: singleton bounded authoritative fact caches.
- `IDonationDataCacheLoader` / `IAccountingDataCacheLoader`: scoped repository loaders.
- `IUserAccountContextService`: scoped service with process-wide context storage.
- `ICacheInvalidationVersion`: singleton.
- `IDonorSummaryCache`: singleton, bounded and generation-aware.
- `IMissingGiftCache`: singleton, bounded and generation-aware.
- `IAccountMetadataCache`: singleton, generation-aware metadata populated by admin preload.
- `IDonorContactCache`: singleton, generation-aware current-contact projection cache.
- `IAccountBalanceCache`: singleton, bounded as-of balance cache.
- `IImportCacheInvalidator`: singleton.

Review this registration map whenever introducing a new cache or changing cache ownership.
