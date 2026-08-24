# UI Cache Baseline Test

## Run

From the repository root, run:

```powershell
.\cya2.0\scripts\Measure-UiCacheBaseline.ps1 `
	-LogPath .\baseline.log `
	-ResultsPath .\cya2.0\scripts\ui-cache-baseline.csv `
	-Baseline after `
	-Commit optimize-ui-cache-state
```

The script is intentionally generic. Before running it, replace the placeholders in `$TestData` in `Measure-UiCacheBaseline.ps1` with the Admin user, regular user, default account, and six test accounts.

## Procedure

1. Start the application with the same build configuration for every run.
2. Capture the application output into the log file passed through `-LogPath`.
3. Run each listed scenario five times.
4. For cold scenarios, restart the application before each run.
5. Use one browser circuit for warm navigation and account switching.
6. Use two authenticated circuits for import and rollback recovery.
7. Record one row per run in `ui-cache-baseline.csv`.
8. Compare medians between `before` and `after` baseline values.

The script prints the scenario checklist and exports parsed correlation/cache metrics to a `.parsed-log.csv` file when matching logs are available. Because log formats can evolve, review parsed counts against the original log before using them for conclusions.

## Required test data

- Admin user identity
- Regular user identity
- Admin default account, or `none`
- Six valid fundraising accounts: A through F
- A reversible import file or test import dataset
- A rollback target that is safe to restore

Do not use production data for destructive import, update, or rollback testing unless a verified backup and recovery procedure is in place.
