# .NET 10.0 Upgrade Plan

## Execution Steps

Execute steps below sequentially one by one in the order they are listed.

1. Validate that an .NET 10.0 SDK required for this upgrade is installed on the machine and if not, help to get it installed.
2. Ensure that the SDK version specified in global.json files is compatible with the .NET 10.0 upgrade.
3. Upgrade `cya2.csproj`.

## Settings

This section contains settings and data used by execution steps.

### Excluded projects

Table below contains projects that do belong to the dependency graph for selected projects and should not be included in the upgrade.

| Project name | Description |
|:-------------|:-----------:|
| *(none)*     |     N/A     |

### Aggregate NuGet packages modifications across all projects

NuGet packages used across all selected projects or their dependencies that need version update in projects that reference them.

| Package Name                                   | Current Version | New Version | Description                      |
|:-----------------------------------------------|:---------------:|:-----------:|:---------------------------------|
| Microsoft.AspNetCore.Authentication.Google     |     8.0.16      |   10.0.8    | Recommended for .NET 10.0        |

### Project upgrade details
This section contains details about each project upgrade and modifications that need to be done in the project.

#### `cya2.csproj` modifications

Project properties changes:
  - Target framework should be changed from `net8.0` to `net10.0`

NuGet packages changes:
  - `Microsoft.AspNetCore.Authentication.Google` should be updated from `8.0.16` to `10.0.8` (*recommended for .NET 10.0*)

Feature upgrades:
  - No additional feature-specific breaking changes were detected by analysis.

Other changes:
  - Run a full solution build and Blazor regression checks after package and framework updates.

Blazor regression checks:
  - Verify app startup and initial render complete without console errors in browser dev tools.
  - Verify authentication sign-in flow works with Google provider and returns to the expected page.
  - Verify authentication sign-out clears user state and protected UI no longer renders.
  - Verify authorization-protected pages/components correctly redirect or show access denied when unauthenticated.
  - Verify primary navigation links load expected routes and no `404` routes appear.
  - Verify forms with validation show validation messages and submit successfully with valid input.
  - Verify key API-backed components load data and handle empty/error responses gracefully.
  - Verify `@onclick` and other event handlers execute and update component state correctly.
  - Verify JavaScript interop calls used by components execute successfully.
  - Verify static assets (CSS, JS, images, fonts) load correctly and no missing asset errors occur.
  - Verify responsive behavior for main pages at desktop and mobile widths.
  - Verify publish output runs correctly in the target hosting environment (or local equivalent).
