# Compact Authoritative Cache Design

## Decision

Use two broad, account-scoped, generation-aware authoritative caches:

1. `IDonationDataCache` for donation facts, donor identity inputs, and subaccount metadata.
2. `IAccountingDataCache` for accounting facts used to classify and display expenses and transfers.

The caches are not page DTO caches. They contain immutable fact projections that are shared by page services and projected into fresh page DTOs after authorization.

## Projection boundary

```text
Database
  -> DonationDataCache + DonorContactCache
	  -> DonationDataDto for Donations.razor
	  -> DonorSummaryDto / DonorDetailDto for Donors.razor
  -> AccountingDataCache
	  -> ExpenseDataDto for Expenses.razor

HomeSummaryCache remains independent and stores FinancialSummaryDto values.
```

Each consumer must authorize the requested account before reading or projecting an authoritative cache entry. Cache entries must never contain user account lists, selected account state, selected subaccount state, or role information.

## Donation cache contents

The donation cache retains one compact record per donation ID, including only:

- donation and donor identity keys;
- date and amount;
- display name, frequency, soft credit, and anonymous state;
- fund/account-name inputs needed by donor identity resolution;
- addressee needed by the existing identity resolver;
- subaccount ID, fund, and kind metadata.

Payment method, gift type, date-created metadata, current contacts, and page-specific selection fields are excluded unless a confirmed workflow demonstrates a requirement.

Current donor contact values remain in `IDonorContactCache`, keyed by donor ID and donor-contact generation. They are joined during donor or donation projection, avoiding repeated email/phone/address strings on every donation row.

## Accounting cache contents

The accounting cache retains the fields needed by `ExpenseTransactionDto` and transfer classification:

- transaction ID;
- date;
- number;
- amount;
- account label;
- transaction type;
- accounting class;
- account number.

Totals and expense/transfer lists are derived per request. Date ranges and selected accounts are request state.

## Key and invalidation policy

- Donation entries use account ID, normalized fund, donations generation, and broad date coverage.
- Accounting entries use account ID, accounting class/number identity, accounting generation, and broad date coverage.
- Account metadata changes must invalidate entries whose account or subaccount interpretation changed.
- Donor-contact changes invalidate only contact projections and donor DTOs, not donation facts.
- Authorization changes never make a cache entry an authorization grant; services revalidate access before projection.

## Memory expectations

The largest savings should come from one shared fact collection per domain rather than separate `DonationDataDto`, donor summary, and expense DTO collections for the same account and date window. The cache should also reduce repeated string fields and exclude unused source metadata. Exact savings must be measured with representative account sizes; a DTO projection with identical fields would not be smaller than the current snapshot.

## Migration sequence

1. Implement bounded single-flight donation and accounting cache implementations.
2. Update loaders to populate the two caches from repository results.
3. Update DonationService, DonorService, and ExpenseService to check the appropriate authoritative cache first and project page DTOs.
4. Replace AccountSnapshotWarmupService raw snapshot warming with the two authoritative cache warm operations.
5. Remove AccountSnapshotCache only after all consumers and tests migrate.
6. Preserve broad-window reuse, current authorization checks, startup probe/limited mode, and central invalidation.

## Risks

- Intern donation paths use designation-based repository queries and may require a specialized cache key/loader.
- Donor identity resolution currently depends on fields not displayed in the grid; those fields must remain until the resolver is simplified or proven unnecessary.
- Merged and separate subaccount rules must remain explicit and covered by tests.
- Contact projections are current-state data and must not become historical contact storage.
