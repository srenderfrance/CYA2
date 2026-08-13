# Copilot Instructions

## Project Guidelines
- For this Blazor application, use a two-year cache window. After Home's initial render, preload all accessible accounts only when the user has 20 or fewer accounts. Admin caching must be bounded and may preload Accounts Overview, last-12-month donation totals, and Fund References; do not preload all-account detail data.