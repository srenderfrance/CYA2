# Cache System Redesign Implementation Plan

## Understanding
Redesign the application's caching system as a coordinated, authorization-safe architecture covering raw account data, donor/contact projections, derived DTOs, authorization context, UI state, import previews/progress, invalidation, expiration, capacity, and observability. Preserve current behavior, especially broad account snapshot reuse for narrower date ranges, database limited mode, and invalidation after imports and rollbacks.

## Goals
- Establish clear ownership for every cache.
- Establish canonical database read ownership so caches do not conceal duplicate query producers.
- Separate raw data from derived/presentation data.
- Keep donation dates in raw account snapshots so donor membership can be derived for any selected date range without duplicating raw donor data.
- Add a separate current-state donor projection cache for `Donors` and `DonorContacts` where appropriate.
- Enforce authorization before returning shared account data.
- Give every memory-resident cache an explicit TTL, capacity, eviction, and invalidation policy.
- Preserve database-unavailable and limited-mode behavior.
- Prevent donor or accounting data from crossing account-authorization boundaries.
- Keep each raw database fact in one canonical in-memory cache layer.

## Target cache layers

### Shared raw data
- `AccountSnapshotCache`
  - Broad account-scoped donation rows, including `DonorId` and donation dates.
  - Accounting rows.
  - Subaccount metadata.
  - Account identity, coverage range, and data generation.
  - Shared broad snapshots may satisfy narrower requested date ranges.
- `DonorProjectionCache`
  - Current `Donors` identity projection keyed by stable donor identity where authorization permits.
  - Current `DonorContacts` values keyed by donor identity.
  - A separate normalized `Fund + IdentityKey` lookup may resolve donor IDs without duplicating projections.
  - No date dimension because current donor/contact state is not historical.

### Derived data
- Dashboard DTOs and account balances.
- Donor summaries and missing-gift results keyed by requested date range.
- Expense/accounting summaries.
- Donation presentation DTOs.
- Donor/contact DTOs built by joining date-filtered donation membership with current donor projections.

### User and UI state
- User account authorization/context cache.
- Authorization-result cache with a short revocation-safe TTL.
- Selected account and selected date-range state, kept per user/circuit and never shared as financial data.
- Import preview and progress records, scoped to the owning user/import operation.

## Architecture principles
- Canonical loaders own database reads; derived services consume canonical raw models instead of issuing equivalent queries directly.
- Cache keys are partitioning and reuse mechanisms, not authorization.
- Shared account-data reads must validate the current user through centralized authorization services before returning cached values.
- Raw data caches must not contain user-specific UI state.
- Derived caches must include every dimension that changes their result, including account/fund, date coverage, generation, and relevant options.
- Current donor/contact values must not be presented as historical values for old donation dates; historical contact reporting requires a separate history model.
- Source-data generation invalidation takes precedence over TTL.
- Database-unavailable behavior must return safe empty/limited results and must not crash startup or normal rendering.
- No donor names or personal contact data may be written to diagnostics or cache-key logs.
- Shared raw caches use single-flight loading; one caller's cancellation must not cancel the shared load for other callers.
- Failed, canceled, or database-unavailable loads are never stored as successful cache entries.
- An invalidated in-flight load cannot repopulate a cache with stale data.
- Cache keys use canonical normalization and deterministic ordering; equivalent fund sets produce equivalent keys.
- Source generations are domain-specific: donations, donors/contacts, accounting, account metadata, and authorization/access may advance independently.
- Mutation invalidation occurs only after the database transaction commits; generation advancement and cache invalidation follow the successful commit.

## Data ownership invariants
- `AccountSnapshot` contains raw account-scoped donation rows, accounting rows, and subaccount metadata, but not user-specific UI state or duplicated donor/contact projections.
- Donation membership is determined from donation dates in the raw snapshot.
- Current donor identity and contact values are joined only when constructing derived results.
- Current contact values are never represented as historical contact values.
- A denied request cannot populate or retrieve a shared account-data cache entry.
- Cache generations include only the source domains relevant to the cached contents.
- Cache diagnostics contain metadata only: category, outcome, normalized-key hash, coverage, generation, row/byte counts, and timing.

## Cache contract matrix

| Cache | Contents | Scope | Key dimensions | TTL | Capacity/eviction | Invalidation | Authorization |
|---|---|---|---|---|---|---|---|
| Account snapshot | Donations, accounting, subaccounts | Process-wide shared | Account, fund, coverage, generation | Explicit absolute TTL | Entry and approximate-byte bound, LRU | Import, rollback, source generation, account metadata | Validate before read |
| Donor projection | Current donor/contact projection | Process-wide shared | Account/funds, generation | Shorter than or equal to donor freshness requirement | Entry/byte bound, LRU | Donor/contact reconciliation, donation import, rollback, generation | Validate before read |
| Dashboard DTO | Derived dashboard/balance | Shared or scoped per current policy | Account, containing range, options, generation | Explicit absolute TTL | Bounded LRU | Financial invalidation | Validate before read |
| Donation DTO | Presentation donation results | Scoped/user partition as required | Account/fund, range, filters, generation | Explicit absolute TTL | Bounded | Donation/contact invalidation | Validate before read |
| Expense/accounting DTO | Derived expense data | Scoped/user partition as required | Account, range, options, generation | Explicit absolute TTL | Bounded | Accounting invalidation | Validate before read |
| Donor summaries | Derived donor summaries | Shared only after authorization filtering | Funds/account, requested range, options, generation | Explicit absolute TTL | Bounded | Donation/contact invalidation | Validate before read |
| Missing gifts | Derived warnings | Shared only after authorization filtering | Account/fund, requested range, generation | Explicit absolute TTL | Bounded | Donation/contact invalidation | Validate before read |
| User account context | Authorization-sensitive context | Per user/circuit or generation-aware shared | Authenticated user, access generation | Short TTL | Bounded | Access changes, user changes, logout | User identity resolved server-side |
| Authorization result | Access decision | Per user/account | User, account, access generation | Short revocation-safe TTL | Bounded | Access changes, role changes | Server-side source of truth |
| Selected account | UI selection | Per user/circuit | User/circuit | Session lifetime | Naturally bounded | Selection change/logout | Never used as authorization |
| Selected date range | UI selection | Per user/circuit | User/circuit | Session lifetime | Naturally bounded | Selection change/logout | Never used as authorization |
| Import preview | Uploaded preview data | Per user/import | Owner, import type, preview ID | Short absolute TTL | Entry and byte bound | Completion, cancellation, owner removal | Owner/admin only |
| Import progress | Progress/status | Per user/import | Owner, operation ID | Short absolute TTL | Entry bound | Completion, cancellation, expiration | Owner/admin only |

## Assumptions
- Existing broad `AccountSnapshotCache` behavior remains the canonical account-data reuse path during migration.
- Donor membership is determined from donation dates in the broad snapshot; current contact fields come from `Donors`/`DonorContacts`.
- Existing derived cache APIs may be adapted incrementally rather than replaced all at once.
- Authorization changes must take effect within the configured authorization-cache revocation window, with mutation invalidation taking effect immediately where possible.
- Data generations remain the authoritative protection against stale source data.

## Risks and decisions to resolve
- Canonical query ownership must be established before adding more cache layers; otherwise duplicate repository reads may remain hidden behind cache hits and misses.
- The current donation projection embeds current contact values in donation rows; migration must define whether those fields remain fallback-only or are removed from donor summary construction.
- Some fund-based donor queries do not have a single account ID; they require a deterministic normalized funds-based lookup key or an explicit non-snapshot fallback.
- Donor/contact tables are current-state projections, so date-range filtering determines donor membership, not historical contact values.
- Cache capacity and TTL defaults must be based on measured workload and memory behavior rather than arbitrary global values.
- Existing singleton caches containing user IDs require review to ensure authorization and privacy boundaries are explicit.
- DonorId-based projection reuse requires confirmation that donor IDs are stable and that authorization is checked before returning any projection.

## Implementation steps
1. Inventory every current cache, its registration lifetime, key, contents, invalidation, and callers.
2. Identify canonical database read owners and duplicate read producers.
3. Map each raw, derived, authorization, UI-state, preview, and progress cache to the target matrix.
4. Define source-generation domains and mutation-to-generation mappings.
5. Define shared cache options for category-specific TTLs, entry limits, and approximate-byte limits.
6. Define common single-flight, expiration, eviction, failure, and cache-outcome instrumentation contracts.
7. Define canonical cache keys for account snapshots, fund sets, donor identities, and authorization context.
8. Define raw account snapshot coverage and ensure broad snapshots satisfy contained narrower date ranges.
9. Define donor projection models and repository queries for `Donors` and `DonorContacts`.
10. Implement the bounded generation-aware donor projection cache with single-flight loading.
11. Refactor donor summary construction to filter donation membership by requested date range and join donor projections.
12. Refactor remaining raw and derived caches to use explicit category policies without changing behavior.
13. Centralize authorization checks for shared account-data cache reads.
14. Separate user/circuit UI state from shared financial caches.
15. Extend transactional import, rollback, account, donor, contact, role, and access invalidation paths.
16. Preserve database startup probing, limited mode, and safe empty/error behavior.
17. Add tests for cache policy, broad/narrow range reuse, donor date filtering, authorization revocation, invalidation, expiration, capacity, single-flight, and database unavailable mode.
18. Update `src/Cya2.Web/docs/CACHE_ARCHITECTURE.md` and related implementation checklists.
19. Build the solution, run application/web tests, and perform targeted cache behavior validation.

## Phase 1 inventory deliverables
- A cache inventory identifying implementation, lifetime, key, contents, callers, policy, and invalidation behavior.
- A canonical read-path map identifying the owner for each raw data set and all duplicate query producers.
- A generation and invalidation map covering imports, rollbacks, account metadata, donor/contact reconciliation, and authorization changes.
- A list of unresolved decisions and runtime measurements required before selecting TTL, capacity, and byte-limit defaults.

## Phase 1 findings

### Cache inventory
- `AccountSnapshotCache` is the strongest existing shared raw-cache implementation: singleton, normalized key, LRU, 64-entry limit, 64 MiB approximate-byte limit, single-flight loading, and invalidation-token protection. It currently has no explicit TTL.
- `AccountBalanceCache` is a singleton derived cache with a global generation gate and 256-entry LRU limit. It has no TTL, byte bound, single-flight loading, or load instrumentation.
- `AdminSharedCache` is a singleton metadata/derived cache with one global generation and a four-entry overview limit. Its account, subaccount, and staff values share the generation but have no TTL or single-flight loading.
- `DashboardSessionCacheService` is scoped but stores raw accounting, donation, and subaccount data and performs its own repository reads. It keeps a default and one recent non-default fund, uses broad-range containment, and has no explicit TTL or single-flight loading.
- `SessionDonationDataCacheService` and `SessionDashboardDtoCacheService` are singleton user-partitioned DTO caches with per-user LRU fund limits of 30 and 15 respectively. They have no TTL, byte limit, generation in their keys, or single-flight loading.
- `SessionExpenseDataCacheService`, `SessionDonorSummaryCacheService`, and `SessionMissingGiftCacheService` are singleton DTO caches with composite string keys and no TTL, capacity policy, byte bound, generation key, or single-flight loading. `DonorService` adds a separate per-query semaphore for some donor-summary reads.
- `MemoryUserSelectionService` and `MemoryUserDateRangeSelectionService` use the shared `IMemoryCache` with a 24-hour absolute TTL and user-based keys. They are UI state, not financial data.
- `ImportPreviewStore` is a singleton owner-scoped store with configured entry and byte limits plus explicit expiration removal, but expiration depends on callers invoking `RemoveExpired`.
- `SessionUserStateService` and `DateRangeStateService` are scoped UI state services and must remain outside shared financial caches.

### Canonical and duplicate read paths
- `AccountSnapshotLoader` is the intended canonical raw account loader for accounting, donations, and subaccounts.
- `DashboardSessionCacheService` independently loads all three raw datasets and duplicates the snapshot loader's repository work.
- `AccountCalculationService`, `ExpenseService`, `DonationService`, and `DonorService` retain direct repository fallbacks or specialized fund/date reads. These paths need explicit classification before consolidation because some are account-scoped and some are funds-based or intern-designation-based.
- `DonationService` and `DonorService` each maintain additional derived/raw caches, so the same donation rows can exist in account snapshots, per-user donation DTOs, and donor-summary DTOs.

### Generation and invalidation findings
- `CacheInvalidationVersion` is one process-wide monotonically increasing generation. It cannot currently express independent donation, donor/contact, accounting, account-metadata, or authorization changes.
- `ImportCacheInvalidator` clears all known session, snapshot, admin, and balance caches and increments the global generation after import/rollback callers complete. It does not yet expose affected domains or transaction metadata.
- `CacheDataVersionMonitorService` also invalidates all caches when a combined DonationData/AccountingData marker changes. The marker does not independently cover donor/contact or account metadata changes.
- `AccountImportService`, donation/accounting import processors, and rollback orchestration are invalidation producers. Their ordering and failure/cancellation behavior must be documented before domain-specific invalidation is introduced.

## Phase 4 progress: account balance read consolidation

- `AccountCalculationService.CalculateBalanceAsync` now reuses the generation-aware account snapshot for bounded normal account ranges.
- `AccountCalculationService.CalculateBalancesAsync` now loads bounded accounts through the shared snapshot cache, preserving single-flight reuse per account.
- Snapshot accounting rows are filtered to the requested date range before the existing `AccountingTransactionProcessor` runs, preserving balance classification behavior.
- Unrestricted and out-of-coverage balance requests retain the direct repository path.
- Regression coverage verifies single-account snapshot reuse, multi-account snapshot loading, and repository fallback; the application suite passes 104 tests.
- Database limited mode is preserved through `DatabaseGuardAdapter` and `DatabaseMonitorService`; repository calls throw when MySQL operations are unavailable. Cache redesign must not convert those failures into successful empty cache entries.

## Phase 5 progress: donation and donor read-path audit

- `DonationService` already uses `AccountSnapshotCache` for authorized selected-account requests within the canonical snapshot range, including subaccount metadata and narrower date filtering.
- Account-specific `DonorService.GetDonorSummariesForAccountAsync` already reuses the shared snapshot for covered date ranges and derives donor summaries from filtered snapshot donations.
- Generic fund-set searches, all-history queries, intern-designation reads, donor detail/current contact projections, and selected separate-subaccount queries remain explicit repository paths because they do not share the normal account snapshot semantics.
- No additional safe migration was identified in this audit; replacing these specialized reads would risk changing scope, authorization, or current-state projection behavior.

## Phase 6 progress: items 1–2, domain generations and cache keys

- Added independent generations for donations, accounting, account metadata, donor/contact projections, and authorization while retaining the aggregate `Current` generation for compatibility.
- Account snapshot producers now use a composite donations/accounting/account-metadata generation instead of hard-coded generation `0` values.
- Broad snapshot coverage and narrower date-range reuse remain unchanged; generation changes only partition source freshness.
- Added regression coverage for independent domain advancement and composite-generation changes.
- TTL, capacity, eviction, and additional policy work remain deferred to Phase 6 item 3.

### Priority decisions for Phase 2
1. Make `AccountSnapshotLoader` the only canonical owner of broad account raw reads and migrate `DashboardSessionCacheService` to consume it.
2. Decide whether account snapshots contain accounting rows permanently or whether accounting requires a separate canonical raw snapshot; do not retain both copies unintentionally.
3. Replace the global generation with domain-specific source generations while retaining a compatibility aggregate during migration.
4. Define a canonical normalized fund-set key for fund-based donor reads and identify which fund queries cannot use account snapshots.
5. Add explicit TTL and bounded-capacity policies only after measuring current cache sizes, hit rates, concurrent loads, and repository query counts.
6. Add single-flight behavior to shared raw loading before adding more derived cache layers.
7. Review logging because current cache logs include user IDs and fund values; future cache diagnostics should use metadata or normalized-key hashes where privacy requires it.

## Validation criteria
- No shared cache returns account data before authorization is revalidated.
- A broad account snapshot serves contained narrower date ranges without a new raw-data query.
- Donor membership changes correctly with the selected donation date range.
- Current Donors/DonorContacts values are not duplicated per date range.
- Donor/contact changes invalidate donor projections before their TTL expires.
- Access removal prevents subsequent reads within the required revocation window.
- Every cache has explicit TTL, capacity, eviction, and invalidation documentation.
- Startup and rendering remain safe when the database is unavailable.
- Diagnostics contain cache metadata only and never donor names, contact values, or raw financial data.
