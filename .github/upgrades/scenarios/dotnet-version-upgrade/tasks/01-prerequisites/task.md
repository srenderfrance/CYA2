# 01-prerequisites: Verify .NET 10 upgrade prerequisites

Verify the installed .NET 10 SDK and any `global.json` constraints for the complete solution before changing project files. Confirm the solution and all six projects are the intended scope, and capture the baseline build/test state needed to distinguish upgrade changes from pre-existing issues.

## Research Findings

- Solution: `C:\Users\srend\dev\Cya2\cya2.0\cya2.0.sln`.
- Confirmed six projects are included: `src/Cya2.Core/Cya2.Core.csproj`, `src/Cya2.Shared/Cya2.Shared.csproj`, `src/Cya2.Application/Cya2.Application.csproj`, `src/Cya2.Infrastructure/Cya2.Infrastructure.csproj`, `cya2.0/cya2.csproj`, and `tests/Cya2.Application.Tests/Cya2.Application.Tests.csproj`.
- The .NET 10 SDK is installed and compatible.
- No `global.json` file is present, so SDK selection is not constrained by repository configuration.
- All projects are modern SDK-style projects; `dotnet build` and `dotnet test` are the appropriate baseline tools.

## Baseline Validation

- Full solution build: passed with 8 pre-existing warnings and no errors. Warnings are in `FinancialDashboardService.cs`, `ExpenseCalculationTests.cs`, `DonationImportProcessor.cs`, `RollbackExecutor.cs`, `AuthenticationServiceCollectionExtensions.cs`, `Admin.razor`, and `Donors.razor`; they must be resolved during the upgrade validation because warnings are treated as errors.
- Full solution tests: 63 passed, 0 failed, 0 skipped.
- Baseline project target frameworks confirmed from build output: five projects target `net8.0`; the Blazor `cya2` project targets `net10.0`.

**Done when**: The .NET 10 SDK is available, SDK selection is compatible, the complete solution scope is confirmed, and baseline validation results are recorded.
