# Copilot Instructions

## General Guidelines
This is a .net 10 app that uses blazor, dapper and mysql , radzen components and uses clrean architecture. The app is for a small non profit and allows staff to keep track of  thier fundraising accounts.

- For shared account snapshots, use the snapshot's broad coverage to serve narrower user-selected date ranges (such as Last Month); do not require the UI range to equal a special canonical range before using the snapshot.
- When modifying the caching system, preserve broad shared-snapshot reuse for narrower user-selected date ranges and update the cache architecture documentation (CACHE_ARCHITECTURE.md).
- Preserve existing cache behavior and broad shared-snapshot reuse when making further changes; prioritize architecture improvements without changing functionality or breaking caching.
- When refactoring architecture, preserve the database startup probe and limited-mode behavior; the app must not crash when the database is unavailable.

## Tooltip Styles
- Use two semantic tooltip styles throughout the app: an attention/warning style for important instructions users must notice (currently Admin account-creation guidance, red emphasis), and a neutral informational style for optional contextual help.
- Prefer reusable tooltip styling and preserve semantic distinction rather than styling all tooltips identically.

