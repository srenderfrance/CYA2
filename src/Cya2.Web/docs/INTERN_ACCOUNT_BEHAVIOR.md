# Intern Account Behavior

Intern accounts intentionally differ from normal fundraising accounts. This document records those differences and the code that implements them.

## Identity and Storage

- An intern account is recognized by the `Intern: ` fund prefix. The stored fund remains the account and lifecycle identity.
- The designation after that prefix is the intern identity used to retrieve donation and donor data.
- Intern accounts have `AccountingClass = NULL` and an `INT-<user id>` account number because they are not accounting-backed accounts.

Implementation:

- `src/Cya2.Core/Utilities/InternAccountUtility.cs`
- `src/Cya2.Application/Services/UserManagementService.cs` (`EnsureInternAccountAccessAsync`)
- `database/schema/001_InitialSchema.sql`

## Donation and Donor Data

- Normal accounts load donation and donor data by fund, including merged subaccounts where appropriate.
- An intern account loads by `DonationData.InternName`, never by its fund. A single source fund can contain data belonging to multiple interns, so using the fund would return the wrong rows.
- Imported names may differ in order or punctuation. Intern lookup accepts normalized equivalent forms to keep valid imported history connected.

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
- Renaming an Intern user renames the linked account identity while retaining that account and its history.
- Creating or renaming to a designation with an existing intern account requires explicit Admin confirmation before reuse. This prevents a user from silently inheriting another intern account’s history.
- Changing an Intern user to another role removes access to linked intern accounts without deleting the account or data.

Implementation:

- `src/Cya2.Application/DTOs/AdminUserDtos.cs`
- `src/Cya2.Application/Services/UserManagementService.cs`
- `src/Cya2.Web/Components/Pages/Admin.razor`

## Authorization and Account Context

- Intern authorization and selected-account resolution preserve `InternName` as the lookup identity.
- Normal accounts use Fund or AccountId for their normal authorization and selection paths.
- Account context and fund-reference DTOs derive and carry `InternName` separately from the stored fund so presentation cannot accidentally change data lookup behavior.

Implementation:

- `src/Cya2.Application/Services/UserAuthorizationService.cs`
- `src/Cya2.Application/Services/HomeAuthorizationService.cs`
- `src/Cya2.Application/Services/UserAccountContextService.cs`
- `src/Cya2.Application/Services/FundReferenceService.cs`
- `src/Cya2.Application/Services/AccountSelectionCoordinator.cs`

## Dropdown Display and Sorting

- All account dropdowns display intern accounts as `LastName, FirstName (Intern)`.
- The stored `Intern: <designation>` value is not changed for display.
- Dropdown sorting uses the display sort key, placing intern accounts by surname with other named accounts rather than under `Intern`.
- Selection values preserve the account ID, Fund, or `InternName` required by the individual data flow; display labels must not be used as data lookup keys.
- User Settings uses `SelectionKey`, not `AccountingClass`, because intern accounting classes are intentionally null.

Implementation:

- `src/Cya2.Core/Utilities/InternAccountUtility.cs`
- `src/Cya2.Web/Components/Pages/Admin.razor`
- `src/Cya2.Web/Components/Pages/Home.razor`
- `src/Cya2.Web/Components/Pages/Donations.razor`
- `src/Cya2.Web/Components/Pages/Donors.razor`
- `src/Cya2.Web/Components/Pages/Expenses.razor`
- `src/Cya2.Web/Components/Pages/UserSettings.razor`
- `src/Cya2.Application/Services/FinancialDashboardService.cs`
- `src/Cya2.Application/Services/ExpenseService.cs`

## Caching and Summary Warming

- Intern donation caches and summary warmups are keyed by intern identity rather than by a normal fund/accounting combination.
- Accounting cache warming is skipped for intern accounts because no accounting data exists for them.
- Authorization/context caches are invalidated after user and account-access lifecycle changes.

Implementation:

- `src/Cya2.Application/Services/AuthoritativeDataCacheLoaders.cs`
- `src/Cya2.Application/Services/CacheRebuildService.cs`
- `src/Cya2.Application/Services/FinancialDashboardService.cs`
- `src/Cya2.Application/Services/AccountSnapshotWarmupService.cs`
- `src/Cya2.Application/Services/UserAccountContextService.cs`
- `src/Cya2.Application/Services/UserManagementService.cs`
