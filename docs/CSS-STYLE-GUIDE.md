# CSS Handling Guide

## Current CSS Layer Order
The app loads styles in this order from `Components/Layout/MainLayout.razor`:
1. Radzen base/theme CSS
2. Bootstrap
3. `wwwroot/css/radzen-overrides.css`
4. `wwwroot/app.css`
5. `wwwroot/css/responsive.css`
6. `cya2.styles.css` (generated scoped component CSS)

This order is intentional and should be preserved.

## Where CSS Belongs

### 1) `wwwroot/css/radzen-overrides.css`
Use this file for **Radzen-specific overrides** that require:
- high selector specificity,
- `!important`,
- direct targeting of Radzen internal classes (`.rz-*`),
- behavior fixes for dropdowns/tooltips/grid internals.

Examples in current app:
- `.rz-dropdown-panel`, `.rz-tooltip`, `.rz-chart-tooltip`
- Radzen grid amount/header alignment helpers

### 2) `wwwroot/app.css`
Use this file for **global app styles and shared helpers**:
- app shell/background/layout foundations,
- design tokens and theme variables,
- shared utility classes reused across pages,
- shared non-page-specific controls (date toolbar shell, account selector helpers, loading boundary styles).

Examples in current app:
- `.layout-root`, `.main-content`
- `.date-range-toolbar-box`, `.date-range-apply-group`
- `.account-selector-*`, `.donor-search-row`, `.donors-grid-scroll`

### 3) `wwwroot/css/responsive.css`
Use this file for **reusable responsive layout patterns** used by multiple pages:
- two-column grid containers,
- shared breakpoint collapse behavior.

Examples in current app:
- `.page-two-col-layout`, `.page-summary-col`, `.page-grid-col`
- `.donation-list-layout`, `.donation-summary-col`, `.donation-grid-col`

### 4) Component scoped CSS (`*.razor.css`)
Use scoped CSS for **component-local styling** that should not leak globally.

Current examples:
- `Components/Layout/NavMenu.razor.css`
- `Components/Layout/MainLayout.razor.css`
- `Components/Shared/ConfirmDialog.razor.css`

### 5) Inline styles (`Style="..."`, `<style>` in `.razor`)
Allowed when necessary, especially for Radzen behavior and stability.

Use inline/page-local styles when:
- a rule is tightly coupled to one page/component,
- dynamic value binding is needed,
- Radzen-generated markup/specificity makes global/scoped class overrides unreliable,
- moving the rule to shared CSS risks regression.

Current intentional usage patterns:
- `Components/Pages/Donations.razor` large Radzen grid tuning block
- `Components/Pages/Donors.razor` page-specific suggestion/grid behavior block
- `Components/Pages/Expenses.razor` mobile grid behavior block
- Radzen `ValueTemplate` spans with ellipsis/overflow handling

## Consistency Rules (Decision Order)
Before adding CSS, choose location in this order:
1. Is it Radzen internals (`.rz-*`) or requires `!important` Radzen override? -> `radzen-overrides.css`
2. Is it reused on multiple pages and not purely responsive? -> `app.css`
3. Is it a reusable responsive breakpoint pattern? -> `responsive.css`
4. Is it local to one component and safe to scope? -> `Component.razor.css`
5. Is it tightly coupled or Radzen-specific enough to need local control? -> keep inline/page `<style>`

## Audit Findings Snapshot
- The app is using a **hybrid but consistent model**: global + responsive + Radzen override layers, with selective inline/page-local styles for Radzen-heavy surfaces.
- Date range toolbar/account selector patterns are mostly standardized via shared classes in `app.css`.
- Remaining inline/page `<style>` usage is largely justified by Radzen specificity and scroll behavior constraints.
- Scoped CSS usage is currently concentrated in layout/shared components, which is acceptable.

## Guardrails for Future Changes
- Preserve stylesheet load order in `MainLayout.razor`.
- Prefer moving duplicated, stable rules into `app.css` or `responsive.css`.
- Do not move Radzen-sensitive rules out of page/local styles without visual regression testing.
- In Razor `<style>` blocks, use `@@media` (not `@media`) to avoid Razor parsing issues.
