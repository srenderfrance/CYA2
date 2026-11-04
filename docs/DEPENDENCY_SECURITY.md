# Dependency security

## Automated checks

`.github/workflows/dependency-security.yml` audits the active `Cya2.sln` on pushes and pull requests to main, every Monday at 08:23 UTC, and manual dispatch. The workflow runs `scripts/Test-DependencyVulnerabilities.ps1`, which:

- Restores with NuGet auditing enabled for all direct and transitive dependencies at every severity.
- Treats NU1900–NU1905 audit failures/findings as errors, including inability to retrieve advisory data.
- Queries NuGet.org for known vulnerabilities and fails when the JSON report contains findings, reports problems, or cannot be produced.
- Saves `TestResults/dependency-vulnerabilities.json` as a 30-day CI artifact.

No dependency versions are changed by an audit. No application secrets, Docker or live database are needed. A clean result means no *known, reported* NuGet vulnerabilities at the scan time, not that dependencies are risk-free.

From the repository root in PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./scripts/Test-DependencyVulnerabilities.ps1
```

This permits only that PowerShell process to execute the checked-in script; it does not change machine policy. PowerShell 7/Linux users can run `pwsh -NoProfile -File ./scripts/Test-DependencyVulnerabilities.ps1`.

The security workflow is independent of the existing deployment workflow, so its failure does **not** currently block deployment. The owner should review results before production promotion. Making it a deployment dependency or required branch check is a separate pipeline/policy change; no required reviews or second-person approval are introduced here.

## Update PRs

`.github/dependabot.yml` schedules weekly Monday updates at 09:00 UTC:

- NuGet: nine explicit active project directories under `src`, `tests`, and `tools`. Duplicate/obsolete source trees are excluded.
- NuGet minor/patch version updates are grouped to keep shared versions aligned; major upgrades remain separate. Security update PRs are not placed into that version-update group.
- GitHub Actions: action references in `.github/workflows`.
- No automatic merging, assigned reviewers or production approval requirements.

Dependabot update PRs are not a full GitHub Actions vulnerability scanner. Enable GitHub dependency graph and Dependabot alerts/security updates in repository **Settings → Advanced Security** (or **Code security**, depending on GitHub UI). Confirm availability for the repository's visibility and plan. These settings cannot be enabled by committing YAML alone. Alerts provide advisory-based coverage for supported ecosystems, including Actions where GitHub recognizes the dependencies; investigate alerts separately from the NuGet CI report.

The dependency-security workflow runs on update PRs, but it does not build/test them. Before merging, run the solution build/unit/web tests and Docker database tests where relevant. Existing build/deploy and database workflows run after a merge to main; they were not restructured by this change.

## Ownership and response

The sole repository maintainer owns triage, updates, testing and promotion. Suggested response targets, subject to organizational acceptance:

| Severity | Triage | Remediation or documented exception |
| --- | --- | --- |
| Critical | Within 1 day | Within 2 days |
| High | Within 2 days | Within 7 days |
| Moderate | Within 7 days | Within 30 days |
| Low | Weekly review | Within 90 days |

For each finding, record the advisory URL, affected direct/transitive dependency, reachability assessment, planned fix and target date in a GitHub issue. Fix a transitive dependency through its parent package where possible. Validate changes before merging; do not suppress a finding merely to turn CI green. An accepted exception needs rationale, compensating controls, an owner and an expiry/review date. This initial configuration has no automated exception/allowlist mechanism, so an accepted risk does not silently bypass a failed audit.

## Runtime and tool maintenance

Review these monthly, and immediately for critical/high security advisories:

- .NET 10 SDK in `global.json` and the existing deployment workflow's `DOTNET_VERSION`; keep them aligned. Dependabot NuGet updates do not maintain the SDK or Azure-hosted runtime.
- App Service runtime and hosted MySQL maintenance configuration: verify applied updates in the hosting environment.
- MySql.Data (including the separate migrator project), EPPlus, Radzen and all other NuGet dependencies: use Dependabot PRs, review release notes/licensing/API compatibility, and test imports, rollbacks and UI behavior.
- GitHub Actions releases and advisories: review update PRs and workflow permissions. A version-update check alone is not evidence that an action is vulnerability-free.
- Disposable MySQL test image in `tests/Cya2.Database.Tests/MySqlFixture.cs`: review supported patch versions manually; it is not managed by the NuGet/Actions configuration.

Repository configuration and a successful local audit do not prove GitHub alerts are enabled or scheduled runs have succeeded. Confirm those separately after pushing, and record workflow evidence before marking production-readiness acceptance complete.
