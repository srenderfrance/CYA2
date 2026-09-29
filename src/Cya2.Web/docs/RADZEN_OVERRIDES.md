# Radzen Theme and CSS Overrides

This document records how Radzen styling is overridden in this application. It is intended as a reference when upgrading Radzen, changing the application theme, or diagnosing browser-specific visual differences.

## Stylesheet Load Order

The stylesheet order is defined in `Components/Layout/MainLayout.razor`:

1. Radzen `default-base.css`
2. Radzen `default.css`
3. Bootstrap Icons
4. Bootstrap CSS
5. `wwwroot/css/radzen-overrides.css`
6. `wwwroot/app.css`
7. `wwwroot/css/responsive.css`
8. Generated scoped component CSS

The Radzen override file must remain after both Radzen stylesheets and Bootstrap. Otherwise, Radzen or Bootstrap declarations can win in the cascade.

```razor
<link rel="stylesheet" href="/_content/Radzen.Blazor/css/default-base.css" />
<link rel="stylesheet" href="/_content/Radzen.Blazor/css/default.css" />
<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css" />
<link rel="stylesheet" href="/css/radzen-overrides.css" />
<link rel="stylesheet" href="/app.css" />
```

## Where Radzen Theme Values Are Defined

Radzen defines its default theme values in `default.css`, generally as CSS custom properties on `:root`. The application overrides these values in:

```text
wwwroot/css/radzen-overrides.css
```

The application theme currently uses:

- Aqua primary controls: `rgb(96 188 181)`
- Dark blue success/selection controls: `#0a3b6b`
- Dark red danger controls: `#910813`
- White control text: `#ffffff`

Keep global Radzen `--rz-*` variables in `radzen-overrides.css`, not in `app.css`. This keeps Radzen theme configuration separate from application layout and page-specific styling.

## Important Radzen Variables

### Primary buttons and controls

Radzen buttons use the primary color variables rather than application Bootstrap variables:

```css
--rz-primary
--rz-primary-light
--rz-primary-lighter
--rz-primary-dark
--rz-primary-darker
--rz-on-primary
--rz-on-primary-light
--rz-on-primary-lighter
--rz-on-primary-dark
--rz-on-primary-darker
```

For consistent cross-browser results, override the related `on-*` contrast variables as well as the background variables.

### Danger buttons

Danger buttons use:

```css
--rz-danger
--rz-danger-light
--rz-danger-lighter
--rz-danger-dark
--rz-danger-darker
--rz-on-danger
```

The application’s rollback and delete controls should use `ButtonStyle="ButtonStyle.Danger"`. If a danger button displays Radzen’s default pink/red instead of the application red, inspect both the `--rz-danger` variable and the computed `.rz-button.rz-danger` rule.

### Success and selection colors

The application uses dark blue for success and selection-related visual states:

```css
--rz-success
--rz-success-light
--rz-success-lighter
--rz-success-dark
--rz-success-darker
--rz-on-success
--rz-grid-selected-background-color
--rz-grid-selected-color
```

### Checkboxes

In Radzen 10.4.7, the checked checkbox selector reads:

```css
.rz-chkbox-box.rz-state-active {
	background-color: var(--rz-checkbox-checked-background-color);
}
```

The relevant variables are:

```css
--rz-checkbox-checked-background-color
--rz-checkbox-checked-hover-background-color
--rz-checkbox-checked-disabled-background-color
--rz-checkbox-checked-color
```

If checked checkboxes are wrong in a browser, inspect `.rz-chkbox-box.rz-state-active` and confirm which variable resolves in DevTools.

### Switches

In Radzen 10.4.7, switch colors are applied to the inner circle element:

```css
.rz-switch .rz-switch-circle {
	background: var(--rz-switch-background-color);
}

.rz-switch.rz-switch-checked .rz-switch-circle {
	background: var(--rz-switch-checked-background-color);
}
```

The relevant variables are:

```css
--rz-switch-background-color
--rz-switch-checked-background-color
--rz-switch-circle-background-color
--rz-switch-checked-circle-background-color
```

Do not rely only on `.rz-switch.rz-state-active`; the installed Radzen version may apply the visible background to `.rz-switch-circle` and may use `.rz-switch-checked` for the checked state.

### Dropdowns and grids

The application overrides selected and hover states with:

```css
--rz-dropdown-item-selected-background-color
--rz-dropdown-item-selected-color
--rz-dropdown-item-selected-hover-background-color
--rz-dropdown-item-selected-hover-color
--rz-dropdown-item-hover-background-color
--rz-dropdown-item-hover-color
--rz-grid-hover-background-color
--rz-grid-hover-color
```

Account-selector layout rules and long-label wrapping remain in `app.css` because they are application-specific. Generic Radzen option spacing or component behavior belongs in `radzen-overrides.css` unless it is intentionally limited to the account selector.

### Alerts and progress bars

The application uses:

```css
--rz-info
--rz-on-info
--rz-progressbar-value-background-color
```

These affect Radzen info alerts and progress indicators. Bootstrap `.alert-info` and `.alert-danger` are separate styles and do not automatically use Radzen variables.

### Scrollbars

Radzen’s default CSS uses these variables for its custom scrollbars:

```css
--rz-scrollbar-color
--rz-scrollbar-background-color
--rz-scrollbar-size
```

The current application values are:

```css
--rz-scrollbar-color: #0a3b6b;
--rz-scrollbar-background-color: #f1f1f1;
--rz-scrollbar-size: 10px;
```

Do not add competing global `::-webkit-scrollbar` declarations in `app.css` unless there is a specific reason. Radzen’s selectors distinguish between default and custom scrollbar modes and may be more specific than a generic universal selector.

## Explicit Component Overrides

CSS custom properties are preferred, but explicit selectors are useful when Radzen’s default selectors or browser behavior still win. The current override file includes explicit rules for:

```css
.rz-button.rz-primary
.rz-button.rz-danger
.rz-chkbox-box.rz-state-active
.rz-switch .rz-switch-circle
.rz-switch.rz-switch-checked .rz-switch-circle
```

Use `!important` sparingly, but it is appropriate when overriding Radzen’s component rules and the intended application value must reliably win.

## RadzenUpload

RadzenUpload’s choose button is a Radzen button nested inside `.rz-upload`. Its text, icon, and pseudo-elements may need to be overridden together:

```css
.rz-upload .rz-button,
.rz-upload .rz-button *,
.rz-upload .rz-button .rz-button-text,
.rz-upload .rz-button .rz-button-label,
.rz-upload .rz-button .rz-button-icon,
.rz-upload .rz-button span,
.rz-upload .rz-button::before,
.rz-upload .rz-button::after
```

Keep this rule in `radzen-overrides.css` because it is a generic RadzenUpload customization.

## Dialogs and DatePickers

Generic Radzen dialog and DatePicker styling belongs in `radzen-overrides.css`, including:

- `.rz-dialog`
- `.rz-dialog-titlebar`
- `.rz-dialog-content`
- `.rz-dialog-wrapper`
- `.rz-dialog-title`
- `.rz-datepicker-popup`
- `.rz-popup.rz-datepicker-popup`
- `.rz-popup .rz-calendar`

The mobile rules keep dialogs centered and viewport-sized and make DatePicker popups fit narrow screens.

## What Belongs in `app.css`

Keep application-specific layout and feature rules in `app.css`, even when they contain Radzen descendants. Examples include:

- `.account-selector-box`
- `.account-selector-dropdown`
- `.account-selector-option`
- `.expense-grid-compact .rz-data-grid ...`
- `.expense-data-grid-compact ...`
- `.donor-search-row`
- `.date-range-toolbar-box`
- `.section-loading-boundary`
- `.donors-grid-scroll`
- `.expenses-account-row`

A selector should remain in `app.css` when its purpose is a page layout or feature rather than a global Radzen component override.

The `.rz-layout` and `.rz-body` background rules also remain in `app.css` because they establish the application surface color.

## Bootstrap Versus Radzen

Not every button or alert in the application is a Radzen component. These Bootstrap rules do not use Radzen variables:

```css
.btn-primary
.alert-info
.alert-danger
```

When a color is wrong, first inspect the element’s classes. Use Radzen variables and `.rz-*` selectors for Radzen controls; use Bootstrap selectors or application-specific selectors for Bootstrap controls.

## Browser Troubleshooting Procedure

When a Radzen color appears correctly in Chrome but not in Firefox, Edge, or Brave:

1. Perform a hard reload with the browser cache disabled.
2. Inspect the actual element, not only its parent.
3. Check the computed `background-color`, `border-color`, and `color`.
4. Check the resolved values of the relevant `--rz-*` variables.
5. Identify the winning stylesheet and selector.
6. Confirm that `radzen-overrides.css` is loaded after Radzen `default.css` and Bootstrap.
7. Check whether the component uses a state class such as `.rz-switch-checked`, `.rz-state-active`, or `.rz-chkbox-box.rz-state-active`.
8. Check whether the control is Bootstrap rather than Radzen.

Radzen’s distributed `default.css` is minified into a single line, so searching the installed NuGet file with browser DevTools or a script can be easier than reading it in the editor.

For this project, the installed Radzen package is currently version `10.4.7`. If Radzen is upgraded, re-check the actual selectors and variable names in the new `default.css`; do not assume they remain unchanged.

## Validation Checklist After Theme Changes

- Primary Radzen buttons are aqua.
- Danger Radzen buttons are the application dark red.
- Primary and danger button text remains readable.
- Active switches are aqua and inactive switches use the intended neutral color.
- Checked checkboxes are dark blue.
- Dropdown selected and hover states remain readable.
- Account dropdown spacing, wrapping, width, alignment, and scrollbar behavior remain unchanged.
- RadzenUpload choose buttons remain aqua with white text and icons.
- Info alerts remain dark blue with white text.
- Progress bars remain aqua.
- Dialog corners, shadows, sizing, and mobile scrolling remain correct.
- DatePicker popups remain visible and fit narrow screens.
- Global and dropdown scrollbars retain the intended blue color and 10px Radzen size.
- The full solution builds successfully.
