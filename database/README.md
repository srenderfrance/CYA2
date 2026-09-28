# CYA2 database setup

This directory contains the schema for implementing the new CYA2 application database.

The data is to be populated by exported data from the authoritative sources:

- Donation and donor data from the third-party donor system.
- Accounting data from QuickBooks exports.

This is not intended as a database migration. 

## Initial setup

`schema/001_InitialSchema.sql` creates the clean application schema, including:

- Accounts, Subccounts, Users, and AccountsUser
- Donors, DonorContacts, as well as their rollback tables.
- DonationData, AccountingData as well as their backup and snapshot tables.
- Donation and accounting rollback payload and snapshot tables used by the import rollback workflow.

It does not contain production data, fake seed data, database users, passwords, or other secrets.

Create the target database using the deployment environment or a database administrator, select that database, and then apply the script.

For example, using the MySQL client with credentials supplied outside the repository:

```text
mysql --host=<host> --user=<setup-user> --password <database-name> < database/schema/001_InitialSchema.sql
```

The application runtime account should not need `CREATE`, `ALTER`, `DROP`, or other schema-management permissions. The application assumes that this schema setup has completed successfully before imports are run. Permission to create temporary tables is required for import transactions, but those tables are session-scoped and not persistent.

## Importing data

Schema setup and source data import are separate operations:

1. Create the empty database.
2. Apply `001_InitialSchema.sql`.
3. Configure the application runtime connection string.
4. Import donor data through the application's donor-system Excel workflow.
5. Import accounting data through the application's QuickBooks Excel workflow.
6. Validate counts, totals, and import results.

## Future schema changes

Once this baseline is established, add later structural changes as new, ordered scripts under `schema`, for example:

```text
002_AddImportExpiration.sql
003_AddNewIndex.sql
```

A deployment process or migration runner should apply those scripts in order and record which scripts have been applied. The initial script uses `CREATE TABLE IF NOT EXISTS` for clean setup and limited safe re-execution; it is not intended to repair an existing database whose columns or constraints have drifted from the schema.

