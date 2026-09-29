# CYA2 Local Setup (Draft)

> **Status: Draft**
>
> This document records the local setup information currently available in the repository. Values that depend on the local developer, database administrator, Google Cloud project, or deployment environment are marked **Required from project owner**. Do not commit real passwords, OAuth client secrets, donor exports, or accounting exports.

## 1. Application overview

CYA2 is a .NET Blazor application using:

- .NET 10 for the web project.
- .NET 8 for the Core, Application, Infrastructure, Shared, and test projects.
- Dapper for database access.
- MySQL through `MySql.Data`.
- Radzen Blazor components.
- Google OAuth for sign-in.
- EPPlus for Excel import and export.

The application database is a new database. It is populated from current exports from the authoritative donor system and QuickBooks, not by copying the legacy PHP application database.

## 2. Prerequisites

### .NET SDK

The web project targets `net10.0`. Supporting projects and the test project target `net8.0`.

Install a .NET 10 SDK capable of building `net10.0` and the referenced `net8.0` projects. The repository does not currently pin an SDK with a `global.json` file.

**Required from project owner:**

- The exact .NET SDK version to standardize on locally and in CI.
- Whether the supporting projects should remain on .NET 8 or be retargeted.

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

From the repository or solution directory:

```text
git clone <repository-url>
cd Cya2/cya2.0
dotnet restore cya2.0.sln
```

The primary web project is `cya2.csproj`. The solution also contains the Core, Application, Infrastructure, Shared, and Application.Tests projects.

Build the solution with:

```text
dotnet build cya2.0.sln
```

Run the tests with the test project or the Visual Studio Test Explorer after the test prerequisites have been installed.

## 4. Create and initialize the local database

The schema script is stored at:

```text
database/schema/001_InitialSchema.sql
```

This script creates the clean application schema, including the operational tables and rollback tables. It does not load donor data, accounting data, fake data, users, or secrets.

Create an empty local database using the setup account, select that database, and apply the schema:

```text
mysql --host=<host> --port=<port> --user=<setup-user> --password <database-name> < database/schema/001_InitialSchema.sql
```

The command above is an example only. Do not replace the placeholders with real credentials in this document.

The application assumes the schema has already been applied. It does not create, alter, or drop persistent tables during normal operation. Donation imports create session-scoped temporary staging tables; the runtime account therefore needs the separate MySQL `CREATE TEMPORARY TABLES` permission for that workflow.

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
	"MaxAttempts": 3
  }
}
```

The values shown are the current code defaults or safe local-development values. `UseLocalInfile` also depends on the MySQL connection configuration allowing local infile operations. Enable it only after the security implications and server settings have been confirmed.

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

## 6. Add the initial local application user

Google sign-in is matched to an application user in the `Users` table. A local user must be added by an administrator or setup process after the schema is applied.

The user record requires at least:

- The Google subject identifier in `GoogleId`.
- The matching email address.
- Display name.
- Language, normally `en`.
- An application authorization level such as `Admin`, `Viewer`, `Intern`, or `User`.
- An optional default account.

**Required from project owner:**

- Which local Google account should be the first administrator.
- The exact Google subject identifier and email address, supplied privately rather than committed here.
- Whether the first user should be an `Admin`.
- Which account, if any, should be assigned as the default account.

Do not place a real email address, Google subject identifier, or password in this document or in source control.

## 7. Run the application

The repository provides these local launch URLs:

```text
HTTPS: https://localhost:7243
HTTP:  http://localhost:5211
```

Use HTTPS for Google OAuth:

```text
dotnet run --project cya2.csproj --launch-profile https
```

If the local HTTPS certificate is not trusted, trust the development certificate using the standard .NET development-certificate procedure for the operating system.

On startup, the application performs a database availability check. If the database is unavailable, it is expected to enter limited mode rather than crash.

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

1. Exact .NET SDK version and whether a `global.json` should be added.
2. Final target framework decision for the supporting .NET 8 projects.
3. Supported and tested MySQL version.
4. Local database creation procedure and database name convention.
5. Local schema setup account and restricted runtime account responsibilities.
6. Required MySQL grants, including the temporary-table grant.
7. Standard local connection-string format and TLS requirements.
8. Google Cloud project, OAuth client, authorized redirect URIs, and local sign-in users.
9. Procedure for adding the first local administrator to `Users`.
10. Complete list of required environment settings for the chosen hosting and development environments.
11. Whether `Import:UseLocalInfile` should remain disabled or be enabled in approved environments.
12. Supported operating systems and required local tooling.
13. Approved synthetic data and Excel fixtures.
14. EPPlus licensing confirmation for the organization and the intended development/deployment environments.
15. CI workflow and deployment environment details; no workflow configuration was confirmed during this inventory.
