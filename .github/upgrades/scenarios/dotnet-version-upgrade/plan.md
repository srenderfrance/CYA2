# .NET Version Upgrade Plan

## Overview

**Target**: Upgrade the complete `cya2.0.sln` solution to .NET 10 while preserving the existing .NET 10 Blazor application and validating all projects together.
**Scope**: Six SDK-style projects: five `net8.0` projects (`Cya2.Core`, `Cya2.Shared`, `Cya2.Application`, `Cya2.Infrastructure`, and `Cya2.Application.Tests`) plus the existing `net10.0` Blazor web application.

### Selected Strategy
**All-At-Once** — All projects upgraded simultaneously in a single operation.
**Rationale**: The assessment covers six modern SDK-style projects, reports all 20 packages as compatible, identifies only three source-incompatible API findings, and shows a straightforward dependency structure.

## Tasks

### 01-prerequisites: Verify .NET 10 upgrade prerequisites

Verify the installed .NET 10 SDK and any `global.json` constraints for the complete solution before changing project files. Confirm the solution and all six projects are the intended scope, and capture the baseline build/test state needed to distinguish upgrade changes from pre-existing issues.

**Done when**: The .NET 10 SDK is available, SDK selection is compatible, the complete solution scope is confirmed, and baseline validation results are recorded.

---

### 02-upgrade-solution: Upgrade all solution projects to .NET 10

Upgrade all five `net8.0` projects to `net10.0` in the solution while retaining the existing Blazor web application's `net10.0` target. Restore all package references without unnecessary version changes because the assessment reports all packages compatible, then resolve the three source-incompatible `TimeSpan.FromSeconds`/`TimeSpan.FromMinutes` findings inline. Research the affected projects and preserve existing Blazor, Dapper, MySQL, Radzen, caching, database startup probe, limited-mode, donor projection, and tooltip behavior.

**Done when**: Every project in `cya2.0.sln` targets `net10.0`, restore succeeds, all source incompatibilities are resolved, and the complete solution builds with zero errors and zero warnings.

---

### 03-solution-validation: Validate the upgraded solution

Run the full solution build and all discovered tests after the atomic upgrade. Review the final project and package state, confirm no dependency conflicts or security issues were introduced, and document any deferred recommendations or runtime concerns without narrowing the solution scope.

**Done when**: The full solution builds warning-free, all tests pass, all six projects remain included in the solution, and final upgrade results are documented.
