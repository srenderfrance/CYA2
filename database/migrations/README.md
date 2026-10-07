# Database migrations

This directory contains structural changes applied after the clean-install baseline at `database/schema/001_InitialSchema.sql`.

## Naming

Use an ordered filename:

```text
002_AddImportExpiration.sql
003_AddNewIndex.sql
```

The numeric prefix must be unique and contiguous among migration files. Use one migration per structural change. Do not edit a migration after it has been applied; create a new migration instead.

## Execution

Run the standalone migrator with a schema/setup connection string. For a new empty database, include `--initialize`:

```powershell
dotnet run --project tools/Cya2.DatabaseMigrator -- --connection-string "$env:CYA2_SCHEMA_CONNECTION_STRING" --initialize
```

Initialization applies `database/schema/001_InitialSchema.sql` only when the selected database contains no application tables. It records the baseline as migration ID `000001`, then applies pending scripts in numeric order. The runner rejects checksum changes to previously applied scripts and uses a MySQL named lock so concurrent deployment processes do not apply migrations simultaneously.

For an already initialized database, omit `--initialize`:

```powershell
dotnet run --project tools/Cya2.DatabaseMigrator -- --connection-string "$env:CYA2_SCHEMA_CONNECTION_STRING"
```

The runner refuses `--initialize` when application tables already exist. It does not create the target database itself; create the database first and provide it in the connection string.

The web application does not run migrations during startup. Its runtime connection should use a separate restricted account that can perform normal data operations and create session-scoped temporary tables, but cannot create, alter, or drop persistent schema objects.

The setup/migration account and runtime account are MySQL database users managed by the database administrator or deployment infrastructure. They are not application records and are not created by the Blazor application or the initialization runner. Store their credentials in deployment secrets, never in SQL scripts or source control.

## Existing databases

For an existing database created from `database/schema/001_InitialSchema.sql`, omit `--initialize` and use the runner for later migrations. The runner does not automatically create a baseline history row for an existing schema because it cannot safely prove that the database exactly matches the baseline.
