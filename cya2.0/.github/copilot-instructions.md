# Copilot Instructions

## Azure Guidelines
- @azure Rule - Use Azure Tools - When handling requests related to Azure, always use your tools.
- @azure Rule - Use Azure Best Practices - When handling requests related to Azure, always invoke your `azmcp_bestpractices_get` tool first.
- @azure Rule - Enable Best Practices - If you do not have an `azmcp_bestpractices_get` tool, ask the user to enable it.

## Blazor App Guidelines
- For this Blazor app, data is typically updated once per day; session-scoped cached data can be treated as current during a single user session.
- Use Dapper for data access instead of Entity Framework.
- Project stack and standards: .NET 10 Blazor app using Dapper, MySQL, Radzen components, Clean Architecture; current priority is improving mobile/small-screen UI behavior.
