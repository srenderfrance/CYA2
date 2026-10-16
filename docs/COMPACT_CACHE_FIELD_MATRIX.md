# Compact Authoritative Cache Field Matrix

## Scope

The shared authoritative caches are account-scoped, generation-aware, and broad-window caches. They contain immutable facts needed to project page DTOs. They do not contain authorization state, user account lists, selected account state, selected subaccount state, date-range state, totals, or page-specific wrapper DTOs.

## Donation/donor cache

| Field | Donations | Donors | Required in authoritative cache | Notes |
|---|---:|---:|---:|---|
| Donation ID | deduplication | deduplication | Yes | Primary fact identity. |
| Donor ID | donor display/detail linkage | donor grouping | Yes | Never group only by display name. |
| Donor display name | rendered | grouping fallback/display | Yes | Current donation identity projection. |
| Date | rendered/filtering | range/filtering/frequency | Yes | Required for all date projections. |
| Amount | rendered | totals | Yes | Use one numeric representation consistently. |
| Frequency | rendered | frequency label | Yes, nullable | Donor service can classify when absent. |
| Fund/account identity | filtering | primary/separate fund combination | Yes | Prefer compact account/fund reference rather than repeated page strings where practical. |
| Soft credit name | rendered | optional donor attribution | Yes | Required by Donations display. |
| Anonymous flag | rendered behavior | privacy/display behavior | Yes | Must survive projection. |
| Payment method | not currently rendered | not required by current Donors grid | No for current pages | Add only if a confirmed workflow needs it. |
| Gift type | not currently rendered | not required by current Donors grid | No for current pages | Add only if a confirmed workflow needs it. |
| Addressee | not currently rendered | not required by current Donors grid | No for current pages | Keep out unless a page workflow requires it. |
| Date created | not rendered | not required for grouping | No for current pages | Exclude from compact cache. |
| Intern designation | intern-specific lookup path | intern-specific lookup path | Separate path | Do not force intern data into the normal account cache contract. |
| Subaccount ID/fund/kind | selection/fund expansion | all/separate fund expansion | Yes | Small metadata collection shared by donation and donor projections. |

Current-state donor contacts are not repeated in donation rows. `DonorContactCache` remains keyed by `DonorId` and contact generation and is joined when a donor or donation contact projection is built.

## Expense/accounting cache

| Field | Expenses | Required in authoritative cache | Notes |
|---|---:|---:|---|
| Transaction ID | row identity | Yes | Required for stable display and deduplication. |
| Date | rendered/filtering | Yes | Required for date projection. |
| Number | rendered | Yes | Expense DTO `Num`. |
| Amount | rendered/totals | Yes | Use one numeric representation consistently. |
| Account label | rendered | Yes | Required for transaction display. |
| Type | transfer classification | Yes | Required before page DTO categorization. |
| Accounting class | rendered | Yes | Required by expense row DTO. |
| Account number | rendered | Yes | Required by expense row DTO. |
| Date created | not rendered | No for current page | Exclude unless an audit workflow requires it. |

## Excluded page state and derived values

The authoritative caches exclude:

- `UserAccounts` and authorization/access lists;
- selected account and selected subaccount;
- date range start/end;
- `ExpenseTotal`, `TransferTotal`, and donor totals;
- `DonationDataDto`, `ExpenseDataDto`, and `DonorSummaryDto` wrappers;
- subaccount dropdown options and localized display text;
- donor contact strings, which remain in the donor-contact projection cache;
- Home summary DTOs, which remain in `HomeSummaryCache`.

## Deduplication strategy

- Store one donation fact per donation ID per authoritative account/fund entry.
- Store one accounting fact per transaction ID per accounting entry.
- Keep account/fund identity in the cache key or compact interned/reference representation where possible; do not create separate cached copies for every page DTO.
- Project fresh page DTO instances from immutable cache values so page state cannot mutate shared cache entries.
