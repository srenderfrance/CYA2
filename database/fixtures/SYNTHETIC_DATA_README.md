# Synthetic database fixtures

These fixtures contain fictional data only. They are intended for an isolated development or test database and must not be run against production.

The fixture scripts are opt-in. They are not referenced by application startup, schema setup, deployment, or CI automatically.

## How the fixtures relate

The donor and accounting fixtures can be loaded into the same isolated database, but they populate separate data domains:

- `Accounts` and `SubAccounts` provide the selectable synthetic account context. The donor fixture creates Accounts for `Test Fund` and `Intern: Synthetic Intern`, plus Separate and Merged subaccount rows under `Test Fund` for `Synthetic Separate Fund` and `Synthetic Merged Fund`.
- `DonationData`, `Donors`, and `DonorContacts` contain donor and donation projections. The donor fixture uses donor IDs `900001` through `900013`.
- `AccountingData` contains accounting transactions. The accounting fixture creates the selectable Account `Synthetic Accounting` with account number `2200000`, then uses synthetic transaction numbers beginning with `ACC-SYNTH-` and the accounting class `General Administration:Fundraising:General Giving`.
- The schema has no foreign key directly linking `DonationData` to `AccountingData`. The application calculates donation totals and accounting balances through separate services, so loading both fixtures does not automatically combine their amounts into one account total.

The fixtures are useful together for testing screens and workflows that display donor and accounting information, but they do not represent a balanced accounting ledger for the donor gifts.

## Donor edge cases

Run `synthetic-donor-data.sql` after `database/schema/001_InitialSchema.sql`:

```text
mysql --host=<host> --port=<port> --user=<setup-user> --password <isolated-database> < database/fixtures/synthetic-donor-data.sql
```

The script creates and cleans up its synthetic account numbers `SYNTH-ACCOUNT-001` and `SYNTH-INTERN-001`, their subaccounts, donor IDs `900001` through `900013`, and `SYNTH-DONOR-*` gift identifiers. It expects an isolated development database because `Test Fund` is a fixture fund name and Accounts.Fund is unique. Run it only in an isolated development database.

### Expected donor results

| Donor | Edge case | Expected result |
|---|---|---|
| Donor1 | Account-name fallback and current contacts | Display name is `Donor1`, because no primary addressee or soft credit name is present. The donor view should show `Address1`, `City1`, `State1`, `11111`, `Country1`, `(111) 111-1111`, and `donor1@example.test`. The single gift is `OneTime`. |
| Donor2 Primary | Primary addressee contains soft credit | Display name is `Donor2 Primary`. The soft credit value `Donor2` is contained in the primary addressee, so the primary addressee remains the identity. The gift is `OneTime`. |
| Donor3 ThirdParty | Third-party soft credit | Display name is `Donor3 ThirdParty`, because the soft credit name is not contained in `Donor3 Account`. The resolution source is `SoftCredit`. |
| Anonymous | Anonymous gift | Display name is `Anonymous`. The gift has no identifying addressee, soft credit, honor/memorial, or contact values. The anonymous gift remains included in donation totals but should not expose the source account identity. |
| Donor5 Monthly | One missed month | Gifts occur in January, February, April, May, June, and July. March is intentionally absent. The one-month gap is within the missed-month tolerance, so the donor is `Monthly`. |
| Donor6 CatchUp | Catch-up payment | January and February are $10; April is $20 and represents the missed March amount plus April. The donor remains `Monthly`; the April gift should be eligible for catch-up classification and cover two months. |
| Donor7 Quarterly | Quarterly spacing | Gifts are three months apart in January, April, and July. The donor is `Quarterly`. |
| Donor8 Yearly | Yearly spacing | Gifts are twelve months apart in January 2024 and January 2025. The donor is `Yearly`. |
| Donor9 Sporadic | Irregular spacing | Gifts are two and seven month boundaries apart. The amounts are intentionally 100, 5, and 5 so the gaps are not interpreted as catch-up or prepayment coverage. The pattern is neither monthly, quarterly, nor yearly, so the donor is `Sporadic`. |
| Donor10 Separate Subaccount | Separate subaccount | The `Synthetic Separate Fund` gift is excluded from the primary donation total and appears in `SeparateSubfundTotals` for the `Separate` subaccount. |
| Donor11 Merged Subaccount | Merged subaccount | The `Synthetic Merged Fund` gift is included in the `Test Fund` total and contributes to `MergedSubfundDonations` because the subaccount kind is `Merged`. |
| Donors12-13 | Intern designation and Honor/Memorial display | The selectable account is `Intern: Synthetic Intern`. Both gifts are matched by `Intern = 'Synthetic Intern'`, regardless of their `Fund` value, and total 100.00. `HonorMemorialName` is also `Synthetic Intern` so the intern designation column can be inspected in donation details. |

### Donor verification queries

```sql
SELECT DonorId, COUNT(*) AS GiftCount, SUM(Amount) AS TotalAmount,
	   MIN(Date) AS FirstGift, MAX(Date) AS LastGift
FROM DonationData
WHERE GiftImportId LIKE 'SYNTH-DONOR-%'
GROUP BY DonorId
ORDER BY DonorId;

SELECT Id, DisplayName, IdentityKey, ResolutionSource
FROM Donors
WHERE Id BETWEEN 900001 AND 900009
ORDER BY Id;

SELECT DonorId, ContactType, ContactValue
FROM DonorContacts
WHERE DonorId BETWEEN 900001 AND 900013
ORDER BY DonorId, ContactType;
```

Donor search and donor lists should use the `Donors.DisplayName` values above. Donation detail views should show `PrimaryAddressee`, `SoftCreditName`, and `HonorMemorialName` where present. Contact values are current-state projections in `DonorContacts`, not historical contact records. The anonymous donor must not display `Hidden Account` or contact information.

### Account and subaccount verification query

```sql
SELECT AccountId, Fund, AccountingClass, AccountNumber
FROM Accounts
WHERE Fund IN ('Test Fund', 'Intern: Synthetic Intern')
   OR AccountNumber IN ('SYNTH-ACCOUNT-001', 'SYNTH-INTERN-001')
ORDER BY AccountId;

SELECT s.Id, s.AccountId, a.Fund AS PrimaryFund, s.SubFund, s.Kind
FROM SubAccounts s
JOIN Accounts a ON a.AccountId = s.AccountId
WHERE s.SubFund IN ('Synthetic Separate Fund', 'Synthetic Merged Fund')
ORDER BY s.Id;
```

## Accounting edge cases

Run `synthetic-accounting-data.sql` after `database/schema/001_InitialSchema.sql`:

```text
mysql --host=<host> --port=<port> --user=<setup-user> --password <isolated-database> < database/fixtures/synthetic-accounting-data.sql
```

The accounting script creates or refreshes the synthetic `Synthetic Accounting` Account with number `2200000`, then removes only rows with the `ACC-SYNTH-` prefix before inserting the fixture. It can be rerun in an isolated development database and must not be run against production.

### Expected accounting results

| Transactions | Edge case | Expected result |
|---|---|---|
| `001` | Ordinary expense | Included in `ExpenseTransactions`, contributes 100.00 to `ExpenseTotal`, and subtracts 100.00 from the balance. |
| `002` | Payroll check | Classified as an expense because `Type` is `Payroll Check`; contributes 200.00 to `ExpenseTotal`. |
| `003` | Expense account prefix | Classified as an expense because the account begins with `Expenses:`; contributes 50.00 to `ExpenseTotal`. |
| `004`, `005` | Signed transfers | Both are transfers. Their combined `TransferTotal` is 225.00 because 300.00 plus -75.00 preserves the signs. |
| `006` | Other income | Included in `OtherTransactions` and the balance, but not in expense or transfer totals. |
| `007` | General giving account 2200000 | Included in the balance, but not classified as an expense or transfer. |
| `008`, `009` | Excluded balance accounts | `Prepaids` and `Payroll Clearing Insurance` are excluded before classification and do not appear in `AllTransactions` or affect the balance. |
| `010` | Expense/transfer overlap | Classified as an expense, not a transfer, because expense classification has precedence. |
| `011` | Same-class account match | Included when matching `General Administration:Fundraising:General Giving`; normal account matching accepts the matching accounting class. |
| `012` | Class mismatch | Excluded when matching `General Administration:Fundraising:General Giving`; its 999.00 amount must not affect the selected account. |

For transactions `001` through `011`, excluding `008` and `009`, expected totals are:

- Expense total: **410.00** (`100 + 200 + 50 + 60`)
- Transfer total: **225.00** (`300 - 75`)
- Other total: **95.00** (`40 + 25 + 30`)
- Total balance before any account adjustment: **-90.00** (`-410 + 225 + 95`)

The fixture directly inserts canonical `AccountingData` rows. It tests classification and calculation behavior, but not workbook parsing or accounting import validation.

### Accounting verification query

```sql
SELECT AccountingClass, Num, Date, Amount, AccountNumber, Account, Type
FROM AccountingData
WHERE Num LIKE 'ACC-SYNTH-%'
ORDER BY Date, Num;
```

## Shared limitations

Both scripts insert canonical database rows directly. They are opt-in, fictional, isolated-database fixtures and are not referenced by application startup, schema setup, deployment, or CI.

- The donor script does not exercise workbook parsing, import validation, donor identity resolution, or automatic frequency recalculation. Persisted frequency values are set for stable display testing.
- The accounting script does not exercise workbook parsing or accounting import validation; it tests classification and balance calculations using canonical `AccountingData` rows.
- Use synthetic `.xlsx` import fixtures for end-to-end workbook behavior when those fixtures are created.
