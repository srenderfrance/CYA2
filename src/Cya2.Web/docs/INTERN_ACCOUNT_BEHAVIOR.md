# Intern Account Behavior

Intern accounts intentionally differ from normal fundraising accounts. This document records those differences and the code that implements them.

The rules below are the intended contract. The [production review](../../../docs/PRODUCTION_REVIEW.md) A07 identifies central warmup/cache paths requiring correction/verification; this document is not evidence that every intern path already complies.

## Identity and Storage

- An intern account is recognized by the `Intern: ` stored label prefix. `AccountId` is authoritative for account relationships; the label is not a donation lookup key. Donation/donor lookup identity is `InternName`.
- The designation after that prefix is the intern identity used to retrieve donation and donor data.
- Intern accounts have `AccountingClass = NULL` and an `INT-<user id>` account number because they are not accounting-backed accounts.

Implementation:

- `src/Cya2.Core/Utilities/InternAccountUtility.cs`
- `src/Cya2.Application/Services/UserManagementService.cs` (`EnsureInternAccountAccessAsync`)
- `database/schema/001_InitialSchema.sql`

## Donation and Donor Data

- Normal accounts load donation and donor data by fund, including merged subaccounts where appropriate.
- An intern account loads by `DonationData.InternName`, never by its fund. A single source fund can contain data belonging to multiple interns, so using the fund would return the wrong rows.
- Donor selection and account-authorized export use the same intern-name lookup for all dates and date ranges; their summary cache scope retains that intern identity.
- Donor identity is shared across funds using direct Constituent IDs or unambiguous normalized-name matches. This never broadens intern donation reads: only gifts matching the selected InternName contribute to totals. Contacts come only from direct gifts linked to the shared donor, not intermediary contacts.
- Single and bulk reads require an exact stored `InternName`, including case, accents, punctuation, order, and whitespace; they do not infer alternate names. Imports must use the account designation exactly or those gifts will not appear for that account.

Implementation:

- `src/Cya2.Application/Services/AccountFactDataService.cs`
- `src/Cya2.Application/Services/DonationService.cs`
- `src/Cya2.Application/Services/DonorService.cs`
- `src/Cya2.Infrastructure/Repositories/DonationReadRepository.cs`
- `src/Cya2.Infrastructure/Services/DonationImportProcessor.cs`

## Accounting and Expenses

- Intern accounts have donation data only.
- They intentionally return no `AccountingData`, expense transactions, accounting cache entries, or accounting snapshots.
- Home and other views use intern mode to avoid showing fund-only accounting features for an intern account.

Implementation:

- `src/Cya2.Application/Services/AccountFactDataService.cs`
- `src/Cya2.Application/Services/AccountCalculationService.cs`
- `src/Cya2.Application/Services/AccountSnapshotWarmupService.cs`
- `src/Cya2.Application/Services/AdminRecentAccountSnapshotService.cs`
- `src/Cya2.Web/Components/Pages/Home.razor`

## User Lifecycle and Access

- Creating an Intern user automatically creates or links an intern account and makes it the user’s default account.
- Deleting an Intern user revokes access but retains the account and historical donation/donor data.
- Renaming an Intern user is intended to correct typos, not to represent an actual name change. It retains the linked account and donation rows while changing the exact lookup designation; imported `InternName` values must match the corrected designation.
- Creating or renaming to a designation with an existing intern account requires explicit Admin confirmation before reuse. This prevents a user from silently inheriting another intern account’s history.
- An existing Intern user's role cannot be changed. A different role requires a new user account; the Admin role selector is disabled for an existing Intern. Other permitted profile and access changes remain available.

Implementation:

- `src/Cya2.Application/DTOs/AdminUserDtos.cs`
- `src/Cya2.Application/Services/UserManagementService.cs`
- `src/Cya2.Web/Components/Pages/Admin.razor`

## Authorization and Account Context

- Intern authorization checks the currently linked intern account, and the account designation supplies `InternName` as the lookup identity.
- Normal accounts use Fund or AccountId for their normal authorization and selection paths.
- Account context and fund-reference DTOs derive and carry `InternName` separately from the stored fund so presentation cannot accidentally change data lookup behavior.
- Home authorizes every account selection against the current account context before loading dashboard data. Intern Home selection continues to resolve with `InternName`; it does not use a previously selected normal-account value.
- When an account selection is no longer authorized, Home reports access denied. The no-account message is reserved for a current account context with no associated accounts.

Implementation:

- `src/Cya2.Application/Services/UserAuthorizationService.cs`
- `src/Cya2.Application/Services/HomeAuthorizationService.cs`
- `src/Cya2.Application/Services/UserAccountContextService.cs`
- `src/Cya2.Application/Services/FundReferenceService.cs`
- `src/Cya2.Application/Services/AccountSelectionCoordinator.cs`

## Dropdown Display and Sorting

- All account dropdowns display intern accounts as `LastName, FirstName (Intern)`.
- The Accounts Overview grid and its copied table use the same intern-aware display label; the underlying fund and intern donation lookup identity remain unchanged.
- The stored `Intern: <designation>` value is not changed for display.
- Dropdown sorting uses the display sort key, placing intern accounts by surname with other named accounts rather than under `Intern`.
- Selection values preserve the account ID, Fund, or `InternName` required by the individual data flow; display labels must not be used as data lookup keys.
- User Settings uses `SelectionKey`, not `AccountingClass`, because intern accounting classes are intentionally null.
- Selector presentation uses the refreshed database-backed account context rather than sign-in-time role claims. Intern users see their single account as read-only text; Admin and Viewer users receive searchable selectors; regular users receive non-searchable selectors. This updates with refreshed account data after a role change.

Implementation:

- `src/Cya2.Core/Utilities/InternAccountUtility.cs`
- `src/Cya2.Web/Components/Pages/Admin.razor`
- `src/Cya2.Web/Components/Shared/AccountsOverviewView.razor`
- `src/Cya2.Web/Components/Pages/Home.razor`
- `src/Cya2.Web/Components/Pages/Donations.razor`
- `src/Cya2.Web/Components/Pages/Donors.razor`
- `src/Cya2.Web/Components/Pages/Expenses.razor`
- `src/Cya2.Web/Components/Pages/UserSettings.razor`
- `src/Cya2.Application/Services/FinancialDashboardService.cs`
- `src/Cya2.Application/Services/ExpenseService.cs`

## Caching and Summary Warming

- Intern Home summary warmups and live reads share a key containing `AccountId`, exact intern identity, donation/metadata generation and calendar period; the warm builder uses the account's overhead.
- Central rebuild and the overview fallback exclude intern accounts from normal fund and accounting loads. Intern summary, overview donation totals, and missing-gift warmups use exact `InternName` rows; overview intern balances are zero and do not create accounting balance entries.
- Accounting cache warming is skipped for intern accounts because no accounting data exists for them.
- Authorization/context caches are invalidated after user and account-access lifecycle changes.
- Creating or updating an Intern also invalidates account metadata and schedules the central rebuild for the current intern designation.

Implementation:

- `src/Cya2.Application/Services/AuthoritativeDataCacheLoaders.cs`
- `src/Cya2.Application/Services/CacheRebuildService.cs`
- `src/Cya2.Application/Services/FinancialDashboardService.cs`
- `src/Cya2.Application/Services/AccountSnapshotWarmupService.cs`
- `src/Cya2.Application/Services/UserAccountContextService.cs`
- `src/Cya2.Application/Services/UserManagementService.cs`
