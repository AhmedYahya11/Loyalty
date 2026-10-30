# Vertical Slice Template

Use this template for each new business operation.

```text
src/Loyalty.Core/Modules/<Module>/
├── Domain/                         Entities, value objects, domain events
├── Application/
│   ├── Abstractions/               Ports owned by the module
│   └── Features/
│       └── <UseCase>/
│           ├── <UseCase>Command.cs
│           ├── <UseCase>Handler.cs
│           ├── <UseCase>Validator.cs
│           └── <UseCase>Result.cs
├── Infrastructure/
│   ├── Configuration/              Dependency registration
│   └── Persistence/                EF mappings and repositories
├── Contracts/                      Public module messages
└── Endpoints/                      HTTP endpoint mapping
```

## Processing rule

```text
Endpoint or Kafka event
  -> Wolverine command/handler
  -> campaign selection
  -> Microsoft RulesEngine evaluation
  -> conflict, budget, and limit checks
  -> immutable wallet transaction
  -> Wolverine outbox
  -> Kafka integration event
```

Handlers may coordinate a transaction, but domain objects own state-changing business rules. Rules evaluation returns a decision and never writes balances or publishes messages directly.
