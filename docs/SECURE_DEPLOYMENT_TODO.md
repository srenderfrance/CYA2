# Secure Setup, Deployment, and Data Protection TODO

Use this checklist to prepare the application for safe local setup, deployment, and handling of donor data.

## First: Make It Possible to Set Up and Deploy Safely

### 1. Create a repeatable database setup

- [x] Add version-controlled database scripts or migrations.
- [x] Define all required tables.
- [x] Define all required columns and data types.
- [x] Define all required indexes.
- [x] Define all required foreign keys and other constraints.
- [x] Ensure the setup can be run repeatedly or safely from a clean database.
- [ ] Add a small, clearly fake donor dataset for development and testing.
- [ ] Add a small, clearly fake accounting dataset for development and testing.
- [ ] Add fake import files or fixtures that can be used to test the import workflow.
- [ ] Verify that the fake data contains no real donor or financial records.

### 2. Write local setup instructions

- [x] Document the required .NET SDK version.
- [x] Document the supported MySQL version.
- [x] Document how to create the local database.
- [x] Document how to apply the database scripts or migrations.
- [x] Document how to configure the application connection string.
- [x] Document how to configure Google OAuth login.
- [ ] Document how to add a matching local user to the `Users` table.
- [x] List every required configuration setting.
- [x] Include fake example values only.
- [x] State clearly where developers must provide local secrets.
- [ ] Confirm that secrets are excluded from source control.

### 3. Deal with the database password in Git history

- [ ] Identify the connection string and password present in Git history.
- [ ] Ask the database owner whether the historical password still works.
- [ ] Determine which database and host the credential reaches.
- [ ] If the credential still works, rotate it immediately.
- [ ] Confirm that replacement credentials are not committed to the repository.
- [ ] Remove active secrets from current tracked files.
- [ ] Add or verify secret scanning and repository protection rules.
- [ ] Record the rotation decision without recording the password.

### 4. Make deployment reproducible

- [x] Confirm the target .NET version for every project.
- [x] Align the web project target framework with the GitHub Actions SDK installation.
- [x] Resolve the current .NET 10 project versus .NET 8 workflow mismatch.
- [x] Confirm package versions are compatible with the selected target framework.
- [x] Confirm the build SDK version used by CI.
- [ ] Confirm the hosting runtime version.
- [ ] Confirm where the web application will be hosted.
- [ ] Confirm where the MySQL database will be hosted.
- [ ] Document required host environment settings.
- [ ] Document production connection-string configuration using a secret manager.
- [ ] Document the production Google OAuth redirect URL.
- [ ] Test a clean CI build and deployment from the repository.
- [ ] Verify the deployed application can connect to the intended database.

## Next: Protect Imports and Access to Donor Data

### 5. Put firm limits on uploads

- [ ] Determine the largest legitimate spreadsheet currently used.
- [ ] Set an explicit maximum upload size based on that limit.
- [ ] Accept only the expected `.xlsx` file type.
- [ ] Validate the file signature and extension.
- [ ] Validate that the file can be opened as a valid workbook.
- [ ] Return a controlled validation error for invalid workbooks.
- [ ] Reject oversized files before processing them.
- [ ] Avoid keeping unrestricted uploaded files in memory.
- [ ] Add tests for oversized, wrong-type, corrupt, and valid files.

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

- [ ] Record the authenticated admin ID when creating an import preview.
- [ ] Record the authenticated admin ID when creating an import progress record.
- [ ] Check the current admin permission when confirming an import.
- [ ] Require the requesting admin to own the preview or progress record.
- [ ] Prevent another signed-in user from viewing import progress by ID alone.
- [ ] Prevent another signed-in user from confirming an import by ID alone.
- [ ] Return a controlled authorization response for unauthorized access.
- [ ] Add tests for cross-user preview and progress access.

### 8. Handle revoked access promptly

- [ ] Shorten the authentication cookie lifetime from the current 24-hour period.
- [ ] Decide on an appropriate cookie lifetime for the application.
- [ ] Revalidate the user and current admin role for sensitive operations.
- [ ] Ensure a removed user cannot import after their access is removed.
- [ ] Ensure a revoked admin cannot perform administrative changes.
- [ ] Ensure authorization changes do not depend on cookie expiration.
- [ ] Invalidate or refresh affected authentication sessions where appropriate.
- [ ] Add tests for removed users and revoked administrators.

### 9. Separate database setup permissions from everyday permissions

- [x] Identify schema changes currently performed during normal application operations.
- [x] Move backup-table creation into the database setup process.
- [ ] Move column alterations into the database setup process.
- [ ] Move all other runtime schema changes into version-controlled migrations.
- [ ] Remove runtime schema modification code from normal application paths.
- [ ] Create a restricted application database user.
- [ ] Grant only the read, insert, update, and delete permissions required by features.
- [ ] Keep schema-management permissions in a separate setup or migration account.
- [ ] Verify the application starts and operates with restricted permissions.
- [ ] Document both database users and their intended responsibilities.

### 10. Remove personal values from diagnostics

- [x] Stop logging sample values from the `Honor/Memorial Name` spreadsheet column.
- [x] Review logs for user email addresses.
- [x] Review logs for uploaded filenames.
- [x] Review exception messages before they are written to logs.
- [x] Review spreadsheet validation errors before they are returned in progress responses.
- [x] Remove personal values from progress responses where possible.
- [x] Prefer row numbers, field names, error categories, and counts for diagnostics.
- [x] Add redaction or structured logging rules for sensitive fields.
- [ ] Verify production logs do not contain donor or account personal data.

## Verify the Protections

### 11. Add focused authorization tests

- [ ] Verify a regular user cannot import.
- [ ] Verify a regular user cannot manage users.
- [ ] Verify a regular user cannot view another user’s import progress.
- [ ] Verify a user cannot export donors from an unauthorized account.
- [ ] Verify an unknown Google user is rejected.
- [ ] Verify a removed user cannot access protected operations.
- [ ] Verify a revoked administrator cannot import.
- [ ] Verify a revoked administrator cannot perform administrative changes.
- [ ] Verify authorization checks are enforced server-side, not only in the UI.

### 12. Apply antiforgery protection consistently

- [ ] Identify every endpoint that changes data or starts an import.
- [ ] Add explicit antiforgery protection to import endpoints.
- [ ] Preserve antiforgery protection on donor export.
- [ ] Confirm the actual upload interface sends the antiforgery token.
- [ ] Test valid requests with the upload interface.
- [ ] Test requests with a missing antiforgery token.
- [ ] Test requests with an invalid antiforgery token.
- [ ] Confirm rejected requests do not change data or create import records.

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
