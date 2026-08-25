# UI Cache Optimization Handoff

## Goal
Reduce duplicate Blazor page loads and database reads while preserving account, subaccount, and date-range selections.

## Current phase
The shared snapshot migration covers `DonationService`, `ExpenseService`, and the default date-range account path in `DonorService` for ranges contained within snapshot coverage. Admin now has a separate scoped bounded preload for popup data; broad Home-style account summaries remain lazy.

## Completed
- Baseline logs show duplicate cold donation and dashboard work; warm donation reads are fast.
- Added page-operation scopes for Home, Donations, Expenses, and Donors.
- Added Blazor circuit open/close logging.
- Enabled readable console log scopes so Application and Infrastructure entries inherit page-operation metadata.
- Removed uncorrelated Expenses console output and development-tool/startup log noise.
- Disabled prerendering for Home, Donations, Expenses, and Donors while retaining Interactive Server rendering.
- Added a singleton bounded single-flight account snapshot cache with immutable snapshot values, normalized account keys, LRU limits, and import-wide invalidation support.
- Added the first `DonationService` snapshot consumer path with immutable donation/subaccount mapping and preserved repository fallbacks.
- Added `IAccountSnapshotLoader` and `AccountSnapshotLoader` to centralize complete account snapshot creation.
- Wired canonical `DonationService` snapshot loads through the shared loader so donation and expense consumers will use the same account-keyed snapshot.
- Migrated `ExpenseService` aggregate, expense-only, transfer-only, and summary operations to reuse canonical account snapshots.
- Added requested-range filtering and source diagnostics for ExpenseService snapshot loads and hits.
- Changed ExpenseService snapshot eligibility from exact canonical-range equality to date containment; selected ranges load the broad snapshot coverage once and filter in memory.
- Migrated the default `Donors.razor` account-range summary path to reuse the shared donation snapshot.
- Preserved separate subaccount queries for the Donors `All` selection, because those funds are not included in the main account snapshot.
- Preserved All Dates, explicit subaccount, multi-fund, donor detail, name, and search repository paths.
- Confirmed the solution builds successfully after the shared-loader wiring.
- Confirmed the solution builds successfully after the ExpenseService migration.
- Added a scoped `AdminPreloadService` with single-flight preload state for Admin accounts, subaccounts, staff, and Accounts Overview aggregates.
- Wired `AccountsOverviewView` and `FundReferencesView` to reuse the Admin preload state; `AccountSummariesView` remains lazy-loaded.
- Disabled Admin prerendering so Admin data initialization runs only after the Interactive Server circuit opens.
- Added Admin preload invalidation after successful imports, rollback, account updates/deletes, and subaccount creation.
- Confirmed a clean command-line solution build after the Admin changes.
- Added Admin recent-account snapshot warming for the five most recently selected Fundraising Accounts.
- Added a separate Admin default-account startup warmup; the configured default account is warmed when present but is excluded from the five-account recent-selection limit.
- Reused the regular generation-0 account snapshot key and broad in-coverage range so Donations, Expenses, and Donors can consume the warmed snapshots without another repository load.
- Limited Admin warmups to two concurrent loads and made selection warming non-blocking.
- Added invalidation protection so in-flight snapshot loads cannot repopulate stale data after Admin mutations, imports, or rollback.

## Confirmed finding
Each initial page load previously ran twice: once during the HTTP prerender request and once after the Interactive Server circuit opened. Both executions could independently miss caches and query the database. The four primary data pages now use `InteractiveServerRenderMode(prerender: false)`, so their data-loading lifecycle runs only after Interactive Server startup.

This removes prerendered page HTML for these pages. The UI should show its existing loading state until the interactive circuit is ready; this tradeoff should be checked manually in the next test run.

## Home prefetch finding
Home has one active background prefetch entry point: `QueueBackgroundPrefetch` calls `PrefetchFundAsync` for the selected account. The repeated Expense and Donor cache-miss messages are expected double-checks inside their services; each is followed by one database load and one cache set. No duplicate Home prefetch path was found, so Home orchestration was not changed.

## Cold-load reproduction
1. Stop the application to clear process caches.
2. Start it in Debug mode.
3. Open one browser tab and navigate once to Home, Donations, Expenses, or Donors.
4. Copy the related Debug output from circuit-open through the page's `init complete` entry.
5. Repeat with a new tab only when comparing circuits.

## Evidence to capture
Each scoped entry includes readable `Page`, `Operation`, `OperationId`, `CircuitId`, and `ScopeId` values.

Interpret duplicate reads as follows:
- Same `CircuitId` and same `OperationId`: one operation made duplicate downstream calls.
- Same `CircuitId` and different `OperationId`: multiple page operations occurred in one circuit.
- Different `CircuitId`: separate browser tabs or circuits.
- `CircuitId=prerender`: a pre-interactive rendering scope.

## Snapshot cache status
The singleton cache now has an active `DonationService` consumer for the canonical selected-account range. Its loader creates complete immutable snapshots with donations, accounting rows, and subaccounts, preventing multiple consumers from overwriting the same account key with partial data. `DonationService` performs authorization before cache access, derives filtered results from immutable snapshot values, and preserves legacy repository/session-DTO behavior for separate-subaccount and non-canonical range paths. The cache deduplicates concurrent loads and evicts least-recently-used entries within bounded entry/byte limits.

## Next implementation decision
Manually verify all three snapshot consumers: the first in-coverage account load should log one snapshot miss/load with the broad queried range, repeated Donations, Expenses, and Donors requests should log snapshot-cache reuse, and import/rollback invalidation should remove the snapshot. Verify account switching loads one snapshot per account, Last Month and other in-coverage ranges filter correctly, Donors `All` includes separate-fund donations, explicit subaccount and All Dates behavior remains unchanged, and ranges outside snapshot coverage retain their existing repository/session-cache behavior.

For Admin, stop the application before testing to clear process caches. On startup, verify that Accounts Overview and Fund References use the bounded preload without creating regular snapshots for every account; if the Admin has a default account, verify that exactly that account is warmed separately. Then select five non-default accounts and confirm they are all retained as recent warmups; selecting the default account must not consume or evict one of those five slots. Verify Admin mutations, imports, and rollback invalidate both preload data and warmed snapshots.

Also select six different Fundraising Accounts in Admin. The first five valid selections should start broad snapshot warmups, repeated selections should report snapshot-cache reuse, and the sixth selection should cause the least-recent Admin selection to fall out of the Admin MRU list. The underlying singleton snapshot cache may retain more entries according to its own global LRU policy; the Admin five-account limit controls warmup recency, not forced eviction of snapshots used by other pages.

## Validation command
`dotnet build "C:\Users\srend\dev\Cya2\cya2.0\cya2.0.sln"`
