# Production Readiness TODO

Use this checklist with `SECURE_DEPLOYMENT_TODO.md` before enabling the application for real donor and financial data. It records remaining production work identified from the repository as of the current `main` branch.

## How to use this checklist

- **P0** blocks production handling of real data.
- **P1** should be complete before or immediately after launch, with an owner and due date.
- **P2** improves operability and long-term maintainability.
- Items marked **Verify in environment** cannot be proven from source code and must be evidenced in the selected Azure, MySQL, Google Cloud, and GitHub environments.
- Do not mark an item complete based only on local testing. Record the date, responsible owner, and evidence in the deployment runbook or change record.

## P0 — Security and identity

- [ ] **Verify in environment:** Configure production secrets only through the Azure Web App secret/configuration store or an approved secret manager. Do not place connection strings, OAuth credentials, bootstrap values, or real identities in files, workflow variables, or deployment packages.
  - Acceptance: a configuration inventory identifies the secret owner, rotation process, and storage location; a production startup succeeds without tracked secret values.
- [ ] **Verify in environment:** Configure the exact production Google OAuth redirect URI (`https://<production-host>/signin-google`) and restrict the OAuth client to approved redirect URIs and organizational Google accounts as appropriate.
  - Acceptance: sign-in, sign-out, rejected user, and revoked-admin journeys pass on the production host.
- [ ] Remove `InitialAdminBootstrap__Email` and `InitialAdminBootstrap__Name` immediately after the first administrator is provisioned.
  - Acceptance: the values are absent from the deployment configuration, and a second unknown user cannot bootstrap administration.
- [ ] **Verify in environment:** Enable GitHub secret scanning, push protection, Dependabot alerts, and branch protection for `main`.
  - Acceptance: protected changes require successful build/test/security checks and secret scanning blocks a test credential pattern.
- [ ] Add an automated secret scan to pull-request and main-branch CI, with a documented response procedure for detected credentials.
  - Acceptance: the workflow fails on a controlled test finding without printing a secret value.
- [ ] Review external JavaScript/CDN dependencies used by the Content Security Policy; pin versions and, where supported, add Subresource Integrity or self-host essential assets.
  - Acceptance: every production script/style origin is approved, versioned, and covered by the CSP.
- [ ] **Verify in environment:** Confirm Azure Front Door/App Service/proxy forwarding configuration preserves HTTPS and trusted client IPs. Update rate limiting to use forwarded client IPs only from trusted proxies.
  - Acceptance: requests through the production proxy are rate-limited per real client without allowing spoofed forwarding headers.
- [ ] Perform an authenticated external security test covering OAuth callback handling, authorization boundaries, direct API endpoints, upload validation, headers/CSP, and open redirects.
  - Acceptance: findings are remediated or formally risk-accepted before launch.

## P0 — Database, data protection, and recovery

- [ ] **Verify in environment:** Create separate MySQL migration and runtime accounts. Grant the runtime account only the minimum DML and temporary-table permissions required by the application; do not grant schema-management permissions.
  - Acceptance: migrations run only with the setup account and the application operates successfully with the restricted runtime account.
- [ ] **Verify in environment:** Require encrypted MySQL transport and validate the server certificate/CA configuration.
  - Acceptance: the production connection string and MySQL server reject unencrypted connections.
- [ ] Define the donor-data backup, retention, encryption, geographic storage, and access-control policy with the organization.
  - Acceptance: a named system creates automated backups, the policy specifies retention and restoration objectives, and access is restricted and audited.
- [ ] Execute and document a full backup restore into an isolated environment, including application startup and data reconciliation.
  - Acceptance: restore meets the agreed recovery-time and recovery-point objectives and does not overwrite production.
- [ ] Add a production migration runbook: backup/restore checkpoint, migration command, locking behavior, verification query, rollback/forward-fix decision, and communication plan.
  - Acceptance: an approved dry run succeeds against a production-like copy before each schema release.
- [ ] Document import reconciliation and rollback procedures for real donor and accounting uploads.
  - Acceptance: staff can verify source row counts/totals, identify rejected rows without personal-data leakage, and use the approved recovery route.
- [ ] Establish data retention and deletion rules for donor/contact projections, exports, database backups, and platform logs.
  - Acceptance: policy owner approves documented retention periods and deletion procedures.

## P0 — Hosting, release management, and availability

- [ ] **Verify in environment:** Confirm the supported .NET 10 runtime, Azure App Service plan/SKU, region, operating-system image, and MySQL hosting/version.
  - Acceptance: these values are recorded in a production architecture/runbook document with support dates and owners.
- [ ] Split CI validation from production deployment. The current workflow deploys pushes to `main` using an environment named `Development`; add a production environment with required reviewers and explicit promotion.
  - Acceptance: pull requests build and test without deployment; production deployment requires approval and targets only the production environment.
- [ ] Add `dotnet test` for the application and web test projects to CI, publish TRX results, and fail the workflow when tests fail.
  - Acceptance: CI reports test results for every pull request and main build.
- [ ] Add dependency vulnerability scanning and an update cadence for NuGet, GitHub Actions, the .NET runtime, MySQL provider, EPPlus, and Radzen.
  - Acceptance: advisory findings have an owner, severity SLA, and documented remediation/exception.
- [ ] Provide separate readiness and liveness probes. The current authenticated `/health` endpoint uses a deferred database check; define a non-sensitive platform probe and a protected dependency-aware readiness check.
  - Acceptance: Azure health checks can detect unhealthy instances without exposing database details publicly.
- [ ] Verify scale-out behavior. The application currently holds selections, import previews/progress, queues, and caches in process memory.
  - Acceptance: deploy a single instance with a documented capacity limit, or move state/coordination to shared storage and validate multiple instances.
- [ ] Configure graceful shutdown, restart, and maintenance procedures for active Blazor circuits and queued imports.
  - Acceptance: a planned restart does not leave imports in an ambiguous state, and staff know how to recover a stopped import.

## P1 — Observability and incident response

- [ ] **Verify in environment:** Send structured application, platform, and MySQL logs to an access-controlled central sink with retention consistent with the donor-data policy.
  - Acceptance: operators can correlate a request via trace ID without logs containing donor, account, intern, spreadsheet-cell, OAuth-token, or connection-string values.
- [ ] Run a production log review after representative sign-in, import, export, authorization-denial, and database-outage scenarios.
  - Acceptance: documented evidence confirms the no-personal-data logging requirements remain true in the deployed environment.
- [ ] Add availability, failed-login, authorization-denial, import-failure, queue-delay, database-connectivity, and cache-warmup metrics with actionable alerts.
  - Acceptance: alerts reach named responders and a test alert is acknowledged.
- [ ] Create an incident runbook for database unavailability, OAuth failure, suspected credential exposure, unauthorized access, failed import, and data-restoration requests.
  - Acceptance: owner, escalation contact, containment steps, and recovery validation are recorded and exercised.
- [ ] Record an audit trail for privileged administrative actions and import confirmations without logging donor values.
  - Acceptance: the organization can determine who performed each privileged action, when, and its non-sensitive outcome.

## P1 — Quality assurance and functional validation

- [ ] Run a production-like end-to-end test using only approved synthetic fixtures: Google sign-in, initial-admin removal, user/account management, donation import, accounting import, donor export, account revocation, and sign-out/session expiry.
  - Acceptance: expected results, counts, and authorization failures are recorded before real data is loaded.
- [ ] Add a real Google OAuth callback integration test in a controlled non-production configuration, or document the manual regression test and release gate.
  - Acceptance: callback success, unknown-user rejection, invalid state/failure handling, and redirect behavior are verified.
- [ ] Add database integration tests against an ephemeral or isolated MySQL instance for migrations, repository queries, import transaction rollback, and restricted-runtime permissions.
  - Acceptance: CI or a scheduled pipeline runs them without production credentials.
- [ ] Establish test coverage goals for authorization, imports, cache invalidation, and financial calculations; publish results in CI.
  - Acceptance: critical paths have named tests and regression failures block release.
- [ ] Document a user-acceptance checklist for staff, including reporting totals, donor deduplication, intern-account behavior, separate/merged account rules, and export authorization.
  - Acceptance: designated staff sign off against representative synthetic or approved non-production data.

## P1 — Privacy, compliance, and governance

- [ ] Identify the organization’s applicable privacy, donor-data, financial-record, and breach-notification obligations with a qualified owner/advisor.
  - Acceptance: the approved data classification, lawful-use/notice, retention, access, deletion, and incident-response requirements are documented.
- [ ] Define production access governance for administrators, viewers, regular users, database operators, Azure operators, and GitHub maintainers.
  - Acceptance: least-privilege roles, approvers, periodic access review cadence, and offboarding process are documented.
- [ ] Define a process for donor data-subject requests and correction/deletion requirements, including effects on projections, exports, backups, and accounting records.
  - Acceptance: requests have an owner, response target, and documented exceptions for required financial retention.

## P2 — Performance, capacity, and maintainability

- [ ] Perform load and soak testing with synthetic data at the expected donor, donation, accounting-row, concurrent-user, and workbook-size volumes.
  - Acceptance: response-time, memory, CPU, database-connection, and import-duration targets are recorded; bottlenecks have remediation or accepted limits.
- [ ] Capacity-test the configured MySQL pool (`MaximumPoolSize=15`) and rate limits under normal pages, exports, cache warmup, and imports.
  - Acceptance: no pool exhaustion, unbounded queueing, or unacceptable contention occurs at target load.
- [ ] Establish cache operational limits and invalidation verification. The app uses in-memory caches and background warmups.
  - Acceptance: cache memory budget, warmup time, stale-data behavior, invalidation checks, and restart behavior are documented and monitored.
- [ ] Resolve documentation drift before launch. `docs/LOCAL_SETUP.md` still states that synthetic fixtures do not exist, while fixture files and `database/fixtures/SYNTHETIC_DATA_README.md` are present.
  - Acceptance: setup, fixture, deployment, and security documents describe one consistent, current workflow.
- [ ] Replace draft-only setup/deployment guidance with an owner-approved operations runbook and keep it version controlled.
  - Acceptance: the runbook covers provisioning, configuration, migration, rollback, smoke tests, monitoring, backups, access review, and incident response.
- [ ] Define a release/versioning and rollback policy for application packages and database migrations.
  - Acceptance: each release has an immutable artifact, release notes, tested rollback/forward-fix path, and change approver.

## Launch gate

Do not load real donor or financial data until all P0 items are complete and P1 exceptions have a named owner, expiration date, and explicit risk acceptance. Perform a final deployment smoke test, confirm backups and alerts, verify production logging redaction, and capture evidence of the approved launch decision.
