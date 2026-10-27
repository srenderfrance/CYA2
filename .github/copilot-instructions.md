# Copilot Instructions

## General Guidelines
This is a .net 10 app that uses blazor, dapper and mysql, radzen components and uses clean architecture. The app is for a small non-profit and allows staff to keep track of their fundraising accounts.

- For shared account snapshots, use the snapshot's broad coverage to serve narrower user-selected date ranges (such as Last Month); do not require the UI range to equal a special canonical range before using the snapshot.
- When changes are made to the caching system update the cache architecture documentation (CACHE_ARCHITECTURE.md).
- When refactoring architecture, preserve the database startup probe and limited-mode behavior; the app must not crash when the database is unavailable.
- When restoring previously working UI behavior, use the repository's Git baseline directly and adding new CSS workarounds.
- Prioritize the best long-term data architecture over the smallest short-term change, especially for donation imports and donor/contact modeling.
- AccountId is authoritative for the account relationship. Intern accounts contain donation and donor data only, with no accounting data. Merged subaccounts are semantically part of the primary account and should be combined into the primary reporting data; separate subaccounts remain distinguishable for separate views. Donor All views must combine donations across primary/separate funds and deduplicate donors who give to both, using donor identity rather than listing them twice.
- Intern accounts are not fund-based. Intern donation lookups must use DonationData.InternName (the intern identity), never the shared Fund value, because a fund can contain data for multiple interns. Intern selector display/value handling must preserve InternName as the lookup identity. All account dropdowns must display intern accounts as '<intern name> (Intern)' while preserving the underlying intern identity/value for selection and DonationData.InternName lookups.
- Do not log intern names or Fund/SubFund values anywhere, including startup diagnostics; retain only non-personal metadata such as counts, kinds, ranges, and row counts.
- Keep intern-related code comments and primary documentation up to date whenever intern behavior changes.
- When an agreed implementation plan exists, follow it as the authoritative scope. Do not propose or ask to change the plan without explicitly stating that the suggestion would deviate from the agreed plan and explaining the deviation first.
- User tests application behavior through the UI and expects diagnoses not to assume direct database edits when app-driven creation is being tested.

## Data Management
- The Donors and DonorContacts tables are application display projections, not the organization's official donor database. Contact data should be current-state only: each upload must remove contact values no longer present and retain DateCreated plus DateModified, without historical contact rows.
- For CYA2, restricted audit-stream records may include internal numeric user IDs and should have a 90-day retention period. General diagnostic logs must continue to exclude personal values and sensitive account/intern data.

## Caching Strategy
- FinancialSummary DTOs are independently cached presentation DTOs and must be recreated automatically after database updates; broad Donation/Donor and Expense/Accounting DTO caches are not automatically rebuilt for every account after invalidation and may not exist when summaries rebuild.
- Accounts Overview is automatically regenerated after DB updates using its existing special date range, not the broad preset-range DTO window. Prefer intelligent partial invalidation/rebuild when DonationData and AccountingData update separately, while allowing full DTO rebuild when simpler and safe.
- Maintain an all-normalized-account-name/access-list cache for instant account selector rendering; UserAccountContextService can help populate it, but authorization must still be revalidated before financial data access.

## Tooltip Styles
- Use two semantic tooltip styles throughout the app: an attention/warning style for important instructions users must notice (currently Admin account-creation guidance, red emphasis), and a neutral informational style for optional contextual help.
- Prefer reusable tooltip styling and preserve semantic distinction rather than styling all tooltips identically.

- For CSS and styling, make use of CSS-STYLE-GUIDE.md and RADZEN_OVERIDES.md in Cya.Web/docs

