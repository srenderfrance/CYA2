# Secure Setup, Deployment, and Data Protection TODO

> **Historical implementation checklist, reconciled 2026-11-04.** Use [Production review](PRODUCTION_REVIEW.md) as the current staged backlog. Checked historical implementation work does not prove current production configuration; unsupported session/diagnostic guarantees below have been reopened. The original review recorded 191 tests passed and seven Docker cases skipped; a later enabled database-project run passed 40/40 with none skipped. Environment acceptance remains pending.

Use this checklist to prepare the application for safe local setup, deployment, and handling of donor data.

## First: Make It Possible to Set Up and Deploy Safely

### 1. Create a repeatable database setup

- [x] Add version-controlled database scripts or migrations.
- [x] Define all required tables.
- [x] Define all required columns and data types.
- [x] Define all required indexes.
- [x] Define all required foreign keys and other constraints.
- [x] Ensure the setup can be run repeatedly or safely from a clean database.
- [x] Add a small, clearly fake donor dataset for development and testing.
- [x] Add a small, clearly fake accounting dataset for development and testing.
- [ ] Add approved synthetic donor/QuickBooks Excel upload fixtures and end-to-end import coverage (A13/B07). SQL fixtures and repository DTO tests exist but do not validate workbook parsing.
- [x] Verify that the fake data contains no real donor or financial records.

### 2. Write local setup instructions

- [x] Document the required .NET SDK version.
- [x] Document the supported MySQL version.
- [x] Document how to create the local database.
- [x] Document how to apply the database scripts or migrations.
- [x] Document how to configure the application connection string.
- [x] Document how to configure Google OAuth login.
- [x] Document one-time initial administrator bootstrap through secret-backed Google-email matching.
- [x] List every required configuration setting.
- [x] Include fake example values only.
- [x] State clearly where developers must provide local secrets.
- [x] Require removal of initial-administrator bootstrap secrets after first sign-in.
- [x] Confirm that secrets are excluded from source control. Current tracked content and rewritten reachable history were scanned with Gitleaks during this audit with no leaks found. Root ignore rules exclude common local credential files; application secrets use user secrets or environment variables.

### 3. Deal with the database password in Git history

- [x] Identify the connection string and password present in Git history. A Gitleaks scan identified a historical Aiven connection-string credential in obsolete configuration files without recording the secret value in this document.
- [x] Ask the database owner whether the historical password still works. The database owner confirmed the exposed password was rotated previously.
- [x] Determine which database and host the credential reaches. The historical credential was an obsolete Aiven database connection string; no host or credential value is recorded here.
- [x] If the credential still works, rotate it immediately. The database owner confirmed the historical password had already been rotated.
- [x] Confirm that replacement credentials are not committed to the repository. Gitleaks scanned the rewritten reachable repository history during this audit and found no leaks.
- [x] Remove active secrets from current tracked files. Current tracked content and rewritten reachable history passed the Gitleaks scan during this audit.
- [ ] Verify current secret scanning/push protection and repository policy settings (B10). Historical Gitleaks evidence does not establish GitHub settings.
- [x] Record the rotation decision without recording the password. The historical credential was rotated before this audit; leaked configuration paths were removed from reachable history and a fresh clone passed Gitleaks verification.

### 4. Make deployment reproducible

- [x] Confirm the target .NET version for every project.
- [x] Align the web project target framework with the GitHub Actions SDK installation.
- [x] Resolve the current .NET 10 project versus .NET 8 workflow mismatch.
- [x] Confirm package versions are compatible with the selected target framework.
- [x] Confirm the build SDK version used by CI.
- [ ] Confirm the hosting runtime version.
- [ ] Confirm where the web application will be hosted.
- [ ] Confirm where the MySQL database will be hosted.
- [x] Document required host environment settings in [Azure setup](AZURE_SETUP.md); verify actual settings in B02/B03.
- [x] Document production connection-string configuration using a secret manager in Azure setup; deployment proof remains B02/B03.
- [x] Document production Google OAuth redirect format in Azure setup; verify the exact deployed URI in B02.
- [x] Test a clean CI build and deployment from the repository.
- [x] Verify the deployed application can connect to the intended database.

## Next: Protect Imports and Access to Donor Data

### 5. Put firm limits on uploads

- [x] Determine the largest legitimate spreadsheet currently used. Review of files used over the last 20+ years found that the largest file was no more than 2,600 KB as of September 2026.
- [x] Set an explicit maximum upload size based on that limit. The limit is 5,000 KB (5,120,000 bytes), providing headroom above the 2,600 KB largest file identified through September 2026.
- [x] Accept only the expected `.xlsx` file type.
- [x] Validate the file signature and extension.
- [x] Validate that the file can be opened as a valid workbook. Upload validation opens the OOXML package with EPPlus and requires at least one worksheet before storing the preview.
- [x] Return a controlled validation error for invalid workbooks.
- [x] Reject oversized files before processing them.
- [x] Avoid keeping unrestricted uploaded files in memory. Uploads are bounded to 5,000 KB, preview validation is limited to two simultaneous operations, and retained previews are limited to two previews/10 MB in aggregate. Because this is a single-location monolith with unusually low concurrent upload activity, bounded in-memory preview storage is acceptable; temporary-file or distributed storage is not currently required.
- [x] Add tests for oversized, wrong-type, corrupt, and valid OOXML package uploads.

### 6. Clean up import data

- [x] Add expiration timestamps to upload previews.
- [x] Add expiration timestamps to completed progress records.
- [x] Remove unused upload previews after a short documented period.
- [x] Remove completed progress records after a short documented period.
- [x] Ensure cleanup runs even when an import fails or is abandoned.
- [x] Document the retention period.
- [x] Add tests proving expired records are removed.

Production policy: upload previews and in-memory import progress records are retained for a maximum of 15 minutes. Cleanup runs every minute; terminal progress records may also be removed when the progress dialog closes. This policy applies to production as well as local and deployed non-production environments.

### 7. Tie imports to the admin who started them

- [x] Record the authenticated admin ID when creating an import preview.
- [x] Record the authenticated admin ID when creating an import progress record.
- [x] Check the current admin permission when confirming an import.
- [x] Require the requesting admin to own the preview or progress record.
- [x] Prevent another signed-in user from viewing import progress by ID alone.
- [x] Prevent another signed-in user from confirming an import by ID alone.
- [x] Return a controlled authorization response for unauthorized access.
- [x] Add tests for cross-user preview and progress access.

### 8. Handle revoked access promptly

- [x] Shorten the authentication cookie lifetime from the current 24-hour period. The cookie now has a four-hour absolute lifetime.
- [x] Decide on an appropriate cookie lifetime for the application. The policy is four hours, non-sliding, and non-persistent.
- [ ] Verify route/account/date resume through reauthentication (A08). An interactive monitor now handles it; browser end-to-end behavior remains to be verified.
- [x] Revalidate the current database user and role before account selection and account-specific dashboard, donation, expense, and export reads; stale authentication claims cannot grant all-account access.
- [x] Revalidate regular-user account membership before account selection and account-specific reads; removed `AccountsUsers` links are denied even when prior data was cached.
- [x] Ensure a removed user cannot use protected account data or imports after access is removed. Missing database users are treated as revoked; existing import ownership/current-admin checks remain enforced.
- [ ] Verify a revoked Admin cannot retain a stale Admin shell/session (A08). The interactive monitor and cookie validation check the current role; browser acceptance remains pending.
- [ ] Verify active-circuit revalidation independent of cookie expiry (A08); an interactive monitor now checks initially and every minute.
- [ ] Verify session refresh/resume clearing on deletion/demotion and transient database failures (A08/B02).
- [x] Add service-level removed-user/current-role/account-membership tests. Historical 83-test count is superseded by the review's current validation; this does not prove browser-circuit revocation.

Sensitive account reads and selections revalidate immediately before use. The monitor targets one-minute revalidation while connected; browser behavior and recovery remain to be verified (A08).

### 9. Separate database setup permissions from everyday permissions

- [x] Audit schema changes performed during normal application operations; no persistent runtime DDL was found.
- [x] Move backup-table creation into the database setup process.
- [x] Move column alterations into the database setup process; the runtime audit found no column alterations requiring migration.
- [x] Move all other runtime schema changes into version-controlled migrations; future changes use the standalone runner and `database/migrations`.
- [x] Remove runtime schema modification code from normal application paths; only session-scoped temporary import tables remain.
- [ ] Create a restricted application database user in the MySQL deployment environment.
- [ ] Grant only required DML and CREATE TEMPORARY TABLES permissions; prove staging transaction/permission behavior without persistent DDL grants (A02/B03).
- [x] Keep schema-management permissions in a separate setup or migration account.
- [ ] Verify the application starts and operates with restricted permissions.
- [x] Document both database users and their intended responsibilities.

### 10. Remove personal values from diagnostics

- [x] Stop logging sample values from the `Honor/Memorial Name` spreadsheet column.
- [ ] Recheck general logs for all personal identifiers (A09/B08), including the current user-name diagnostic and email-based user-ID fallback. Historical email cleanup is not blanket log safety.
- [x] Review logs for uploaded filenames.
- [ ] Complete exception/logging review (A09/B08). Raw exceptions and a user-name diagnostic remain; historical cleanup was not comprehensive.
- [x] Review spreadsheet validation errors before they are returned in progress responses.
- [x] Remove personal values from progress responses where possible.
- [x] Prefer row numbers, field names, error categories, and counts for diagnostics.
- [x] Add redaction or structured logging rules for sensitive fields.
- [ ] Verify production logs do not contain donor or account personal data.

## Verify the Protections

### 11. Add focused authorization tests

- [x] Verify a regular user cannot import through tested service/API authorization paths; Application and Web role-filter tests exist. Actual browser workbook processing remains B07.
- [x] Verify a regular user cannot manage users. `UserManagementService` now revalidates the current database user and Admin role before administrative reads and mutations; tests verify denied users cannot update, create, delete, or change account links.
- [x] Verify a regular user cannot view another user’s import progress. Ownership filtering is enforced by the server-side progress service/controller path.
- [x] Verify a user cannot export donors from an unauthorized account. Unauthorized export tests also verify that donor reads are not invoked.
- [ ] Verify an unknown Google user is rejected. Application authorization covers unknown database users; full Google callback rejection requires web integration configuration.
- [x] Verify a removed user cannot access protected operations. Deleted-user authorization and export-denial regressions pass.
- [x] Verify current database role revalidation and owner-bound import authorization in tests. Web role-filter tests also exist; in-flight queued work and active-circuit revocation require A08/A13/B07 acceptance.
- [x] Verify a revoked administrator cannot perform administrative changes. Regression coverage confirms that database-role revocation blocks user updates, creation, deletion, and account-access grants/revocations without invoking repository mutations.
- [x] Verify authorization checks are enforced server-side, not only in the UI. Application services revalidate database user, role, and account membership before protected reads/import operations.

Focused service tests run in `tests/Cya2.Application.Tests`; `tests/Cya2.Web.Tests` now includes a real WebApplicationFactory pipeline host, role-filter, upload/antiforgery, cookie-culture and health tests. A real Google callback/browser circuit session still requires B02/B07 validation.

### 12. Apply antiforgery protection consistently

- [x] Identify every endpoint that changes data or starts an import. The HTTP state-changing endpoints are `POST /api/upload/donations/preview`, `POST /api/upload/donations/confirm`, `POST /api/upload/accounting/preview`, `POST /api/upload/accounting/confirm`, and `POST /api/donors/export-data`. `GET /logout` changes authentication state and `GET /api/antiforgery-token` issues a token; `GET /api/import-progress/{id}`, `GET /api/login`, and `GET /health` are read/challenge/status endpoints. The current Admin Blazor UI does not call the upload controller: its `InputFile` handlers and confirmation actions invoke import services directly through the authenticated interactive server circuit. Other direct Admin circuit mutations include account import, user/account management, fund and subfund changes, rollback, and donation recategorization; these require circuit and application authorization rather than a separate browser form token.
- [x] Add explicit antiforgery protection to import endpoints. All four `UploadController` POST actions validate `IAntiforgery` before reading upload or confirmation data and return a controlled `400` response on failure. No external or legacy clients use these endpoints; they remain protected as an exposed API boundary even though the current Admin UI uses the direct Blazor circuit path.
- [x] Preserve antiforgery protection on donor export. `DonorExportController.ExportData` explicitly validates the request through `IAntiforgery` before parsing the request or reading donor data.
- [x] Confirm the actual upload interface sends the antiforgery token. The current interface is `Admin.razor` and does not call the upload API; `InputFile` handlers invoke the authorized import application service directly in the Blazor Server circuit. The unused API client contract is the authenticated `/api/antiforgery-token` endpoint plus the `RequestVerificationToken` header.
- [x] Test valid requests with the upload interface. Controller-level tests in `tests/Cya2.Web.Tests/UploadAntiforgeryTests.cs` verify valid-token preview and confirmation requests reach the import service for both donations and accounting; the direct Blazor UI path remains covered by application-service authorization tests.
- [x] Test requests with a missing antiforgery token. Tests cover all four upload actions.
- [x] Test requests with an invalid antiforgery token. The shared controller guard rejects invalid tokens before import processing; missing-token tests cover the same rejection path.
- [x] Confirm rejected requests do not change data or create import records. Recording import-service tests verify no preview or import call occurs when antiforgery validation fails.

Endpoint inventory note: API antiforgery tests do not exercise Admin.razor's direct interactive-service upload path. The current Web test project does use WebApplicationFactory with monitor/auth replacements; processor/workbook and browser-circuit acceptance remain A13/B07. Historical focused counts are not the current suite total.

## Completion Review

- [ ] All checklist items have an owner.
- [ ] All checklist items have an issue, pull request, or documented decision.
- [ ] Local setup works from a clean checkout.
- [ ] CI builds the same target framework and package set used for deployment.
- [ ] Deployment has been tested against the intended hosting environments.
- [ ] Database credentials have been rotated or confirmed inactive.
- [ ] Import limits, retention, authorization, and antiforgery tests pass.
- [ ] Production logging has been reviewed for personal data.
- [ ] The database application user has no schema-management permissions.
