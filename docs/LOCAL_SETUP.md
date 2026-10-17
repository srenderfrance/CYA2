# CYA2 Local Setup (Draft)

> **Status: Draft**
>
> This document records the local setup information currently available in the repository. Values that depend on the local developer, database administrator, Google Cloud project, or deployment environment are marked **Required from project owner**. Do not commit real passwords, OAuth client secrets, donor exports, or accounting exports.

## 1. Application overview

CYA2 is a .NET Blazor application using:

- .NET 10 for the web, Core, Application, Infrastructure, Shared, and test projects.
- Dapper for database access.
- MySQL through `MySql.Data`.
- Radzen Blazor components.
- Google OAuth for sign-in.
- EPPlus for Excel import and export.

The application database is a new database. It is populated from current exports from the authoritative donor system and QuickBooks, not by copying the legacy PHP application database.

## Local Visual Studio and MySQL test walkthrough

Use this walkthrough to run CYA2 from Visual Studio on this laptop, connect it to an empty local MySQL database, sign in as the first administrator, and upload synthetic test workbooks. You open the existing CYA2 solution; do not create a new Visual Studio project.

### 1. Install and start the local prerequisites

Install the .NET SDK version in `global.json`, Visual Studio with the ASP.NET and web development workload, MySQL Server 8.0 or later, and MySQL Workbench. Workbench manages the database, but the MySQL Server service is the component that must be running for CYA2 to connect.

Start the MySQL Server service and confirm that Workbench can connect to `localhost` with an account that can create databases and tables.

### 2. Open and configure the existing solution in Visual Studio

1. Open `C:\Users\srend\dev\Cya2\Cya2.sln` in Visual Studio.
2. Restore NuGet packages if Visual Studio does not restore them automatically.
3. In Solution Explorer, right-click `src/Cya2.Web/cya2.csproj` (`cya2`) and select **Set as Startup Project**.
4. Select the `https` launch profile. Its local address is `https://localhost:7243`.
5. If prompted, trust the ASP.NET Core development HTTPS certificate. Google OAuth requires HTTPS.

Do not run the application yet; create and initialize its empty database first.

### 3. Create an empty local database in MySQL Workbench

Open a SQL tab in Workbench using a local account that can create databases and execute:

```sql
CREATE DATABASE cya2_test
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_0900_ai_ci;
```

Use a different database name if `cya2_test` already exists. Do not use an existing database that contains application tables or real data.

For this local test setup, the same local MySQL account can initialize the schema and run the application. A production deployment must use separate restricted runtime and schema setup accounts.

### 4. Initialize the schema from PowerShell

Open PowerShell in the repository root:

```powershell
cd C:\Users\srend\dev\Cya2
```

Set a connection string for the empty database. Replace only the placeholder values in your PowerShell session; do not add credentials to a tracked file:

```powershell
$env:CYA2_SCHEMA_CONNECTION_STRING = "Server=localhost;Port=3306;Database=cya2_test;User ID=<your-mysql-user>;Password=<your-mysql-password>;SslMode=None;"
```

Run the database migrator:

```powershell
dotnet run --project tools/Cya2.DatabaseMigrator -- --connection-string "$env:CYA2_SCHEMA_CONNECTION_STRING" --initialize
```

The migrator runs `database/schema/001_InitialSchema.sql`, records the baseline, and applies later scripts from `database/migrations`. The Blazor application deliberately does not create or alter persistent tables during startup.

Refresh `cya2_test` in Workbench after the command succeeds. Tables such as `Users`, `Accounts`, `DonationData`, and `AccountingData` should appear.

### 5. Add local secrets for the application

From the same PowerShell window, set the Web project's local-only secrets. Use your local MySQL credentials and the Google OAuth values from your Google Cloud project:

```powershell
dotnet user-secrets set "ConnectionStrings:default" "Server=localhost;Port=3306;Database=cya2_test;User ID=<your-mysql-user>;Password=<your-mysql-password>;SslMode=None;" --project src/Cya2.Web/cya2.csproj
dotnet user-secrets set "Authentication:Google:ClientId" "<google-client-id>" --project src/Cya2.Web/cya2.csproj
dotnet user-secrets set "Authentication:Google:ClientSecret" "<google-client-secret>" --project src/Cya2.Web/cya2.csproj
dotnet user-secrets set "InitialAdminBootstrap:Email" "<your-google-email>" --project src/Cya2.Web/cya2.csproj
dotnet user-secrets set "InitialAdminBootstrap:Name" "<your-display-name>" --project src/Cya2.Web/cya2.csproj
```

In Google Cloud, add this authorized redirect URI to the OAuth client:

```text
https://localhost:7243/signin-google
```

The Google account used to sign in must use the exact email configured in `InitialAdminBootstrap:Email`.

### 6. Run, sign in, and upload test data

1. In Visual Studio, start the `cya2` project with the `https` profile, or press **F5**.
2. Browse to `https://localhost:7243` if Visual Studio does not open it automatically.
3. Sign in with the configured Google account.
4. On this empty database, the first successful sign-in creates the configured user as an application `Admin`.
5. Use the application import workflows to upload only synthetic `.xlsx` donation and accounting test files.
6. Use MySQL Workbench to inspect the imported rows and confirm the expected results.

After the first administrator is created, remove the bootstrap secrets:

```powershell
dotnet user-secrets remove "InitialAdminBootstrap:Email" --project src/Cya2.Web/cya2.csproj
dotnet user-secrets remove "InitialAdminBootstrap:Name" --project src/Cya2.Web/cya2.csproj
```

Create any later users through the application's Admin user-management flow. To completely reset this test environment, drop `cya2_test`, recreate it, rerun the initialization command, and repeat the bootstrap process.

## 2. Prerequisites

### .NET SDK

All projects currently target `net10.0`.

Install the .NET SDK version specified by the repository's root `global.json` file:

```text
10.0.401
```

The `global.json` configuration allows later patch updates in the same feature band and does not allow prerelease SDKs. The GitHub Actions workflow uses the same `10.0.401` SDK version.

### MySQL

The initial schema targets MySQL 8.0 or later because it uses the `utf8mb4_0900_ai_ci` collation.

**Required from project owner or database administrator:**

- Supported MySQL version and exact minimum version.
- Local MySQL installation or container instructions.
- Local database host and port.
- Local database name.
- Name and credentials for the schema setup account.
- Name and credentials for the restricted application account.
- Confirmation that the application account has the required data permissions and `CREATE TEMPORARY TABLES` permission for import staging.

### Google OAuth

The application uses the Google OAuth callback path:

```text
/signin-google
```

With the current local launch settings, the HTTPS callback URI is:

```text
https://localhost:7243/signin-google
```

The HTTP URL is not an appropriate OAuth redirect URI for normal local sign-in.

**Required from project owner:**

- Google Cloud project to use for local development.
- OAuth client ID.
- OAuth client secret.
- Authorized redirect URI configuration.
- Authorized JavaScript origins, if required by the Google configuration.
- Google account(s) allowed to sign in during development.

## 3. Get the source and restore dependencies

From the repository root:

```text
git clone <repository-url>
cd Cya2
dotnet restore Cya2.sln
```

The primary solution is `Cya2.sln`. It contains these projects:

- `src/Cya2.Web/cya2.csproj` — the Blazor web application.
- `src/Cya2.Core/Cya2.Core.csproj`.
- `src/Cya2.Application/Cya2.Application.csproj`.
- `src/Cya2.Infrastructure/Cya2.Infrastructure.csproj`.
- `src/Cya2.Shared/Cya2.Shared.csproj`.

The test project is `tests/Cya2.Application.Tests/Cya2.Application.Tests.csproj` and can be run separately or from Visual Studio Test Explorer.

Build the solution with:

```text
dotnet build Cya2.sln
```

Run the tests with:

```text
dotnet test tests/Cya2.Application.Tests/Cya2.Application.Tests.csproj
```

The relocated web project is `src/Cya2.Web/cya2.csproj`. The former `cya2.0` solution and project paths should not be used.

## 4. Create and initialize the local database

The schema script is stored at:

```text
database/schema/001_InitialSchema.sql
```

This script creates the clean application schema, including the operational tables and rollback tables. It does not load donor data, accounting data, fake data, users, or secrets.

Create an empty local database first. Then initialize it with the standalone runner using the setup account:

```powershell
$env:CYA2_SCHEMA_CONNECTION_STRING = "Server=<local-host>;Port=<port>;Database=<database-name>;User ID=<setup-user>;Password=<setup-password>;SslMode=<local-development-mode>;"
dotnet run --project tools/Cya2.DatabaseMigrator -- --connection-string "$env:CYA2_SCHEMA_CONNECTION_STRING" --initialize
```

The command above is an example only. Do not replace the placeholders with real credentials in this document.

The `--initialize` option applies `database/schema/001_InitialSchema.sql`, records the baseline, and then applies any pending scripts in `database/migrations`. It is safe only for an empty application database; the runner refuses initialization when application tables already exist. The application does not create, alter, or drop persistent tables during normal operation. Donation imports create session-scoped temporary staging tables; the runtime account therefore needs the separate MySQL `CREATE TEMPORARY TABLES` permission for that workflow.

For later schema changes, use the standalone migration runner with the setup account rather than the web application:

```powershell
$env:CYA2_SCHEMA_CONNECTION_STRING = "Server=<local-host>;Port=<port>;Database=<database-name>;User ID=<setup-user>;Password=<setup-password>;SslMode=<local-development-mode>;"
dotnet run --project tools/Cya2.DatabaseMigrator -- --connection-string "$env:CYA2_SCHEMA_CONNECTION_STRING"
```

The runner applies numbered scripts from `database/migrations` in order and records successful applications in `Cya2SchemaMigrations`. It is not invoked by Blazor startup. Existing databases created from `database/schema/001_InitialSchema.sql` require explicit schema verification before the deployment process adopts that baseline; do not insert a baseline history row without that verification.

The setup account and restricted application account are MySQL database-infrastructure users. Create them with the DBA, hosting provider, or a protected provisioning process. The Blazor application and migration runner consume their connection strings but do not create database users or manage their passwords.

Use two database accounts:

- A schema setup account with the permissions required to create or update the schema.
- A restricted application account for normal operation. It should not have `CREATE`, `ALTER`, `DROP`, or other persistent schema-management permissions. It does need the data permissions required by the application and `CREATE TEMPORARY TABLES` for imports.

## 5. Configure the application

### Connection string

The application reads the `ConnectionStrings:default` setting. `Program.cs` also accepts the `MYSQLCONNSTR_default` environment variable, which overrides the configured connection string when present.

Use local secrets or environment variables rather than committing a real connection string. A safe placeholder shape is:

```text
Server=<local-host>;Port=<port>;Database=<database-name>;User ID=<application-user>;Password=<local-password>;SslMode=<local-development-mode>;
```

The setup account used to apply the schema should not be the runtime account used by the application.

### Google authentication settings

Configure these values outside committed JSON files, preferably with .NET user secrets for local development:

```text
Authentication:Google:ClientId=<google-client-id>
Authentication:Google:ClientSecret=<google-client-secret>
```

For the local HTTPS profile, register:

```text
https://localhost:7243/signin-google
```

The repository's Development configuration has `Development:BypassGoogleAuth` set to `false`. Do not enable a bypass as a substitute for configuring authentication unless the project owner explicitly defines a safe development-only procedure.

### Import settings

The code recognizes the following optional settings:

```json
{
  "Import": {
	"BatchSize": 1000,
	"UseLocalInfile": false,
	"MaxAttempts": 3,
	"MaximumUploadBytes": 5120000
  }
}
```

The values shown are the current code defaults or safe local-development values. `UseLocalInfile` also depends on the MySQL connection configuration allowing local infile operations. Enable it only after the security implications and server settings have been confirmed.

Uploaded donation and accounting workbooks must use the `.xlsx` extension and are limited to 5,000 KB (5,120,000 bytes). The application validates the OOXML package signature before retaining a preview. This limit was selected because current workbooks are below 3,000 KB while allowing room for growth.

### Cache settings

The current configuration supports:

```json
{
  "CacheInvalidation": {
	"EnableDataVersionMonitor": true,
	"MonitorIntervalMinutes": 15
  }
}
```

These settings are optional unless a local test requires different cache behavior.

### Import retention

The production retention policy for in-memory upload previews and import progress records is a maximum of 15 minutes. Cleanup runs every minute, and terminal progress records may also be removed when the progress dialog closes. The same 15-minute policy applies to local and other deployed environments unless a future documented policy change is approved.

## 6. Bootstrap the initial administrator

On a database with no `Users` records, the first administrator is provisioned automatically during Google sign-in only when the verified Google email exactly matches the configured bootstrap email. The provisioned user receives the `Admin` authorization level, the configured display name, English language, and no default account.

Set these values outside committed JSON files. For local development, use the Web project's .NET user secrets:

```powershell
dotnet user-secrets set "InitialAdminBootstrap:Email" "admin@example.test" --project src/Cya2.Web/cya2.csproj
dotnet user-secrets set "InitialAdminBootstrap:Name" "Initial Administrator" --project src/Cya2.Web/cya2.csproj
```

For a deployed environment, configure the equivalent secret-store or environment values:

```text
InitialAdminBootstrap__Email=<initial-admin-email>
InitialAdminBootstrap__Name=<initial-admin-display-name>
```

The bootstrap path is unavailable when either value is empty, when the signed-in Google email does not match, or after any `Users` record exists. After the first administrator signs in, remove these settings from local secrets or the deployment secret store. All later users must be created through the Admin user-management flow, which preserves the normal email-first Google account linking process.

Do not place a real email address, Google subject identifier, display name, or password in this document or in source control.

## 7. Run the application

The repository provides these local launch URLs:

```text
HTTPS: https://localhost:7243
HTTP:  http://localhost:5211
```

Use HTTPS for Google OAuth:

```text
dotnet run --project src/Cya2.Web/cya2.csproj --launch-profile https
```

The available project launch profiles are `https`, `Development`, and `cya2`. The `https` profile is recommended for local development because it uses the registered OAuth callback URL.

If the local HTTPS certificate is not trusted, trust the development certificate using the standard .NET development-certificate procedure for the operating system.

On startup, the application performs a database availability check. If the database is unavailable, it is expected to enter limited mode rather than crash.

### Azure deployment

The repository workflow is `.github/workflows/azure-webapps-dotnet-core.yml`. It currently:

- Runs on pushes to `main` or by manual workflow dispatch.
- Installs the .NET SDK `10.0.401` specified by the repository workflow and `global.json`.
- Restores and builds `Cya2.sln`.
- Publishes `src/Cya2.Web/cya2.csproj`.
- Deploys the published package to the Azure Web App configured by the `AZURE_WEBAPP_NAME` workflow variable.

The deployment requires the Azure OIDC secrets configured in GitHub Actions: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, and `AZURE_SUBSCRIPTION_ID`. The Azure Web App still requires its production connection string, Google OAuth settings, and other environment-specific values to be configured outside the repository.

## 8. Load development data

The current repository does not yet contain approved fake donor data, fake accounting data, or fake Excel import fixtures.

For local testing, use only:

- Clearly synthetic donor records.
- Clearly synthetic accounting records.
- Synthetic Excel files that contain no real donor or financial information.

The normal initial population workflow is:

1. Apply the database schema.
2. Export current donor data from the third-party donor system.
3. Export current accounting data from QuickBooks.
4. Upload the exports through the application workflows.
5. Review validation results and reconcile counts and totals.

Real exports must remain outside source control.

## 9. Secrets and local-only values

The following values must be supplied locally and must not be committed:

- Database passwords and connection strings.
- Google OAuth client secrets.
- Production or third-party database credentials.
- Real donor-system exports.
- Real QuickBooks exports.
- Real user emails or Google subject identifiers, unless the repository's secret-handling policy explicitly permits them.

Recommended local storage options are .NET user secrets, environment variables, or a local secret manager. Do not put these values in `appsettings.json`, `appsettings.Development.json`, documentation, SQL scripts, or test fixtures.

## 10. Information still needed to finalize this guide

The following information is not available or not confirmed in the repository:

1. Supported and tested MySQL version.
2. Local database creation procedure and database name convention.
3. Local schema setup account and restricted runtime account responsibilities.
4. Required MySQL grants, including the temporary-table grant.
5. Standard local connection-string format and TLS requirements.
6. Google Cloud project, OAuth client, authorized redirect URIs, and local sign-in users.
7. Approved local test Google account and OAuth client values.
8. Complete list of required environment settings for the chosen hosting and development environments.
9. Whether `Import:UseLocalInfile` should remain disabled or be enabled in approved environments.
10. Supported operating systems and required local tooling.
11. Approved synthetic data and Excel fixtures.
12. Final Azure Web App settings, production connection string, Google OAuth redirect URI, and database hosting details.

EPPlus licensing decision: Servant Partners is a legal US nonprofit using CYA2 only for internal operations. The application is not sold, licensed, or provided to other organizations. Retain the existing Polyform Noncommercial notices and review licensing before any change to this use.
