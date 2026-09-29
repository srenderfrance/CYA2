# 02-upgrade-solution: Upgrade all solution projects to .NET 10

Upgrade all five `net8.0` projects to `net10.0` in the solution while retaining the existing Blazor web application's `net10.0` target. Restore all package references without unnecessary version changes because the assessment reports all packages compatible, then resolve the three source-incompatible `TimeSpan.FromSeconds`/`TimeSpan.FromMinutes` findings inline. Research the affected projects and preserve existing Blazor, Dapper, MySQL, Radzen, caching, database startup probe, limited-mode, donor projection, and tooltip behavior.

**Done when**: Every project in `cya2.0.sln` targets `net10.0`, restore succeeds, all source incompatibilities are resolved, and the complete solution builds with zero errors and zero warnings.

## Research Findings

- The solution contains six SDK-style projects: `src/Cya2.Core`, `src/Cya2.Shared`, `src/Cya2.Application`, `src/Cya2.Infrastructure`, `tests/Cya2.Application.Tests`, and the Blazor web project `cya2.0/cya2.csproj`.
- Five projects explicitly target `net8.0`; `cya2.0/cya2.csproj` already targets `net10.0`. Target framework properties are project-local, with no `Directory.Build.props` or central package-management file discovered.
- Package references are project-local and the assessment reports them compatible, so no package version changes are planned.
- The web project has embedded `.resx` resources but no evidence of WPF, WinForms, or legacy .NET Framework requirements; use `dotnet` tooling and escalate only if resource generation requires Visual Studio MSBuild.
- Current source search finds three production rate-limit windows using `TimeSpan.FromMinutes(1)` in `cya2.0/Extensions/WebHostServiceCollectionExtensions.cs` and no current `TimeSpan.FromSeconds` usages. This differs from the assessment's two `FromSeconds` plus one `FromMinutes` findings; verify with the .NET 10 build before changing behavior.
- Dependency order is `Cya2.Core` → `Cya2.Application` → `Cya2.Infrastructure` → `cya2.0`, with `Cya2.Shared` consumed by the web and test projects. The test project uses xUnit and currently has 63 passing baseline tests.
