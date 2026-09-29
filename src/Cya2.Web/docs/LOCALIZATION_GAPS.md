# Localization Gaps

## Resource key coverage

The English and Spanish resource files currently have matching key sets:

- English keys: 240
- Spanish keys: 240
- Missing Spanish keys: none
- Spanish-only keys: none

The Admin upload and progress keys added during the recent localization work now have Spanish equivalents.

## Remaining error-related gaps

The following areas still expose error text that is not consistently represented by localized resource keys. These were intentionally documented rather than changed in this pass.

### Admin page

`Components/Pages/Admin.razor` still contains error paths that should be reviewed:

- Initialization and data-loading messages constructed with literals, including messages similar to `Error initializing admin page`, `Error loading accounts`, and `Error loading users`.
- Exception messages appended directly to user-visible text, such as `ex.Message`.
- Rollback availability and execution messages displayed from service-provided `ErrorMessage` values.
- Account, fund, and staff operation messages displayed directly from service results.
- Diagnostic text such as `Debug: LoadAllStaff exception` that should either be removed from the UI or replaced with a safe localized message and server-side logging.
- The `UserFormModel` validation attributes still use hard-coded English messages (`Email is required`, `Please enter a valid email address`, and `Name is required`).
- An internal `Invalid import type` exception message remains hard-coded. This is primarily diagnostic and should normally remain out of the user interface.

### Other components

- `Components/App.razor` still contains a hard-coded `Not found` page title. The router's not-found content is localized, but this fallback title should be reviewed separately.
- Components that render service-generated errors should distinguish between a localized user-safe message and the underlying exception details. Exception details should generally remain in logs rather than being shown directly to users.

## Recommended follow-up

1. Introduce resource keys for each user-facing Admin operation and validation message.
2. Replace direct exception display with localized, user-safe messages while retaining the exception in structured logs.
3. Localize rollback service result messages at the service boundary or map known service error codes to resource keys in the UI.
4. Replace DataAnnotations literal messages with resource-backed validation messages.
5. Review `App.razor` and other fallback/error pages for hard-coded titles and status text.

## Scope note

This document tracks remaining localization work only. It does not change the behavior of error handling, database availability handling, rollback behavior, or limited-mode behavior.
