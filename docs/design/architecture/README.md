# Architecture

The platform starts as a modular monolith. Module boundaries are explicit so high-volume capabilities can be extracted later without changing their public contracts.

Critical state changes use PostgreSQL as the source of truth, Wolverine for handlers and reliable inbox/outbox processing, Kafka for integration events, and Redis only for cacheable projections.
