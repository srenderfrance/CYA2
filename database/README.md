# CYA2 database setup

This directory contains the schema for implementing the new CYA2 application database.

The data is to be populated by exported data from the authoritative sources:

- Donation and donor data from the third-party donor system.
- Accounting data from QuickBooks exports.

This is not intended as a database migration. 

## Initial setup

`schema/001_InitialSchema.sql` creates the clean application schema, including:

- Accounts, SubAccounts, Users, and AccountsUsers
- Donors, DonorContacts, as well as their rollback tables.
- DonationData, AccountingData as well as their backup and snapshot tables.
- Donation and accounting rollback payload and snapshot tables used by the import rollback workflow.

It does not contain production data, fake seed data, database users, passwords, or other secrets.

This describes the schema script, not the entire directory: opt-in synthetic SQL data is separately available under [fixtures](fixtures/SYNTHETIC_DATA_README.md). See the [production review](../docs/PRODUCTION_REVIEW.md) for unverified import/staging/rollback behavior and pre-live database acceptance.

Create an empty target database with the database default character set `utf8mb4` and collation `utf8mb4_0900_ai_ci`. Verify the defaults even when the hosting provider created the database:

```sql
SELECT SCHEMA_NAME, DEFAULT_CHARACTER_SET_NAME, DEFAULT_COLLATION_NAME
FROM information_schema.SCHEMATA
WHERE SCHEMA_NAME = 'cya2_test';
```

Substitute the actual database name. Import staging relies on the database default; `utf8mb3` combined with `utf8mb4` collations can produce MySQL error 1253. Schema initialization's session `SET NAMES` and individual table defaults do not change the database default. See [Azure setup](../docs/AZURE_SETUP.md#4-create-the-fresh-database-and-initialize-the-schema) for a database-default correction and safety checks. That change does not convert existing tables or data.

For a new empty database, the standalone initializer applies the initial schema:

For example, using the MySQL client with credentials supplied outside the repository:

```text
dotnet run --project tools/Cya2.DatabaseMigrator -- --initialize
```

The target database must already exist, and `CYA2_SCHEMA_CONNECTION_STRING` must use the setup account. The application runtime account should not need `CREATE`, `ALTER`, `DROP`, or other schema-management permissions. The application assumes that this schema setup has completed successfully before imports are run. Permission to create temporary tables is required for import transactions, but those tables are session-scoped and not persistent.

The setup account and restricted runtime account are MySQL infrastructure users. Create and grant them through the hosting provider, DBA process, or a separately controlled database provisioning script. Do not create them from the Blazor application or include passwords in the repository.

## Importing data

Schema setup and source data import are separate operations:

1. Create the empty database.
2. Run the initialization/migration runner with `--initialize`.
3. Configure the application runtime connection string.
4. Import donor data through the application's donor-system Excel workflow.
5. Import accounting data through the application's QuickBooks Excel workflow.
6. Validate counts, totals, and import results.

## Future schema changes

During pre-production, incorporate schema changes into `schema/001_InitialSchema.sql`; the initializer does not apply incremental migrations or record schema history. It takes a named lock and rejects nonempty databases. MySQL DDL may leave partial changes if initialization fails, so investigate before retrying. Existing databases with data or schema drift require a separately reviewed, non-destructive reconciliation and recovery plan; do not rerun initialization or drop existing data as a shortcut.

