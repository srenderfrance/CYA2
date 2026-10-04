# Synthetic database fixtures

These fixtures contain fictional data only. They are intended for an isolated development or test database and must not be run against production.

The fixture scripts are opt-in. They are not referenced by application startup, schema setup, deployment, or CI automatically.

## Donor edge cases

Run `synthetic-donor-edge-cases.sql` after `database/schema/001_InitialSchema.sql`:

```text
mysql --host=<host> --port=<port> --user=<setup-user> --password <isolated-database> < database/fixtures/synthetic-donor-edge-cases.sql
```

The script uses donor IDs `900001` through `900009`, fund `Test Fund`, and `SYNTH-DONOR-*` gift identifiers. It first removes only rows matching those fixture identifiers and donor IDs, then inserts the fixture. Run it only in an isolated development database.

Expected behavior is documented in `docs/SYNTHETIC_DONOR_DATA.md`.

## Accounting edge cases

Run `synthetic-accounting-edge-cases.sql` after `database/schema/001_InitialSchema.sql`:

```text
mysql --host=<host> --port=<port> --user=<setup-user> --password <isolated-database> < database/fixtures/synthetic-accounting-edge-cases.sql
```

The accounting script uses transaction numbers beginning with `ACC-SYNTH-`. It removes only rows with that prefix before inserting the fixture, so it can be rerun in an isolated development database. It must not be run against production.

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
