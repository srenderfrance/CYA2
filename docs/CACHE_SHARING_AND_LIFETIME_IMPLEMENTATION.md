# Cache Sharing and Lifetime Implementation TODO

This checklist defines the work required to determine cache scope, apply appropriate lifetimes, and ensure cached account data is only available to users who are authorized to view the corresponding account.

The implementation must preserve the existing broad shared-snapshot reuse behavior for narrower date ranges, preserve cache invalidation after imports and rollbacks, and avoid exposing donor or accounting data across account-authorization boundaries.

## Goals

- Determine which caches are safe to share across users.
- Determine which caches must remain user- or circuit-specific.
- Assign an explicit lifetime or eviction policy to every cache.
- Ensure cached account data is returned only to users who can currently view that account.
- Provide a reusable service that identifies users authorized to view an account.
- Reuse that authorization service in `Admin.razor` for administrator visibility.
- Keep cache invalidation correct when source data changes.
- Preserve limited-mode behavior when the database is unavailable.

## 1. Determine cache sharing and lifetime requirements

- [x] Inventory every application, infrastructure, Web, session, and UI-state cache.
- [x] Record each cache's current lifetime, eviction policy, registration lifetime, key structure, and invalidation triggers.
- [x] Identify whether each cache contains:
  - [x] Shared account data.
  - [x] User-specific data.
  - [x] Authorization-sensitive data.
  - [x] UI navigation or selection state.
  - [x] Import previews or progress records.
- [x] Classify each cache as one of:
  - [x] Globally shareable after authorization filtering.
  - [x] Shareable by account and date range.
  - [x] Shareable only by account and current authorization generation.
  - [x] Per-user.
  - [x] Per-Blazor-circuit.
  - [x] Never shareable.
- [x] Determine whether account snapshots may remain shared and use broad coverage for narrower requested date ranges.
- [x] Identify caches whose keys currently include a user ID unnecessarily; the matrix records current user partitioning and the authorization implications.
- [x] Identify caches whose keys do not include enough account or authorization context; current gaps are recorded in the matrix and follow-up section.
- [x] Identify caches that could expose data if an account is removed from a user's access list after the data is cached.
- [x] Define the source of truth for account authorization, including Admin and Viewer behavior.
- [x] Decide whether Admin users can view every account and whether that rule applies to cache reads.
- [x] Document whether authorization changes require immediate cache isolation or may wait for a configured authorization refresh interval.
- [ ] Assign a target TTL or eviction policy to every cache. Current policies and missing policies are documented; implementation remains open for unbounded derived caches.
- [x] Distinguish data freshness TTLs from authorization-cache TTLs.
- [x] Document the current decisions in `src/Cya2.Web/docs/CACHE_ARCHITECTURE.md` and this checklist. Remaining implementation gaps are listed in the architecture document and below.

### Required decision table

For every cache, document:

| Cache | Contents | Scope | Shareable? | Key dimensions | TTL | Eviction | Invalidation | Authorization check |
|---|---|---|---|---|---|---|---|---|
|  |  |  |  |  |  |  |  |  |

### Acceptance criteria

- [x] Every cache has an explicit current scope and lifetime decision; implementation gaps are identified where no TTL or capacity exists.
- [x] No cache is classified as shareable solely because it is currently in process memory.
- [x] Account data caches have an explicit authorization strategy.
- [x] UI-state caches are not confused with donor/accounting data caches.
- [x] The broad-snapshot/narrow-date-range behavior is explicitly preserved.

## 2. Implement authorization-safe cache sharing

- [x] Identify the authoritative existing account-access query/service.
- [x] Avoid duplicating account authorization rules inside individual cache implementations; services revalidate through the existing authorization/context services.
- [ ] Add a shared authorization-generation or access-version concept if required to prevent stale access decisions. Current role/access revalidation is implemented, but a dedicated authorization generation is not.
- [x] Document shared cache keys and their required non-user dimensions, including:
  - [x] Account ID.
  - [x] Account fund or account number where required.
  - [x] Requested date range or snapshot coverage range.
  - [x] Data generation/invalidation version where implemented.
- [x] Do not include raw donor or accounting data in authorization keys.
- [x] Ensure a cache hit is authorized before the cached data is returned to the caller.
- [x] Ensure account data cannot be served from a cache entry created for an unauthorized account.
- [x] Ensure access removal takes effect for subsequent reads without requiring application restart.
- [x] Preserve shared snapshot reuse when multiple authorized users view the same account.
- [x] Preserve broad shared snapshots for narrower user-selected date ranges when the requested range is contained by the snapshot range.
- [x] Ensure cache invalidation after donation imports, accounting imports, rollbacks, and detected database marker changes remains intact.
- [x] Ensure authorization failures do not reveal whether another user's account cache entry exists through application service results.
- [x] Add logging that records cache outcomes and authorization-related state without logging donor data or sensitive values.

## 3. Implement cache TTLs and eviction policies

- [ ] Add named configuration options for cache lifetimes and capacity limits.
- [ ] Do not use one global TTL for unrelated cache categories.
- [ ] Assign explicit TTLs for:
  - [ ] Account snapshots.
  - [ ] Dashboard DTOs.
  - [ ] Donation data.
  - [ ] Expense/accounting data.
  - [ ] Donor summaries.
  - [ ] Missing-gift results.
  - [ ] User account context.
  - [ ] Account authorization results.
  - [ ] Selected account state.
  - [ ] Selected date-range state.
- [ ] Define maximum entries, maximum approximate bytes, or both for each memory-resident cache.
- [ ] Use absolute expiration where stale data must not survive beyond a fixed period.
- [ ] Use sliding expiration only where continued use is explicitly acceptable and does not defeat freshness requirements.
- [ ] Keep authorization-result TTLs shorter than or equal to the required access-revocation window.
- [ ] Ensure data invalidation removes entries before their TTL when source data changes.
- [ ] Ensure cleanup and eviction update any accounting of retained memory.
- [ ] Ensure application restart behavior is documented for all in-memory caches.
- [ ] Ensure a database outage does not cause unbounded cache growth or application startup failure.
- [ ] Add metrics or diagnostic counters for cache hits, misses, evictions, expirations, invalidations, and authorization rejections without exposing donor data.

### TTL acceptance criteria

- [ ] Each cache has a documented TTL or an explicit documented no-TTL policy.
- [ ] Each no-TTL cache has bounded capacity and an eviction policy.
- [ ] Authorization cache entries expire within the approved revocation window.
- [ ] Import and rollback invalidation still takes precedence over TTL.
- [ ] Tests verify expiration rather than relying only on invalidation tests.

## 4. Create the account-access listing service

Create a reusable service that returns the users who currently have permission to view a specific account.

- [x] Identify the existing `AccountsUsers` repository and account-access rules.
- [x] Define the service contract, including:
  - [x] Account ID input.
  - [x] User identity fields returned.
  - [x] Users with blank identity fields are excluded; the current schema has no inactive/deleted columns.
  - [x] Admin and Viewer users are represented as global viewers, in addition to explicit assignments.
  - [x] Users are ordered by name, email, and ID, with duplicate IDs removed.
  - [x] Missing accounts return an empty result.
  - [x] Database-unavailable and service failures return an empty result.
- [x] Implement the service in the appropriate Clean Architecture layer.
- [x] Keep authorization logic centralized so cache code and `Admin.razor` use the same rules.
- [x] Ensure the service does not return passwords, OAuth secrets, donor data, or unnecessary personal information.
- [x] Add an Admin-only application/API access path for `Admin.razor`.
- [x] Return a safe empty/error result without disclosing account information to unauthorized callers.
- [ ] Add tests for:
  - [ ] Users explicitly assigned to an account.
  - [ ] Users without access.
  - [ ] Admin access.
  - [ ] Viewer access, if applicable.
  - [ ] Inactive or deleted users.
  - [ ] Missing accounts.
  - [ ] Database-unavailable behavior.
  - [ ] Stable ordering and duplicate removal.

### Service acceptance criteria

- [x] The service is the single reusable source for account-view authorization.
- [x] `Admin.razor` uses the service rather than implementing its own access query.
- [ ] Cache authorization uses the same service or equivalent centralized policy.
- [x] The service does not expose sensitive user or donor data.

## 5. Implement user-aware cache sharing using account access

- [ ] Define how cache reads obtain the current authenticated user.
- [ ] Confirm that authorization is evaluated server-side and never trusts a user ID supplied by the browser.
- [ ] For each shared account-data cache:
  - [ ] Resolve the requested account.
  - [ ] Check current access through the centralized account-access service.
  - [ ] Read the shared cache only after authorization succeeds, or use an equivalent safe authorization-aware lookup.
  - [ ] Prevent unauthorized users from inferring cache presence through timing or different error behavior where practical.
- [ ] Decide whether authorization is checked on every cache read or through a short-lived authorization cache.
- [ ] Ensure authorization-cache invalidation occurs when account assignments or user roles change.
- [ ] Ensure Admin access is revalidated for sensitive data reads.
- [ ] Ensure a user who loses account access cannot continue receiving cached account data.
- [ ] Ensure a user who gains access can receive current data without waiting for an unrelated data-cache expiration.
- [ ] Ensure shared cache entries do not contain user-specific presentation or authorization data.
- [ ] Keep user-specific UI state separate from shared financial data caches.
- [ ] Add integration tests covering:
  - [ ] Two authorized users sharing the same account snapshot.
  - [ ] Two users with different account access lists.
  - [ ] An unauthorized user requesting another account's data.
  - [ ] Access removed while a shared cache entry still exists.
  - [ ] Access granted while an older cache entry exists.
  - [ ] Admin access and role changes.
  - [ ] Narrower date ranges served from an authorized broader snapshot.
  - [ ] Import invalidation with multiple users and accounts.
  - [ ] Application restart and cache repopulation.

## Documentation and operational review

- [x] Update `src/Cya2.Web/docs/CACHE_ARCHITECTURE.md` with the current cache matrix.
- [x] Document all currently implemented TTLs and capacity limits, and identify caches without explicit TTL/capacity policies.
- [x] Document account authorization and cache-read behavior.
- [x] Document how Admin users view account access through current server-side authorization and account-context services.
- [x] Document when access changes take effect through current-user revalidation, invalidation generation, and service-level checks.
- [x] Document database-unavailable behavior.
- [x] Document application restart behavior for in-memory caches.
- [x] Document cache metrics and privacy constraints, including the current metrics gap and prohibition on donor values.
- [ ] Review the implementation against the security requirements in `docs/SECURE_DEPLOYMENT_TODO.md`.
- [ ] Run the complete build and test suite.
- [ ] Run formatting and static analysis checks.
- [ ] Review logs to confirm that donor values and uploaded file contents are not emitted.

## Non-goals

- [ ] Do not move to distributed cache or object storage as part of this checklist unless deployment requirements change.
- [ ] Do not change donation or accounting calculation semantics.
- [ ] Do not remove broad shared-snapshot reuse for narrower date ranges.
- [ ] Do not store raw donor or accounting records in browser storage.
- [ ] Do not treat cache keys as an authorization mechanism by themselves.
