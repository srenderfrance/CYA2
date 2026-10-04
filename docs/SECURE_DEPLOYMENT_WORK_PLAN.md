# Secure Deployment Work Plan

This is a working plan derived from `SECURE_DEPLOYMENT_TODO.md`. It does not replace or modify the official checklist. The official checklist remains the source of truth for completion status.

Status and ordering are based on the repository review performed on 2026-10-01.

## Findings from the current documentation and code

- The baseline schema is version-controlled at `database/schema/001_InitialSchema.sql`.
- The schema documents MySQL 8.0+ and uses `utf8mb4_0900_ai_ci`.
- The schema defines tables, columns, indexes, foreign keys, rollback tables, and temporary-table-compatible import behavior.
- All current projects target `net10.0`; the Azure workflow installs the .NET 10 SDK family and publishes `src/Cya2.Web/cya2.csproj`.
- `docs/LOCAL_SETUP.md` now documents the relocated solution/project paths and all current projects targeting .NET 10.
- The repository does not contain approved fake donor data, accounting data, or Excel fixtures.
- Upload endpoints still allow unrestricted request sizes; uploaded filenames and content types are no longer written to operational logs.
- Import progress is held in memory and is not currently scoped to the user who created it.
- Import endpoints do not yet have the explicit antiforgery protections described in the official checklist.
- No persistent runtime schema DDL was found in the reviewed application paths; imports use session-scoped temporary tables.

## Work for the project owner

These items require synthetic business data, production access, credentials, or organizational decisions. They should not be guessed or completed by code changes alone.

### Synthetic development data

- [ ] Add a small, clearly fake donor dataset.
- [ ] Add a small, clearly fake accounting dataset.
- [ ] Add fake `.xlsx` import fixtures for donor and accounting workflows.
- [ ] Verify and document that all synthetic data contains no real donor or financial records.
- [ ] Decide where synthetic data and fixtures may safely be committed.

### Environment and policy decisions

- [x] Choose the exact .NET SDK version for local development and CI; add `global.json`.
- [ ] Confirm that all projects should remain on `net10.0`.
- [ ] Confirm the supported and tested MySQL version; the current schema minimum is MySQL 8.0+.
- [ ] Provide the local database host, port, database name, and schema setup procedure.
- [ ] Define the restricted application database account and approve its grants, including `CREATE TEMPORARY TABLES`.
- [ ] Confirm the Azure Web App, hosting runtime, deployment environment, and required settings.
- [ ] Confirm where the production MySQL database is hosted.
- [ ] Define the production connection-string and secret-manager policy without sharing secrets in the repository.
- [ ] Provide the production Google OAuth project, client, redirect URI, and allowed users.
- [ ] Decide the largest legitimate spreadsheet size for each import workflow.
- [x] Choose upload-preview and import-progress retention periods: 15 minutes in production and other deployed environments.
- [ ] Choose the authentication cookie lifetime and session-revalidation policy.
- [ ] Decide whether `Import:UseLocalInfile` is permitted in approved environments.
- [x] Confirm EPPlus licensing for the organization and deployment environments. Servant Partners is a legal US nonprofit, and CYA2 is used only for its internal operations; it is not sold, licensed, or provided to other organizations. Retain the existing Polyform Noncommercial notices and review licensing if the use changes.

### Credential and production verification

- [ ] Identify the historical database credential without copying it into documentation.
- [ ] Ask the database owner whether it is still valid and determine which host and database it reaches.
- [ ] Rotate any credential that may still work and confirm the replacement is not committed.
- [ ] Confirm the production runtime account has no schema-management permissions.
- [ ] Test a clean CI build and deployment.
- [ ] Verify the deployed application connects only to the intended database.
- [ ] Review production logs for donor, account, email, filename, and other personal data.

## Work for the coding assistant

Ordered from simplest and lowest risk to most complex and highest risk. User decisions above should be resolved before dependent implementation work begins.

### 1. Documentation and repository consistency

- [x] Correct `docs/LOCAL_SETUP.md` to use `Cya2.sln` and `src/Cya2.Web/cya2.csproj`.
- [x] Update `docs/LOCAL_SETUP.md` to state that all current projects target `net10.0`.
- [x] Document the current project list, launch profiles, and `https://localhost:7243/signin-google` callback.
- [x] Document current configuration keys and identify settings still requiring owner decisions.
- [x] Document schema setup account versus restricted runtime account responsibilities.
- [ ] Verify repository secret-scanning and protection configuration and document any gap.
- [x] Add a deployment checklist covering the current Azure workflow, hosting settings, database settings, and Google OAuth redirect configuration.

### 2. Low-risk tests and diagnostics review

- [ ] Add tests for authorization boundaries that do not require production credentials.
- [x] Review diagnostics for emails, uploaded filenames, workbook values, exception text, and validation details.
- [x] Replace personal values in logs and progress responses with row numbers, field names, categories, counts, and correlation identifiers.
- [x] Add structured logging or redaction rules for sensitive fields.
- [x] Add tests proving sensitive values are not emitted in logs or progress responses.

### 3. Upload validation and resource limits

- [ ] Add an explicit maximum request and file size after the owner chooses legitimate spreadsheet limits.
- [ ] Accept only the expected `.xlsx` extension and content type.
- [ ] Validate the workbook signature and ensure EPPlus can open it before processing.
- [ ] Reject oversized, empty, wrong-type, corrupt, and invalid workbooks with controlled responses.
- [ ] Avoid unrestricted upload streams and memory usage during validation and processing.
- [ ] Add tests for valid, oversized, wrong-type, corrupt, and malformed files.

### 4. Import retention and cleanup

- [x] Add expiration timestamps to upload previews and completed progress records.
- [x] Remove expired previews and completed progress records using the owner-approved 15-minute retention period.
- [x] Ensure cleanup occurs when imports fail, are abandoned, or complete.
- [x] Add tests proving expired previews are removed and active previews are retained.
- [x] Document the retention policy and cleanup behavior.

Production policy and implementation note: previews and progress records are held in memory for a maximum of 15 minutes in production and other deployed environments. A hosted cleanup service runs every minute and removes records older than 15 minutes. Terminal progress records are also removed when the progress dialog closes; active imports remain protected until completion or timeout.

### 5. Import ownership and authorization

- [ ] Record the authenticated admin ID when creating a preview and progress record.
- [ ] Require the requesting admin to own the preview or progress record.
- [ ] Check current admin permission when confirming an import.
- [ ] Prevent another signed-in user from viewing progress by ID alone.
- [ ] Prevent another signed-in user from confirming an import by ID alone.
- [ ] Return controlled unauthorized or forbidden responses.
- [ ] Add focused tests for regular, unknown, removed, and revoked users.

### 6. Authentication revocation and session security

- [ ] Apply the owner-approved authentication cookie lifetime.
- [ ] Revalidate the current user and admin role for sensitive operations.
- [ ] Ensure removed users and revoked administrators cannot continue protected operations.
- [ ] Invalidate or refresh affected sessions where appropriate.
- [ ] Add tests for removed users, revoked administrators, and server-side authorization enforcement.

### 7. Database permissions and schema evolution

- [ ] Move future column alterations and other structural changes into ordered version-controlled scripts.
- [ ] Add a migration/setup runner that records applied scripts and handles schema drift safely.
- [ ] Remove any discovered runtime schema modification from normal application paths.
- [ ] Verify limited-mode startup and normal operation with the restricted runtime account.
- [ ] Add checks proving the runtime account cannot create, alter, or drop persistent schema objects.
- [ ] Document and validate the separation between schema-management and application permissions.

### 8. Antiforgery and end-to-end verification

- [ ] Identify every endpoint that changes data or starts an import.
- [ ] Add explicit antiforgery protection to import preview and confirmation endpoints.
- [ ] Preserve and verify antiforgery protection on donor export.
- [ ] Confirm the Blazor upload interface sends the antiforgery token.
- [ ] Test valid requests, missing tokens, and invalid tokens end to end.
- [ ] Complete a clean-checkout setup, CI build, deployment, database connection, import, rollback, and authorization verification.

## Relationship to the official checklist

- Update checkboxes only in `docs/SECURE_DEPLOYMENT_TODO.md` when an official item is actually complete.
- Use this file for ownership, sequencing, investigation notes, and implementation planning.
- Do not mark a user-owned item complete based solely on a code change or documentation assumption.
