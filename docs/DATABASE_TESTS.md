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

## Coverage

- Fresh schema initialization through the built DatabaseMigrator executable, application of repository SQL migrations, repeat-run idempotency, and baseline checksum rejection.
- Runtime identity limited to SELECT, INSERT, UPDATE, DELETE and CREATE TEMPORARY TABLES; persistent CREATE/ALTER/DROP and GRANT denied.
- Canonical donation writes, donor deduplication, stale-contact removal, retained DateCreated, and donation repository readback.
- Contact freshness: unchanged timestamps, exact/casing-only retained-value changes, new values with null DateModified, removed values, rollback-restored timestamps and untouched donors outside the upload. These are opt-in SQL regressions, not passing local SQL evidence until an enabled run succeeds.
- Injected contact-insert failure after donor/donation writes verifies the transaction leaves no partial persistent data.
- Donation backup/replacement/rollback restores donations and contacts and removes new orphan donors.
- Atomic accounting staging/publication, full backup/replacement/rollback including rows before the replacement window.
- Publication failures preserve existing rows, contacts and snapshots; rollback restores touched donors whose only prior gifts predate the replacement window; pre-cancelled replacement preserves data.
- Missing donation snapshot produces a safe failed rollback without deleting data.

`ImportValidationTests` also runs without Docker: 11 cases exercise the actual processors with synthetic in-memory Excel workbooks and fake persistence. These cover invalid/empty rejection, valid publication and domain invalidation, pre-cancellation, uncertain-outcome invalidation and shared write serialization. The 11 MySQL cases remain opt-in. Accounting no longer uses local-infile loading or partial live-write fallbacks. See [the import safety contract](IMPORT_SAFETY.md), including migration 003.

These tests do not exercise OAuth or Blazor UI uploads, or prove hosting configuration, production workload performance, every migration error path, lost commit acknowledgements, or account/subaccount reporting semantics. Contact DateModified behavior now has dedicated SQL coverage but still requires an enabled MySQL run.

## CI

`.github/workflows/mysql-integration-tests.yml` explicitly enables Docker tests on pushes to main and manual dispatch, uploads TRX results and removes interrupted containers. No secrets or external MySQL service are needed. It has no deployment or approval steps. The existing deployment workflow is unchanged: its ordinary solution run skips the opt-in database tests, so **the separate MySQL job does not currently gate deployment**. Review its result independently before promotion; making deployment depend on it is a separate pipeline change.

Local validation during initial implementation could verify compilation, discovery and skipped behavior only: Docker CLI was not installed. An enabled local/CI run is required before claiming MySQL behavior has passed.
