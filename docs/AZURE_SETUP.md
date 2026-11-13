# CYA2: Azure App Service Linux and MySQL Setup

This guide covers a **test deployment**, not approval for production use or migration of an existing application database. It targets **Linux Azure App Service with .NET 10** and **Azure Database for MySQL Flexible Server**. Resources can be configured in the Azure Portal; publication is manual and does not require a GitHub connection.

Use a separate, clearly identified test app and empty test database, even when using a production Azure subscription or server. Do not initialize, overwrite, or load test data into a database used by another application or for real operations. Use synthetic data for testing. Production use requires separate security, recovery, data reconciliation, and operational approval.

The repository schema runner creates the application tables for a fresh database; the web application does not initialize them. Related documentation: [local testing setup](LOCAL_SETUP.md), [database tests](DATABASE_TESTS.md), and [cache architecture](../src/Cya2.Web/docs/CACHE_ARCHITECTURE.md).

## 1. Decisions and prerequisites

Have these available before provisioning:

- An Azure subscription and permission to create resource groups, App Service, MySQL, virtual networks, Key Vault, and role assignments. Use MFA and least-privilege administrator access.
- The App Service default HTTPS hostname, or an approved custom domain and access to its DNS provider. Examples below use `<app.example.org>` for whichever hostname is selected.
- A Google Cloud project and OAuth web client approved for the organization, including the first administrator's Google account.
- A clean checkout of this repository, Visual Studio with ASP.NET/web development tooling, and the .NET SDK selected by `global.json`. All active projects target .NET 10.
- A secure setup workstation with MySQL Workbench and network access to the private database. The private-network options are described below.
- An agreed Azure region, monthly budget, backup policy, recovery objectives, and staff responsible for secrets and updates. Set a budget alert in **Cost Management**; an alert is not a spending cap.

Use separate test resources where possible and get approval before sharing an existing server. Keep App Service and MySQL in the same region where practical. Choose service tiers based on VNet integration support, measured memory, import performance, availability, and budget rather than assuming Free/shared hosting is production-ready.

### Important current application constraints

- Run **one App Service worker instance** initially. Blazor circuits, application caches, upload previews, import work queues, and progress records are process-local. Do not enable autoscale or increase the instance count without a separate scale-out design. Affinity alone does not make these services distributed.
- Restarting or publishing interrupts circuits and can abandon queued imports/previews. Schedule deployments outside imports and communicate reconnect/retry expectations.
- The application does **not** create or alter persistent tables at startup. Schema setup is a separate administrative operation.
- Startup probes database availability and enters limited mode if unavailable. A live website does not prove that the database is configured correctly.
- `/health/live` is an anonymous process-response probe, not proof of database readiness. `/health/ready` and `/health` require the application authentication cookie and reflect the existing database TCP monitor. They are not anonymous platform probes. See [Health endpoints](HEALTH_ENDPOINTS.md) for limitations; platform health-check configuration remains a separate deployment decision.
- Google OAuth is implemented by the application. Do not enable App Service Authentication (Easy Auth) as an additional sign-in layer unless separately designed and tested.
- The app currently uses a MySQL password connection string, not an Entra token-based database connection. Managed identity below is used for **Key Vault access**, not a claim that database authentication is passwordless.

## 2. Prepare the resource group and network

In the Azure Portal:

1. Select the approved subscription and resource group, or create a dedicated test resource group in the approved region.
2. Open **Virtual networks > Create** in that group and region. Choose non-overlapping address ranges with the network administrator.
3. Create separate subnets for:
   - App Service outbound VNet integration, delegated to `Microsoft.Web/serverFarms`.
   - MySQL private VNet access, delegated to `Microsoft.DBforMySQL/flexibleServers`.
   - An administrative setup VM if one is needed. This must not be either delegated subnet.
4. Allow the application integration subnet and approved setup host to reach MySQL on TCP 3306. Preserve DNS resolution and platform-required network traffic when applying network security groups or routes.

Do not put the database and App Service integration in the same delegated subnet. VNet integration is for **outbound** App Service connectivity; the website remains publicly reachable over HTTPS for staff and Google OAuth.

### Getting the setup machine onto the private network

Use an existing approved VPN/connected administration network, or create a short-lived Windows VM in the administrative subnet through the Portal and connect using Azure Bastion. Do not open RDP to the public internet. Install the matching .NET SDK, Git, and MySQL Workbench on that secure host and obtain the reviewed repository revision.

A developer's laptop cannot reach a private MySQL server just because it has Azure Portal access. The schema runner and Workbench must run from a network that can resolve and reach the database's private endpoint/address. Bastion connects you to the VM; it does not automatically give your laptop private MySQL connectivity. Budget for VM/Bastion charges and remove temporary resources after setup.

## 3. Prepare Azure Database for MySQL Flexible Server

If an approved server already exists, confirm its engine version, network, DNS, TLS, backups, and access settings below; do not change shared-server settings without authorization. Create a separate empty test database in section 4.

1. In the Portal, open **Azure Database for MySQL flexible servers > Create**.
2. Select the approved resource group and region. Choose a supported MySQL version **8.0 or later** compatible with the repository's `utf8mb4_0900_ai_ci` schema. Validate the selected engine version against the schema/imports; do not choose MariaDB.
3. Select an appropriate compute/storage tier. Configure storage growth and capacity alerts. Enable high availability if required by the agreed availability target and supported by the selected tier/region.
4. Create a strong server administrator credential. Store it in the organization's restricted administrative secret store. It is not the application's runtime credential.
5. Under **Networking**, select **Private access (VNet integration)** and the dedicated MySQL delegated subnet. Create/select its private DNS zone and ensure it is linked to the VNet used by App Service and the setup machine.
6. Configure backup retention (for example 30 days where supported) according to policy. Consider geo-redundant backup **at creation time** if the recovery requirements call for it and the region supports it. Record the choice; it is not interchangeable with zone high availability.
7. Complete creation. Record the server's full DNS name from **Overview**. Use that name, not a hard-coded private IP, in connections.
8. Confirm **Server parameters** retain secure transport/TLS requirements. Do not turn off `require_secure_transport` to resolve a connection problem.

This guide deliberately avoids public database exposure and the broad **Allow public access from any Azure service** option. If private networking is impossible, require a separately reviewed firewall design limited to known application outbound IPs and approved administrative IPs; do not expose port 3306 to all addresses.

### TLS and Workbench connection

From the secure setup host, connect Workbench to the MySQL DNS name on port 3306 using the server administrator. Configure **Verify Identity** with the current Azure MySQL CA certificates described in Microsoft's TLS guidance. Install/maintain the required trust chain rather than bypassing certificate verification. Never reuse an Aiven CA file for Azure or use `SslMode=Disabled` in production.

## 4. Create the fresh database and initialize the schema

Azure Portal provisioning creates the server, not CYA2's schema or restricted runtime database user. The following SQL/PowerShell operations are unavoidable application setup steps; they are **not Azure CLI provisioning**.

In Workbench on the secure setup host, create a new database:

```sql
CREATE DATABASE cya2_test
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_0900_ai_ci;
```

If the database already exists, do not create or recreate it. Confirm that it is the intended empty test database and check its database-level defaults before initialization:

```sql
SELECT SCHEMA_NAME, DEFAULT_CHARACTER_SET_NAME, DEFAULT_COLLATION_NAME
FROM information_schema.SCHEMATA
WHERE SCHEMA_NAME = 'cya2_test';
```

Replace `cya2_test` with the exact database name. The expected values are `utf8mb4` and `utf8mb4_0900_ai_ci`; the server's default and the tables' defaults are separate. Donation import temporary tables rely on the database default, and an incompatible default such as `utf8mb3` can cause MySQL error 1253. The schema initializer's `SET NAMES` and explicit permanent-table settings do not change the database default.

For a confirmed test database with an incorrect default, a database administrator can review and apply the following change after confirming the target and backup/recovery requirements. Replace the identifier with the exact database name and retain backticks, especially for names containing hyphens:

```sql
ALTER DATABASE `cya2_test`
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_0900_ai_ci;
```

This changes defaults for subsequently created objects; it does not convert existing tables, columns, or data. Re-run the read-only query above. For a populated or shared database, assess existing table/column collations, application compatibility, backup and recovery separately; do not convert tables, drop data, or reinitialize as a shortcut. After a confirmed default change, restart the app outside import activity so fresh connections pick up the setting, and validate a synthetic import and its outcome. Reconcile any failed or uncertain import before retrying; see [import safety](IMPORT_SAFETY.md).

Use a separate schema/setup identity from the runtime identity. For the one-time initialization, the Azure MySQL administrator can act as the setup identity; never save its credential in App Service. Replace `cya2_test` in the examples with the name of the separate empty test database if needed.

From the **repository root** on that setup host, provide the setup connection through a process environment variable. Avoid placing the password in shell history or passing it as a command-line argument:

```powershell
$schemaSecret = Read-Host "Schema connection string (hidden input)" -AsSecureString
$schemaCredential = [System.Management.Automation.PSCredential]::new("unused", $schemaSecret)
$env:CYA2_SCHEMA_CONNECTION_STRING = $schemaCredential.GetNetworkCredential().Password
try {
	dotnet run --project tools/Cya2.DatabaseMigrator -- --initialize
	if ($LASTEXITCODE -ne 0) { throw "Schema initialization failed. Stop installation." }
}
finally {
	Remove-Item Env:CYA2_SCHEMA_CONNECTION_STRING -ErrorAction SilentlyContinue
	Remove-Variable schemaSecret, schemaCredential -ErrorAction SilentlyContinue
}
```

Enter a connection string with this shape into the hidden prompt (substitute values, and use the CA/trust configuration appropriate to the setup host):

```text
Server=<mysql-server>.mysql.database.azure.com;Port=3306;Database=cya2_test;User ID=<setup-user>;Password=<setup-password>;SslMode=VerifyFull;
```

`MySql.Data` uses `VerifyFull` for certificate and hostname verification. Its certificate trust must be valid on the machine making the connection; if supplying an explicit CA file, use the connector's `SslCa` setting and an existing secured file path. Do not paste a workstation CA path into App Service configuration.

The runner reads `CYA2_SCHEMA_CONNECTION_STRING`, takes a database lock, checks that the database is empty, and applies `database/schema/001_InitialSchema.sql`. Refresh Workbench and verify `Users`, `Accounts`, `SubAccounts`, `Donors`, `DonorContacts`, `DonationData`, `AccountingData`, and `AuditEvents` exist. During pre-production, it does not create schema migration history or apply incremental migrations.

- `--initialize` requires an empty database. If it fails partway, investigate before retrying; MySQL DDL is not reliably rolled back as a whole. Only recreate a disposable test database after confirming its identity and authorization; never drop a shared or existing database as a shortcut.
- A repeat run against a nonempty database is rejected. Running without `--initialize` and incremental migrations are not supported by the current runner. Existing databases and future schema changes need a separately reviewed, non-destructive reconciliation plan and backup.

### Create the restricted runtime MySQL user

Using Workbench as the database administrator, substitute a strong generated password and execute:

```sql
CREATE USER 'cya2_test_app'@'%' IDENTIFIED BY '<generated-runtime-password>' REQUIRE SSL;
GRANT SELECT, INSERT, UPDATE, DELETE, CREATE TEMPORARY TABLES
	ON cya2_test.* TO 'cya2_test_app'@'%';
SHOW GRANTS FOR 'cya2_test_app'@'%';
```

`%` is a MySQL host pattern, not permission to bypass Azure network isolation. Keep private networking and least-privilege network rules in place. Do not grant the runtime user `CREATE`, `ALTER`, `DROP`, `FILE`, `GRANT OPTION`, or global administration rights. Donation imports require session-scoped temporary tables; those permissions do not authorize persistent schema changes. The schema migration identity, runtime MySQL identity, Azure identity, and application Admin user are four separate concepts.

Verify the runtime user can connect over verified TLS and read the initialized tables. Store its connection string in Key Vault in the next steps; never commit the SQL with a real password or put credentials in a support ticket.

## 5. Prepare Linux App Service

1. In the Portal, open **App Services > Create > Web App**.
2. Select the approved subscription, resource group, and region. Choose **Publish: Code**, **Operating System: Linux**, and a **.NET 10** runtime stack. For an existing app, verify these settings and the target before publishing.
3. Confirm the selected Linux runtime actually supports .NET 10 in the target service/region before publishing. If it is unavailable, stop and choose a separately validated self-contained/container hosting path; do not select .NET 8 for this .NET 10 application.
4. Create/select an approved App Service plan for the test. Standard or higher is a practical baseline for this guide; size it for VNet integration, the app's cache footprint and import memory.
5. Set the plan/app to **one instance**. Disable autoscale for this initial deployment.
6. After creation, use **Settings > Configuration / General settings** (Portal labels may vary) to set:
   - **Always On: On**.
   - **Web sockets: On** for Blazor's SignalR transport.
   - **ARR affinity / session affinity: On**.
   - **HTTPS Only: On**, minimum inbound TLS **1.2 or higher**, and **FTPS: Disabled** unless an approved deployment process specifically needs it.
7. Leave App Service **Authentication** disabled; CYA2 handles Google sign-in itself.
8. Under **Networking > VNet integration**, add outbound integration with the dedicated App Service subnet. Verify private DNS resolution and reachability to MySQL. Keep necessary outbound HTTPS access for Google OAuth and Azure secret retrieval.
9. Under **Identity**, turn **System assigned** managed identity **On** and save.

Use the built-in Linux .NET stack, not a custom container. The framework-dependent publish output contains `cya2.dll`. Start with the platform's default startup behavior; if detection fails, inspect the startup logs and configure the startup command `dotnet cya2.dll` in App Service's general configuration. Do not assume IIS or a Windows `web.config` controls Linux startup.

## 6. Key Vault and application settings

### Create and authorize Key Vault

1. Create or select an approved **Key Vault** for the test, using Azure RBAC permissions. Keep soft delete and purge protection enabled.
2. Have a designated secret administrator create secrets for the runtime database connection string and Google OAuth client secret. Optionally store the client ID and temporary bootstrap values here as well.
3. Under Key Vault **Access control (IAM)**, give the App Service's managed identity **Key Vault Secrets User**, scoped to the required vault/secrets. Do not give the app Secret Officer/Administrator privileges.
4. Ensure the vault's networking permits secret retrieval by App Service. If using a private endpoint, configure its private DNS/VNet connectivity too. A role assignment alone does not bypass a vault firewall.
5. Copy each required secret's **Secret Identifier**. Use a versionless identifier for managed rotation where appropriate, and verify references resolve in App Service. Secret rotation is not necessarily immediate: refresh/restart the app deliberately after updating a database credential and validating the new secret.

### App Service environment variables

Open App Service **Settings > Environment variables** (older Portal versions: **Configuration > Application settings**). Add and save these application settings:

| Name | Value / purpose |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__default` | Key Vault reference to the **runtime** MySQL connection string |
| `Authentication__Google__ClientId` | Google OAuth web client ID (or a Key Vault reference) |
| `Authentication__Google__ClientSecret` | Key Vault reference to the Google OAuth client secret |
| `InitialAdminBootstrap__Email` | Temporary, exact Google email for the first Admin; preferably a Key Vault reference |
| `InitialAdminBootstrap__Name` | Temporary Admin display name; preferably a Key Vault reference |
| `AllowedHosts` | The selected App Service hostname and any approved custom hostname, separated by `;`, without scheme/path |
| `CacheInvalidation__EnableDataVersionMonitor` | `true` |
| `CacheInvalidation__MonitorIntervalMinutes` | `15` (initial repository default) |

A Key Vault reference value has this form:

```text
@Microsoft.KeyVault(SecretUri=https://<vault-name>.vault.azure.net/secrets/<secret-name>/)
```

The **runtime** database secret has this form:

```text
Server=<mysql-server>.mysql.database.azure.com;Port=3306;Database=cya2_test;User ID=cya2_test_app;Password=<runtime-password>;SslMode=VerifyFull;
```

Verify the CA chain is trusted by App Service as well as by the setup machine. Arrange a deployed CA bundle/trust configuration if required by current Azure MySQL certificate guidance; validate its accessibility after every deployment. Do not weaken TLS verification to make it work.

Use **one** connection-string configuration path. This guide uses `ConnectionStrings__default` as an application setting. App Service's typed MySQL connection-string entry produces `MYSQLCONNSTR_default`, which this app explicitly gives precedence; remove any stale/conflicting typed entry or manually set `MYSQLCONNSTR_default` value. Local Visual Studio user secrets are not published to Azure.

Do not enable development authentication bypasses or verbose production logging. Retain the repository's upload limits (5,120,000 bytes per `.xlsx`, two retained previews/10 MB aggregate) unless a reviewed requirement changes them. There is no need to enable MySQL local infile loading for the standard donation import path.

## 7. Custom domain, HTTPS, and Google OAuth

### Bind the domain

For isolated testing using the App Service default HTTPS hostname, skip the custom-domain steps below and verify its HTTPS certificate. Otherwise:

1. In App Service **Custom domains**, add `<app.example.org>`.
2. At the domain's DNS provider, add the exact validation TXT and routing record displayed by the Portal. Typically a subdomain uses CNAME and the `asuid` verification record; apex domains may require an A record. Use the actual values shown for this web app, not an assumed `<name>.azurewebsites.net` format.
3. Complete hostname validation and binding. Keep ownership verification records in place to reduce dangling-domain takeover risk.
4. Under **Certificates**, create an App Service managed certificate if eligible, or upload/bind an approved certificate. Bind it to the custom hostname using SNI TLS.
5. Verify HTTPS and certificate hostname/chain validity before registering or testing production sign-in. Managed certificate issuance/renewal has DNS and access requirements; keep those satisfied.

For isolated testing, the App Service default HTTPS hostname can be used without a custom domain. Otherwise, decide the canonical staff hostname and bind its certificate as above. Use the chosen hostname consistently; avoid switching hosts during a login because cookies are host-specific.

### Configure Google Cloud

In Google Cloud Console (outside Azure Portal):

1. Configure the OAuth consent screen/branding and audience. For testing, add the approved test users if the Google application is in testing mode. Publish/verify it as required before any staff production access.
2. Create/select an OAuth **Web application** client.
3. Add the exact authorized redirect URI:

```text
https://<app.example.org>/signin-google
```

4. If the Google configuration asks for authorized JavaScript origins, use `https://<app.example.org>` without a path. This does not replace the redirect URI.
5. Store the client ID/secret in the previously configured Azure settings/Key Vault secrets. Register the App Service default hostname callback only if there is an approved need to sign in there too.

The app's sign-in entry point is `/api/login`; `/signin-google` is the OAuth middleware callback, not a page to visit manually. Authentication cookies are Secure/HttpOnly and expire after four hours. If Google reports an HTTP redirect URI or a wrong hostname, inspect App Service forwarding/host configuration before changing production callback security. Do not register an HTTP callback as a workaround.

## 8. Publish manually to Linux App Service

Build and test the reviewed revision locally and confirm the test database schema matches it. Verify the target is the test app, not an app serving real users. Publish only the web project output; never include credentials, workbooks, or the repository itself.

### Visual Studio

1. In Solution Explorer, right-click **src/Cya2.Web/cya2.csproj > Publish**.
2. Choose **Azure > Azure App Service (Linux)**, sign into the intended tenant/subscription, and select the existing test web app. Labels may vary with Visual Studio versions.
3. Use **Release**, `net10.0`, and framework-dependent deployment with no Windows-specific runtime identifier. Verify the target and publish output; exclude `appsettings.Development.json`, any workbooks, and any secrets. The app expects the .NET 10 runtime installed in the selected Linux stack.
4. Publish using an organization-approved authentication method. If the method requires SCM basic authentication, enable it only under the organization's policy and disable it again afterward. Never commit publish profiles containing credentials. Do not enable FTP/basic credentials globally as a shortcut.
5. Check App Service deployment and startup logs. Do not run imports while publishing or restarting.

### Publish folder and ZIP

Alternatively, create a fresh publish folder and ZIP from the repository root in PowerShell:

```powershell
$publish = Join-Path $env:TEMP ("cya2-publish-" + [guid]::NewGuid())
dotnet publish src/Cya2.Web/cya2.csproj -c Release -f net10.0 --no-self-contained -o $publish
if ($LASTEXITCODE -ne 0) { throw "Publish failed; do not deploy." }
Remove-Item (Join-Path $publish 'appsettings.Development.json') -Force -ErrorAction SilentlyContinue
```

Inspect the entire publish folder for workbooks, credentials, development-only files, and other unintended content before creating the archive. Then, from the repository root:

```powershell
New-Item -ItemType Directory -Path .\artifacts -Force | Out-Null
Compress-Archive -Path "$publish\*" -DestinationPath .\artifacts\cya2-linux-deploy.zip -Force
```

The archive must contain `cya2.dll` and the other published files at its root, not an enclosing publish folder. Deploy the ZIP using an approved App Service ZIP deployment method and check the deployment logs; a ZIP is not itself a GitHub integration. Protect and delete the temporary publish folder and archive according to your organization's policy. Do not start imports during deployment.

Do not enable GitHub deployment for this test unless separately approved. The repository has an existing workflow whose target and environment must be reviewed before it is connected to any app; neither manual publication nor the workflow initializes MySQL.

## 9. Bootstrap the first application Admin

1. Confirm the database schema is initialized, the bootstrap values are configured, and the `Users` table is empty.
2. Browse to the selected HTTPS hostname and sign in using exactly the Google email in `InitialAdminBootstrap__Email`.
3. The first successful matching sign-in creates the application Admin and links the Google identity. An arbitrary first visitor must not become Admin.
4. Confirm the Admin page opens. Use application user management for later users and assign least-privilege roles.
5. **Remove** both bootstrap environment variables from App Service and save. Remove/disable the temporary bootstrap secrets according to the secret policy. Restart only when no import is running, then sign out/in and verify the saved Admin still works without bootstrap settings.

Bootstrap is disabled after any `Users` row exists; failed bootstrap is not a reason to insert ad hoc production users or weaken sign-in checks. Verify the empty database, exact Google email, secret-reference resolution, and application logs first.

## 10. Populate and validate test data

Use synthetic test data only. Do not import real donor or accounting data until production use and its privacy controls are separately approved.

Using the application's Admin UI:

1. Create/import the primary account definitions, then verify Class, Fund Num, Overhead, and subfund-to-primary relationships. Follow the actual account-import UI; do not assume donation uploads create all account configuration.
2. Upload synthetic donation and accounting workbooks through the Admin UI, one import at a time. Never commit workbooks or package them with the deployment.
3. Reconcile imported row counts, date coverage, donation totals, account balances, donor identity/contact display, and merged/separate subfund behavior against the synthetic input and expected results.
4. Verify Fund References includes subfunds and inherits parent metadata; compare Accounts Overview with expected balances/donations.
5. Test Admin, Viewer, regular User, and Intern access through the UI using appropriately authorized test users. A Viewer must not gain Admin mutation access, and Intern selection must retain intern identity rather than a shared fund.
6. Check English/Spanish language selection, Google sign-out/in, mobile navigation, copy/export, and real-time import progress. Confirm WebSockets work (normally a `101` upgrade for the Blazor connection), not merely that the first page loads.
7. Confirm an unregistered Google account is denied and that a non-Admin cannot call import endpoints or see unauthorized financial data.

Do not announce go-live until reconciliation and access checks pass. The DB's Donors/DonorContacts are current-state display projections, not the organization's official donor database.

Account-definition and bulk-user workbooks are selected and uploaded through their respective Admin import sections; these are separate from donation/accounting uploads. Each workbook is limited to 10 MB and is validated in a preview before the administrator can create accounts or users. Uploaded workbooks are processed for the import and must not be included in deployment artifacts or stored as durable application data. Use synthetic workbooks for testing.

## 11. Operations, durability, and go-live checklist

### Secrets, data protection, and deployment safety

- The app does not configure an explicit Data Protection key store. On Linux, verify the actual key location, persistence and access permissions after restart and redeployment; do not assume keys survive an app/slot move or that cookies survive a slot swap. Review key protection at rest and configure an appropriate durable store separately if needed before production use or scale-out.
- Keep one instance and affinity. Do not add Azure SignalR and assume all caches/import queues become distributed.
- Schedule schema changes, deployment, and restarts outside imports. MySQL backups do not protect in-memory previews or queued work.
- Rotate runtime DB and OAuth secrets on a documented schedule; test new credentials, update Key Vault, refresh application configuration, and verify imports/sign-in after rotation.
- Confirm the organization's EPPlus noncommercial license eligibility; the repository configures its noncommercial organization license in code.

### Monitoring and backups

- In Portal **Monitoring**, configure restricted-access diagnostic collection and alerts for app failures, memory/CPU, MySQL storage/connection pressure, database availability, and failed deployments. App Service **Log stream** is useful during setup; disable temporary verbose logs afterward.
- Application Insights may require additional application instrumentation/configuration; do not assume creating an Insights resource alone instruments this repository. Review any automatic request telemetry for query strings/OAuth callback values before enabling or exporting it.
- General logs must exclude donor contact data, intern names, sensitive account values, credentials, OAuth codes/tokens, and workbook contents. Treat restricted DB audit events separately; their configured retention is 90 days. Apply appropriate access control and retention to Azure diagnostic sinks too.
- Azure MySQL automated backups/point-in-time restore are the recovery mechanism for the server. App-level import rollback snapshots are not a replacement. Backups can retain data beyond application-row retention; record that in the privacy/recovery policy.
- Perform a restore drill into a **separate isolated server**. Check schema, required audit data, account relationships and totals, then test the reconnection procedure without pointing production at an unverified restore. Store recovery instructions and subscription access securely.

### Final acceptance

- [ ] Correct subscription/resource group/region; budget and operational ownership assigned.
- [ ] Single Linux App Service worker, supported .NET 10 runtime, Always On, WebSockets, affinity, HTTPS, and a valid certificate for the selected hostname.
- [ ] Private MySQL DNS/network path works from App Service; no unrestricted public DB firewall rule.
- [ ] Database defaults verified as `utf8mb4` / `utf8mb4_0900_ai_ci`; separate empty test database initialized with the repository runner and required tables verified, with no migration-history expectation.
- [ ] Runtime user has data/temporary-table privileges only; setup/admin credential is not in App Service.
- [ ] Key Vault references resolve using managed identity; secrets are absent from source and publish artifacts.
- [ ] Selected-host Google login, bootstrap removal, role restrictions, and culture selection verified.
- [ ] Accounts/imports reconciled through the UI; sensitive logs checked.
- [ ] Backups, restore drill, alerts, and update process approved.
- [ ] Temporary setup VM/Bastion access/resources removed when no longer required; retain an approved future migration access path.

## 12. Troubleshooting

| Symptom | Checks |
| --- | --- |
| Database-limited mode or `/database-error` | Correct runtime secret and database name; private DNS/VNet integration; port 3306; TLS chain and hostname; runtime user permissions; schema initialization. Limited mode deliberately avoids crashing the whole app. |
| Key Vault reference remains unresolved | Managed identity enabled, Secrets User role scope/propagation, existing secret identifier, vault networking/DNS. Never copy secrets into tracked JSON to bypass this. |
| Google `redirect_uri_mismatch` or correlation failure | Exact custom HTTPS callback, consistent browser hostname, correct OAuth client, HTTPS/forwarding and cookie settings; do not switch hosts mid-login. |
| Failed startup | Linux .NET 10 runtime, ZIP contents at archive root, `cya2.dll`, App Service deployment/startup logs, and startup command if platform detection fails. Avoid public detailed error pages. |
| Static page works but interactions disconnect | WebSockets setting, browser/proxy policies, single worker/affinity, restarts, worker memory, and actual Blazor connection. |
| Donation import fails with MySQL error 1253 | Check the target database's default character set and collation using the read-only query in section 4. A non-`utf8mb4` default is a possible cause, not proof of the failing statement; reconcile import outcome before retrying and verify existing table collations separately. |
| Imports fail despite normal reads | `CREATE TEMPORARY TABLES` plus data permissions; schema compatibility; upload limits; no deployment/restart during import; inspect redacted diagnostics. Do not grant runtime schema-administrator rights. |
| Login/session fails after switching slots/apps | Data Protection key-ring/application isolation and host-specific cookies; sign in afresh. Sharing key rings/slots requires deliberate design. |

## Microsoft reference documentation

Portal labels, runtime availability, certificate chains, and service limits change. Verify these official references when executing the installation:

- [Create MySQL Flexible Server in the Portal](https://learn.microsoft.com/en-us/azure/mysql/flexible-server/quickstart-create-server-portal)
- [MySQL private networking](https://learn.microsoft.com/en-us/azure/mysql/flexible-server/concepts-networking-vnet)
- [MySQL TLS connectivity](https://learn.microsoft.com/en-us/azure/mysql/flexible-server/how-to-connect-tls-ssl)
- [App Service general configuration](https://learn.microsoft.com/en-us/azure/app-service/configure-common)
- [Deploy a ZIP package to App Service](https://learn.microsoft.com/en-us/azure/app-service/deploy-zip)
- [App Service VNet integration](https://learn.microsoft.com/en-us/azure/app-service/overview-vnet-integration)
- [App Service Key Vault references](https://learn.microsoft.com/en-us/azure/app-service/app-service-key-vault-references)
- [Custom domains](https://learn.microsoft.com/en-us/azure/app-service/app-service-web-tutorial-custom-domain)
- [App Service certificates](https://learn.microsoft.com/en-us/azure/app-service/configure-ssl-certificate)
- [Blazor Server hosting](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/server?view=aspnetcore-10.0)
- [Data Protection hosting defaults](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/default-settings?view=aspnetcore-10.0)
