# .NET Version Upgrade

## Strategy
**Selected**: All-At-Once
**Rationale**: The entire six-project solution is SDK-style and already uses modern .NET; five projects move from net8.0 to net10.0 while the existing net10.0 Blazor application remains included in the atomic solution-wide validation.

### Execution Constraints
- Upgrade all six solution projects in one atomic operation; do not narrow scope to only the five net8.0 projects.
- Keep the existing net10.0 Blazor application in the solution-wide restore, build, and test validation.
- Resolve the three reported source-incompatible TimeSpan API findings inline.
- Preserve database startup probing and limited-mode behavior, cache snapshot reuse, current-state donor contact behavior, and tooltip semantic styles.
- Treat compiler warnings as errors for task completion and run the full test suite after the upgrade.

## Preferences
- **Flow Mode**: Automatic
- **Target Framework**: net10.0
- **Scope**: Entire solution
- **Upgrade Strategy**: All six projects together
- **API Handling**: Fix inline

### Execution Style
- **Proceed with upgrade**: User approved continuing with the all-at-once .NET 10 upgrade, including source compatibility fixes and full validation.

## Decisions
- Upgrade the whole solution, including projects already on net10.0 — user explicitly confirmed solution-wide scope.
- Use the All-At-Once strategy — user confirmed this approach.

## Source Control
- **Source Branch**: main
- **Working Branch**: upgrade-dotnet-10
- **Commit Strategy**: After Each Task
- **Branch Sync**: Auto (Merge)
- **Last Sync Commit**: 6dc7306 (origin/main)

## Build Tool Decisions
- **All six projects in `cya2.0.sln`**: `dotnet build` and `dotnet test`; all projects are SDK-style and target modern .NET without legacy resource, WPF, or .NET Framework requirements.
