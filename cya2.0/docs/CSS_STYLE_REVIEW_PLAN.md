# CSS Style Review Plan

## Purpose

Document the CSS organization decisions and cleanup candidates identified during the responsive date-range, account-selector, and Expenses layout work. This document is a decision checklist and test plan; it does not itself change application behavior.

## Decision Items

### 1. Where should Radzen account-selector overrides live?

**Current state:** Account selector rules in `wwwroot/app.css` use `!important` and control Radzen-generated dropdown sizing indirectly through classes such as `.account-selector-dropdown` and `.account-selector-dropdown-inline`.

**Style-guide question:** Should the Radzen-host-specific rules move to `wwwroot/css/radzen-overrides.css`, leaving only generic wrapper/layout rules in `app.css`?

**Decision criteria:**

- Move rules that require `!important` or depend on `.rz-dropdown` behavior to `radzen-overrides.css`.
- Keep generic classes such as `.account-selector-box` and `.account-selector-row` in `app.css`.
- Confirm that the move does not change desktop sizing, narrow-screen fitting, ellipsis behavior, or popup positioning.

**Decision:** [ ] Keep current placement  [ ] Move Radzen-specific rules  [ ] Split rules between both files

### 2. Where should shared responsive date/account rules live?

**Current state:** The reusable breakpoint rules for Donations, Donors, and Expenses are in `wwwroot/app.css`.

**Style-guide question:** Should the reusable `@media` rules move to `wwwroot/css/responsive.css`?

**Decision criteria:**

- Move shared breakpoint layout behavior to `responsive.css`.
- Keep non-responsive shared control styling in `app.css`.
- Preserve stylesheet load order and verify that `responsive.css` still wins over the base helpers.

**Decision:** [ ] Keep current placement  [ ] Move shared breakpoint rules to `responsive.css`

### 3. Where should Home-only account-row rules live?

**Current state:** `.home-account-selector-row` and related rules are in the global `app.css` file.

**Style-guide question:** Should Home-only rules move to `Home.razor.css` or remain in a local `<style>` block?

**Decision criteria:**

- Prefer `Home.razor.css` when the rules are ordinary component-local layout.
- Keep a local `<style>` block only if Radzen-generated markup or specificity requires it.
- Confirm that Home desktop and narrow-screen layouts remain unchanged.

**Decision:** [ ] Keep in `app.css`  [ ] Move to `Home.razor.css`  [ ] Keep local to `Home.razor`

### 4. Where should Expenses-only toolbar rules live?

**Current state:** `.expenses-view-toolbar` is in `app.css`, although it is used only by Expenses.

**Style-guide question:** Should it move to `Expenses.razor.css` or the existing Expenses local `<style>` block?

**Decision criteria:**

- Move page-specific styling out of global CSS unless it is intended for reuse.
- Preserve the original shaded container and the original placement of the More Detail button and Compact View checkbox.
- Avoid reintroducing a global `.date-range-toolbar-box` display rule that changes unrelated containers.

**Decision:** [ ] Keep in `app.css`  [ ] Move to `Expenses.razor.css`  [ ] Keep in the Expenses local style block

### 5. Should the generic toolbar box and flex row be separate classes?

**Current state:** `.date-range-toolbar-box` is both a shaded container and, in some contexts, a flex row through a more specific selector.

**Style-guide question:** Should a separate semantic class such as `.date-range-control-row` be introduced?

**Decision criteria:**

- Keep `.date-range-toolbar-box` as a block-level shaded container.
- Apply flex behavior only to the specific date-control row.
- Verify that Expenses retains one shaded outer box while Donations and Donors retain the intended date-control alignment.

**Decision:** [ ] Keep current contextual selector  [ ] Add a separate flex-row class

## Candidates for Duplicate or Unnecessary Rules

These candidates should be tested before removal. Do not delete them solely because they appear similar; verify their computed-style effect at each relevant breakpoint.

### Account selector width and flex rules

Potential overlap exists among:

- `.account-selector-box`
- `.account-selector-dropdown`
- `.account-selector-dropdown-inline`
- `.account-selector-row .account-selector-dropdown`
- `.account-selector-row .account-selector-dropdown-inline`
- `.home-account-selector-row .account-selector-dropdown`
- `.home-account-selector-row .account-selector-dropdown-inline`
- `.shared-account-control .account-selector-dropdown`
- `.shared-account-control .account-selector-dropdown-inline`

**Test for removal or consolidation:**

- Determine whether the base dropdown width is immediately overridden by the row-specific rule.
- Determine whether the `account-selector-dropdown` and `account-selector-dropdown-inline` rules still need separate declarations.
- Check whether `flex`, `width`, and `min-width` are all required or whether one declaration is redundant.
- Test Home, Donations, Donors, Expenses, and User Settings because they use different parent structures.

### Repeated `min-width: 0` and `max-width: 100%`

These declarations occur on multiple nested elements, especially in the `max-width: 1100px` rules.

**Test for removal or consolidation:**

- Remove one declaration at a time in a temporary branch or DevTools override.
- Test for flex-item shrink behavior and horizontal overflow.
- Keep the declaration only where it changes the computed result or prevents a known regression.

### Repeated `width: 100%` declarations in the responsive block

Potentially overlapping rules affect:

- `.shared-common-range`
- `.shared-custom-range`
- `.shared-account-control`
- `.shared-common-range > div`
- `.shared-custom-range > div`
- `.shared-account-control > div`
- `.shared-account-control .date-range-toolbar-box`
- `.shared-account-control .date-range-toolbar-box > div`

**Test for removal or consolidation:**

- Verify whether the parent width alone controls the child correctly.
- Confirm that the account selector does not collapse at tablet widths.
- Confirm that date controls do not expand to the full row when they should remain tool-sized.

### Repeated flex declarations

Potential overlap exists among base rules and breakpoint rules using:

- `flex: 0 1 420px`
- `flex: 0 1 min(420px, calc(100vw - 2rem))`
- `flex: 1 1 0`
- `flex: 1 1 auto`
- `flex: 0 0 100%`

**Test for removal or consolidation:**

- Record the computed `flex`, `flex-basis`, `flex-grow`, and `flex-shrink` values for the dropdown and each parent.
- Confirm desktop behavior before testing responsive behavior.
- Remove rules that are fully superseded by a later, more specific declaration.

### Date toolbar display rules

Potential overlap exists among:

- `.date-range-toolbar-box`
- `.shared-common-range .date-range-toolbar-box`
- `.expenses-view-toolbar`
- `.date-range-apply-group`
- `.date-range-start-end-row`

**Test for removal or consolidation:**

- Confirm that Expenses uses a block-level shaded container.
- Confirm that Common Ranges uses a flex row.
- Confirm that the custom date range still wraps correctly.
- Check whether `.expenses-view-toolbar { display: block; }` is still needed after separating the container and row classes.

### Expenses grid layout rules

Potentially unnecessary or duplicated candidates include:

- The `expenses-grid-stack` wrapper and its CSS, if it is no longer present in the restored Git-baseline markup.
- `.narrow-expense-grid { max-width: 100%; margin: 0 auto; }` if both declarations are already provided by the Radzen card or parent column.
- Repeated compact-grid selectors for `.expense-grid-compact`, `.expense-data-grid-compact`, and `.compact-data-grid`.

**Test for removal or consolidation:**

- Compare the rendered grid width and scrolling behavior before and after each candidate removal.
- Test both Internal Transfers and Expenses grids.
- Test compact and detailed views.
- Test wide desktop, tablet, and narrow mobile widths.

## Suggested Verification Matrix

| Surface | Wide desktop | Tablet / narrow desktop | Mobile | Key checks |
|---|---:|---:|---:|---|
| Home | Yes | Yes | Yes | Account label, dropdown width, ellipsis, no overflow |
| Donations | Yes | Yes | Yes | Common Ranges, custom dates, Account and Sub Account |
| Donors | Yes | Yes | Yes | Date controls, Account selector, donor content below |
| Expenses | Yes | Yes | Yes | Shaded controls/grid container, grid stacking, compact view |
| User Settings | Yes | Optional | Yes | Default account selector remains bounded |

For every surface, record:

- Selected viewport width.
- Computed width and flex values for the dropdown.
- Computed width and flex values for the immediate parent row.
- Whether the selected value is ellipsized rather than clipped by the page.
- Whether horizontal page scrolling appears.
- Whether Radzen dropdown popup positioning remains correct.

## Recommended Review Order

1. Establish the Git-baseline desktop appearance for Expenses.
2. Separate generic shaded containers from flex control rows.
3. Move shared breakpoint rules to `responsive.css` if the decision is approved.
4. Move Radzen-specific `!important` rules to `radzen-overrides.css` if the decision is approved.
5. Move Home- and Expenses-only rules to component-local CSS.
6. Test and remove only demonstrably redundant width, flex, and min/max-width declarations.
7. Build the solution and perform the verification matrix at representative viewport widths.
