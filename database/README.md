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

Create the target database using the deployment environment or a database administrator. For a new database, the standalone initialization/migration runner applies the baseline and future migrations:

For example, using the MySQL client with credentials supplied outside the repository:

```text
dotnet run --project tools/Cya2.DatabaseMigrator -- --connection-string "$env:CYA2_SCHEMA_CONNECTION_STRING" --initialize
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

Once this baseline is established, add later structural changes as new, ordered scripts under `migrations`, for example:

```text
002_AddImportExpiration.sql
003_AddNewIndex.sql
```

Run the standalone migration tool with the schema setup account for later changes:

```powershell
dotnet run --project tools/Cya2.DatabaseMigrator -- --connection-string "$env:CYA2_SCHEMA_CONNECTION_STRING"
```

The runner applies scripts in order, records them in `Cya2SchemaMigrations`, prevents concurrent runs with a MySQL named lock, and fails if an applied script's checksum changes. The initial script uses `CREATE TABLE IF NOT EXISTS` for clean setup and limited safe re-execution; it is not intended to repair an existing database whose columns or constraints have drifted from the schema. `--initialize` is rejected when application tables already exist.

For a database that was already initialized with `schema/001_InitialSchema.sql`, verify the schema before treating that baseline as adopted. The runner intentionally does not infer or silently create a baseline history record. See `database/migrations/README.md` for migration authoring and baseline guidance.

