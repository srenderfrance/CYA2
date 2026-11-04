# CYA2

.NET 10 Blazor fundraising-account application using MySQL, Dapper and Radzen.

## Documentation

- [Production review and consolidated backlog](docs/PRODUCTION_REVIEW.md) — current findings and now/pre-live/ongoing work.
- [Local setup](docs/LOCAL_SETUP.md) and [Azure setup](docs/AZURE_SETUP.md).
- [Database schema and fixtures](database/README.md) and [MySQL integration tests](docs/DATABASE_TESTS.md).
- [Dependency security](docs/DEPENDENCY_SECURITY.md) and [health endpoints](docs/HEALTH_ENDPOINTS.md).
- [Cache architecture](docs/CACHE_ARCHITECTURE.md) and [Web cache supplement](src/Cya2.Web/docs/CACHE_ARCHITECTURE.md).

Use synthetic data for development and pre-live acceptance. Never commit secrets or real donor/accounting exports. A successful build or health response is not production-readiness approval.
