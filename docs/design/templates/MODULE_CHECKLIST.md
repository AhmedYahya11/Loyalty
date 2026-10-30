# Module Checklist

- Keep the module namespace and database schema isolated.
- Add one feature folder per command or query.
- Keep domain code independent of EF Core, Kafka, Wolverine, Redis, and ASP.NET Core.
- Version integration contracts from the first release.
- Add idempotency for every external write.
- Test domain behavior with unit tests.
- Test PostgreSQL, Kafka, and Wolverine behavior with Testcontainers.
- Update architecture tests when a dependency is intentionally introduced.
