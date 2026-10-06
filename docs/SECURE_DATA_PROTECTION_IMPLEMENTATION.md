# Secure Data Protection Implementation

This document records the implementation of Secure Deployment TODO items 5 through 8:

- Upload limits and workbook validation.
- Import preview and progress retention.
- Import ownership and authorization.
- Authentication lifetime, account authorization, and revoked-access handling.

The application is a .NET 10 Blazor Server application using MySQL and in-memory application services.

## Item 5: Upload limits and workbook validation

### Upload policy

The application accepts only the expected Excel workbook format:

- Maximum upload size: **5,120,000 bytes (5,000 KB)**.
- Expected extension: **`.xlsx`**.
- The upload must have the expected OOXML ZIP/package signature.
- The package must be readable as an Excel workbook by EPPlus.
- The workbook must contain at least one worksheet.
- Oversized, incorrectly typed, corrupt, and structurally invalid files receive controlled validation failures.

The 5 MB limit provides headroom over the largest legitimate spreadsheet reviewed while preventing unrestricted upload processing.

### Bounded processing

Upload processing is bounded to protect application memory:

- At most two workbook validation operations run concurrently.
- At most two previews are retained at one time.
- Aggregate retained preview data is limited to 10 MB.
- Upload previews are held in bounded in-memory storage because this is a single-location application with unusually low concurrent upload activity.

Temporary-file or distributed preview storage is not currently required by the deployment model.

### Tests

The implementation includes tests for:

- Files larger than the configured limit.
- Wrong file types.
- Corrupt OOXML packages.
- Valid minimal OOXML workbooks.
- Workbooks without worksheets.

## Item 6: Import data retention and cleanup

### Retention policy

Upload previews and in-memory import progress records are retained for a maximum of **15 minutes** in all environments, including production.

The retention policy applies to:

- Unconfirmed upload previews.
- Completed import progress records.
- Failed or abandoned import progress records.

### Cleanup behavior

Cleanup runs every minute and removes expired records. Terminal progress records may also be removed when the progress dialog closes.

Cleanup is performed when imports complete, fail, or are abandoned so that temporary import data does not remain indefinitely.

### Data handling

Import previews and progress records are temporary operational data. They are not intended to be a historical donor-data store. Diagnostic messages avoid exposing donor values, spreadsheet cell values, or exception details unnecessarily.

### Tests

The implementation includes tests proving that:

- Expired previews are removed.
- Expired progress records are removed.
- Ownership and import type are respected during removal.
- Preview capacity is released after expiration.
- Aggregate preview limits are enforced.

## Item 7: Import ownership and authorization

### Owner identity

Every import preview and progress record is associated with the authenticated internal user who started the operation. Ownership is based on the server-resolved internal user ID, not a user ID supplied by the browser.

The import type is also recorded so that donation and accounting import records cannot be incorrectly reused across workflows.

### Authorization checks

The application verifies the following before sensitive import operations:

- The caller is currently authenticated.
- The caller is currently authorized as an Admin.
- The caller owns the preview or progress record.
- The requested operation uses the expected import type.
- The preview or progress record has not expired.

Cross-user access to previews and progress records is denied. Viewing progress or confirming an import by ID alone is not sufficient authorization.

The same protections apply to direct Blazor service calls, not only HTTP controller or endpoint paths.

Unauthorized access returns a controlled failure rather than exposing whether another user's import record exists.

### Tests

The implementation includes tests for:

- Cross-user preview access.
- Cross-user progress access.
- Cross-user import confirmation.
- Mismatched import types.
- Removed or expired records.
- Direct service-path authorization.

## Item 8: Authentication and revoked-access handling

### Authentication lifetime

Authentication uses the `cya2.auth` cookie with the following policy:

- Absolute lifetime: **four hours**.
- Sliding expiration: **disabled**.
- Google login challenge: expires after four hours.
- `IsPersistent`: **false**.
- Cookie security: HttpOnly, Secure, and SameSite=Lax.

The non-persistent setting means closing the browser does not preserve the authentication cookie.

### Browser-session resume

While the browser remains open, the application preserves limited UI context through `sessionStorage` when reauthentication is required:

- Current local route.
- Selected account/fund.
- Selected date range.
- Selected date-range preset.

The stored state is validated before use and is consumed after restoration. The application does not store the following in browser storage:

- Authentication tokens.
- Donor data.
- Uploaded files.
- Workbook contents.
- Import identifiers.

Because `sessionStorage` is scoped to the browser tab/session, closing the browser clears the resume state. A new browser session starts at the normal Home/Admin entry point.

### Current user and role revalidation

The database is authoritative for current user authorization. Authentication claims are not trusted to expand access after the user has signed in.

The application revalidates the current database user and role before:

- Account selection.
- Dashboard reads.
- Donation reads.
- Expense reads.
- Donor exports.
- Admin page entry.

A missing user record is treated as revoked. An Admin or Viewer role is determined from the current database `AuthLevel` value rather than from a stale claim.

An active Blazor circuit also revalidates the current user every minute. If the user has been deleted, or an Admin has been downgraded, the browser resume state is cleared and the user is sent through the login challenge again.

### Account-level authorization

Regular-user account access is stored in the `AccountsUsers` relationship table. Before account-specific data is selected or returned, the application checks the current relationship in the database.

This prevents a cached account context or cached data result from continuing to authorize access after an administrator removes the user's `AccountsUsers` link.

Admin and Viewer users can access all accounts according to their current database role. Regular users can access only accounts currently assigned to them.

Revoking one account link does not terminate the user's entire authentication session. The revoked account is denied immediately, while the user may continue to use accounts they still have access to.

### Session behavior after revocation

The current implementation uses two levels of enforcement:

1. **Immediate checks:** Sensitive account selection and account-specific reads revalidate against the database before use.
2. **Active-session polling:** An active Blazor circuit checks the current user and role every minute.

When a user is deleted or an Admin role is removed:

- The session is treated as revoked.
- Browser-session resume state is cleared.
- The user is redirected through the login challenge.
- Reauthentication is required.

### Tests and validation

Focused authorization tests cover:

- Deleted users.
- Current database roles.
- Revoked account memberships.
- Valid account memberships.
- Prevention of stale elevated claims.
- Existing cache invalidation behavior.

Final validation for this implementation completed with:

- Full solution build successful.
- **83/83 application tests passing.**
- `git diff --check` successful.

## Related documentation

- `docs/SECURE_DEPLOYMENT_TODO.md` - completion checklist.
- `docs/CACHE_ARCHITECTURE.md` - cache sharing, invalidation, and lifetime guidance.
