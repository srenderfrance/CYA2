# MySQL integration tests

`tests/Cya2.Database.Tests` exercises the real migrator and Infrastructure repositories against disposable MySQL 8.0.40 Linux containers. It never loads application configuration, user secrets, or an external database connection string.

## Run locally

Install Docker Desktop (Linux containers), start its engine, and install the .NET SDK selected by `global.json`. Docker CLI must be on PATH. The first run downloads `mysql:8.0.40` from Docker Hub.

From the repository root in PowerShell:

```powershell
$env:CYA2_RUN_MYSQL_TESTS = '1'
dotnet test tests/Cya2.Database.Tests/Cya2.Database.Tests.csproj --configuration Release
Remove-Item Env:CYA2_RUN_MYSQL_TESTS
```

Set the variable before test discovery. In Visual Studio, restart the IDE from an environment with that variable set and rediscover tests, or use the terminal command above. Ordinary solution runs without the variable report these tests as **skipped**. When enabled, missing Docker, image-pull failures, startup failures and database errors fail tests; they are not silently skipped.

The fixture generates credentials, starts a loopback-only random host port, waits for MySQL readiness, creates a fresh database and restricted runtime identity for each test, and removes the container after execution. It does not mount host data or reuse a developer database. A forcibly terminated test host can leave a container behind: remove only the container with the generated `cya2-tests-` name shown by `docker ps -a` using `docker rm --force <container-name>`.

The fixture explicitly creates each database with default character set `utf8mb4` and collation `utf8mb4_0900_ai_ci`. It does not test a database with an incompatible `utf8mb3` default or prove deployed Azure MySQL 8.4 behavior. Verify database-level defaults and run a synthetic UI import in each target environment; see [Azure setup](AZURE_SETUP.md#4-create-the-fresh-database-and-initialize-the-schema).

## Coverage

- Fresh initial-schema setup through the built DatabaseMigrator executable; required columns, snapshot precision and absence of migration history; rejection of reruns on a nonempty database without changing the tested data.
- Runtime identity limited to SELECT, INSERT, UPDATE, DELETE and CREATE TEMPORARY TABLES; persistent CREATE/ALTER/DROP and GRANT denied.
- Canonical donation writes, donor deduplication, stale-contact removal, retained DateCreated, and donation repository readback.
- Contact freshness: unchanged timestamps, exact/casing-only retained-value changes, new values with null DateModified, removed values, rollback-restored timestamps and untouched donors outside the upload.
- Injected contact-insert failure after donor/donation writes verifies the transaction leaves no partial persistent data.
- Donation backup/replacement/rollback restores donations and contacts and removes new orphan donors.
- Atomic accounting staging/publication, full backup/replacement/rollback including rows before the replacement window.
- Publication failures preserve existing rows, contacts and snapshots; rollback restores touched donors whose only prior gifts predate the replacement window; pre-cancelled replacement preserves data.
- Missing donation snapshot produces a safe failed rollback without deleting data.
- Constituent identity: shared donors across funds, organization IDs excluded from credited identities, ambiguous names kept separate, verified direct contacts preserved on soft-credit-only uploads, and full rollback of historical relinking.
- Name-only promotion preserves direct contact timestamps; newly ambiguous retained soft credits do not inherit constituent-specific contacts.

`ImportValidationTests` also runs without Docker and exercises the actual processors with synthetic in-memory Excel workbooks and fake persistence. Coverage includes required Constituent ID headers with blank values allowed, leading-zero ID preservation, source-ID preservation during duplicate consolidation, soft-credit contact exclusion, invalid/empty rejection, domain invalidation, pre-cancellation, uncertain outcomes and shared write serialization. MySQL cases remain opt-in. Accounting no longer uses local-infile loading or partial live-write fallbacks. See [the import safety contract](IMPORT_SAFETY.md); the constituent schema is defined in the evolving initial schema, not a new migration.

These tests do not exercise OAuth or Blazor UI uploads, or prove hosting configuration, production workload performance, initialization recovery after partial DDL, lost commit acknowledgements, or account/subaccount reporting semantics.

## CI

`.github/workflows/mysql-integration-tests.yml` explicitly enables Docker tests on pushes to main and manual dispatch, uploads TRX results and removes interrupted containers. No secrets or external MySQL service are needed. It has no deployment or approval steps. The existing deployment workflow is unchanged: its ordinary solution run skips the opt-in database tests, so **the separate MySQL job does not currently gate deployment**. Review its result independently before promotion; making deployment depend on it is a separate pipeline change.

The latest recorded enabled database-project run passed **40/40**, with zero skipped, including both disposable MySQL and non-Docker workbook/fake-persistence cases. Browser acceptance, separate-process concurrency and production-scale workload validation remain separate. Test results are local artifacts, not proof of deployed-provider behavior.
