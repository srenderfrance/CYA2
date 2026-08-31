# Copilot Instructions

## General Guidelines
- For shared account snapshots, use the snapshot's broad coverage to serve narrower user-selected date ranges (such as Last Month); do not require the UI range to equal a special canonical range before using the snapshot.
- When modifying the caching system, preserve broad shared-snapshot reuse for narrower user-selected date ranges and update the cache architecture documentation (CACHE_ARCHITECTURE.md).
- Preserve existing cache behavior and broad shared-snapshot reuse when making further changes; prioritize architecture improvements without changing functionality or breaking caching.
- When refactoring architecture, preserve the database startup probe and limited-mode behavior; the app must not crash when the database is unavailable.

