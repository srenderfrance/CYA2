# Pre-production schema changes

During pre-production, incorporate structural changes into [`database/schema/001_InitialSchema.sql`](../schema/001_InitialSchema.sql). The current initializer does not apply incremental migrations or record schema migration history.

## Initial setup

Create a separate empty database with database-level defaults `utf8mb4` and `utf8mb4_0900_ai_ci`. Verify those defaults before initializing: the initializer's session settings and the schema's table defaults do not change the database default. See [database setup](../README.md#initial-setup) for a read-only check and [Azure setup](../../docs/AZURE_SETUP.md#4-create-the-fresh-database-and-initialize-the-schema) for controlled correction guidance.

From the repository root, set `CYA2_SCHEMA_CONNECTION_STRING` securely for the setup account and run:

```powershell
dotnet run --project tools/Cya2.DatabaseMigrator -- --initialize
```

The initializer takes a MySQL named lock and applies the initial schema only when the selected database is empty. It does not create the database, and it rejects calls without `--initialize` or with `--migrations-directory`. MySQL DDL is not reliably rolled back as a whole; investigate partial initialization before retrying.

## Existing databases and runtime access

An existing database may contain data or an older schema. Do not rerun initialization or drop it as a shortcut; prepare a separately reviewed, non-destructive reconciliation and recovery plan. Changing a database default does not convert existing tables or columns.

The web application does not modify persistent schema at startup. Its separate restricted runtime MySQL account needs normal data permissions and `CREATE TEMPORARY TABLES`, not persistent `CREATE`, `ALTER`, or `DROP`. Setup and runtime users are managed by the DBA or deployment infrastructure, not by the application. Keep credentials out of scripts and source control.
