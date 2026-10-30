# Processing Flow

Detailed flowcharts for the MVP described in [`../MVP_IMPLEMENTATION_PLAN.md`](../MVP_IMPLEMENTATION_PLAN.md). Section references below point into that document.

- [1. Synchronous ingestion](#1-synchronous-ingestion)
- [2. Scope establishment](#2-scope-establishment)
- [3. Asynchronous processing](#3-asynchronous-processing)
- [4. Reward decision](#4-reward-decision)
- [5. Request status lifecycle](#5-request-status-lifecycle)
- [6. Idempotency checkpoints](#6-idempotency-checkpoints)

---

## 1. Synchronous ingestion

`POST /api/v1/events` in `Loyalty.Api`. Everything here must be fast and must commit the request row and the durable message together (§8, §16).

```mermaid
flowchart TD
    A["External system<br/>POST /api/v1/events"] --> B{"X-Api-Key valid?<br/>prefix lookup + constant-time hash"}
    B -->|No| R401["401<br/>invalid_credential"]
    B -->|Yes| C{"Account, application, and<br/>all ancestors active?"}
    C -->|No| R403["403<br/>credential_disabled"]
    C -->|Yes| T{"X-Application-Id sent?"}
    T -->|No| D["Target = credential's own application"]
    T -->|Yes| T2{"Inside VisibleApplicationIds?<br/>self or descendant"}
    T2 -->|No| R403b["403<br/>application_outside_subtree"]
    T2 -->|Yes| D
    D --> E{"Within rate and<br/>payload limits?"}
    E -->|No| R429["429<br/>rate_limited"]
    E -->|Yes| F{"EventType + EventVersion<br/>known to the registry?"}
    F -->|No| R400a["400<br/>unsupported_event_contract"]
    F -->|Yes| G{"Attributes valid for<br/>the typed contract?"}
    G -->|No| R400b["400<br/>validation_failed"]
    G -->|Yes| H{"ProgramCode resolves to an active<br/>program in this application?"}
    H -->|No| R400c["400<br/>unknown_program"]
    H -->|Yes| I["BEGIN TRANSACTION"]
    I --> J["INSERT IngestionRequest<br/>Status = Accepted"]
    J --> K{"Unique violation on<br/>ApplicationId + EventId?"}
    K -->|Yes| L["ROLLBACK<br/>load the original request"]
    L --> R202b["202<br/>original RequestId + Status"]
    K -->|No| M["Enqueue durable message<br/>through the EF Core outbox"]
    M --> N{"COMMIT succeeds?"}
    N -->|No| R503["503<br/>durable_acceptance_failed"]
    N -->|Yes| R202a["202<br/>RequestId"]

    style R202a fill:#1b5e20,color:#fff
    style R202b fill:#33691e,color:#fff
    style R503 fill:#b71c1c,color:#fff
    style R403b fill:#b71c1c,color:#fff
```

Two headers, with distinct jobs:

```http
X-Api-Key:        lp_live_7f3c…      required — authenticates the account
X-Application-Id: acme-baghdad       optional — which application owns the event
```

`X-Application-Id` is the explicit-request channel §7 requires for a parent writing on a descendant's behalf. Absent, or equal to the credential's own application, owns the event locally. A descendant is accepted and owns the event, member, wallet, and ledger rows. A parent, sibling, unrelated, or nonexistent ID returns `403`. The header may only point *down* the tree, tested against the same `VisibleApplicationIds` set that backs the query filter — so write authorization is exactly read authorization.

`AccountId` and `SourceSystem` always come from the credential and can never be overridden, whichever application is targeted.

Notes:

- The duplicate branch returns `202` with the original `RequestId`, not a `409`. A retrying caller should not be able to tell a first submission from a replay — that is the point of §9.
- Nothing is validated *after* the transaction opens. Every rejection is decided before the first write, so a rejected event never occupies a request row.
- `ProgramCode` is resolved against the **target** application — the one `X-Application-Id` names, or the credential's own when the header is absent.

---

## 2. Scope establishment

Both entry points converge on one `IApplicationContext`, so a query behaves identically in the API and in the worker (§7).

```mermaid
flowchart LR
    subgraph api["Loyalty.Api"]
        A1["HTTP request<br/>X-Api-Key header"] --> A2["ApiCredential lookup<br/>SYSTEM SCOPE"]
        A2 --> A3["ApplicationId<br/>AccountId"]
    end

    subgraph worker["Loyalty.Worker"]
        W1["Durable message"] --> W2["Read envelope headers"]
        W2 --> W3["ApplicationId<br/>AccountId"]
        W2 --> W4{"ApplicationId present<br/>and valid?"}
        W4 -->|No| W5["Reject message<br/>never guess scope"]
    end

    A3 --> R["Resolve VisibleApplicationIds<br/>from ApplicationClosure<br/>self + all descendants"]
    W3 --> R
    R --> CTX["IApplicationContext<br/>IsEstablished = true"]
    CTX --> QF["EF global query filter<br/>VisibleApplicationIds.Contains(e.ApplicationId)<br/>translates to = ANY(@ids)"]
    QF --> DB[("PostgreSQL")]

    SYS["System scope<br/>IsSystem = true"] -.->|"auth lookup · bootstrap · migrations"| DB

    style W5 fill:#b71c1c,color:#fff
    style SYS fill:#4a148c,color:#fff
```

The dashed path is the only filter bypass in the system. It is named, gated on `IsSystem`, limited to three call sites, and covered by architecture tests. `IgnoreQueryFilters` and raw SQL on scoped entities fail the build everywhere else (§7).

---

## 3. Asynchronous processing

One Wolverine handler, one database transaction (§16).

```mermaid
flowchart TD
    A["Durable message dequeued"] --> B["Restore scope from headers<br/>see diagram 2"]
    B --> C["Status = Processing"]
    C --> D["BEGIN TRANSACTION"]
    D --> E{"Member exists?<br/>ApplicationId + ProgramId + ExternalCustomerId"}
    E -->|No| F["Provision member + default wallet<br/>idempotent via unique index"]
    E -->|Yes| G["Load member + wallet"]
    F --> G
    G --> H["Select candidate campaigns<br/>TriggerEventType matches<br/>EventVersion >= MinimumEventVersion<br/>Status = Active · Schedule covers OccurredAt"]
    H --> I{"Any candidates?"}
    I -->|No| J["Status = Rejected<br/>reason = no_eligible_campaign"]
    I -->|Yes| K["Reward decision<br/>see diagram 4"]
    K --> L{"Any effects produced?"}
    L -->|No| J
    L -->|Yes| M["Write WalletTransaction rows<br/>append-only"]
    M --> N["Increment balance projection<br/>unconditional, same transaction"]
    N --> O["Persist RewardDecision + RewardEffect"]
    O --> P["Enqueue result event<br/>through the outbox"]
    P --> Q["Status = Completed"]
    Q --> Z["COMMIT"]
    J --> Z

    Z -.->|"transient failure"| RT{"Retries remaining?"}
    RT -->|Yes| RB["Status = Retrying<br/>bounded exponential backoff"]
    RB --> A
    RT -->|No| DL["Dead letter<br/>Status = DeadLettered"]
    DL --> FL["Status = Failed"]

    style J fill:#e65100,color:#fff
    style Q fill:#1b5e20,color:#fff
    style FL fill:#b71c1c,color:#fff
```

Notes:

- `Rejected` is a business outcome and is **not** retried. Only transient infrastructure failures re-enter the loop (§16).
- Request state, campaign usage, ledger, projection, and the outbox event all commit or all roll back. There is no window where the ledger has moved but the outbox has not.
- A retry re-runs the whole handler. Duplicate effects are prevented by the effect-level unique index (diagram 6), not by handler bookkeeping.

---

## 4. Reward decision

Pure computation over loaded state (§11, §12). No writes happen inside this box — it returns a set of typed effects that diagram 3 persists.

```mermaid
flowchart TD
    A["Candidate campaigns<br/>ordered by Priority desc, then Id"] --> B["Evaluate conditions<br/>ILoyaltyRuleEvaluator over the AST"]
    B --> C["Keep campaigns that matched"]
    C --> D{"Highest-priority match<br/>is exclusive?"}
    D -->|Yes| E["Discard all lower-priority campaigns"]
    D -->|No| F["Keep all stackable matches"]
    E --> G["Check member limits"]
    F --> G
    G --> H{"Limit reached<br/>for this member?"}
    H -->|Yes| I["Skip that campaign"]
    H -->|No| J["Reserve global budget<br/>UPDATE ... SET remaining = remaining - n<br/>WHERE remaining >= n"]
    J --> L{"Rows affected = 1?"}
    L -->|No| M["Budget exhausted<br/>skip that campaign"]
    L -->|Yes| K["Campaign is eligible"]
    I --> S["Eligible set finalized"]
    M --> S
    K --> S
    S --> N["base = round(amount / SpendAmountPerPoint)"]
    N --> O["multiplied = round(base × highest multiplier)"]
    O --> P["bonuses = sum of stackable fixed bonuses"]
    P --> Q["total = multiplied + bonuses"]
    Q --> R["Emit typed effects<br/>AwardPoints · ReversePoints<br/>AdjustPoints · NoReward"]

    style R fill:#0d47a1,color:#fff
```

The multiplier applies to the **base only**; bonuses are added afterwards and are never multiplied. Rounding uses the program's `RoundingPolicy` at both steps. The §11 reference case — `149.50` at `1.00` per point, `×1.5`, `+50` bonus, `Floor` — must yield exactly `273`.

Budget reservation is the one impure step: it is an atomic conditional `UPDATE` rather than read-modify-write, because optimistic concurrency on a hot campaign budget produces retry storms. It runs inside diagram 3's transaction, so a later rollback releases it.

---

## 5. Request status lifecycle

The statuses of §8, and which transitions are legal.

```mermaid
stateDiagram-v2
    [*] --> Accepted : request row committed
    Accepted --> Queued : durable message visible
    Queued --> Processing : handler picked it up
    Processing --> Completed : effects written
    Processing --> Rejected : no eligible campaign<br/>or business precondition failed
    Processing --> Retrying : transient failure
    Retrying --> Processing : backoff elapsed
    Retrying --> DeadLettered : retries exhausted
    DeadLettered --> Failed : operator or automatic finalization
    DeadLettered --> Queued : controlled replay

    Completed --> [*]
    Rejected --> [*]
    Failed --> [*]

    note right of Rejected
        Terminal and never retried.
        Carries StatusReason.
    end note

    note right of Failed
        Infrastructure gave up.
        Replay is safe: idempotency
        keys make a completed
        event a no-op.
    end note
```

---

## 6. Idempotency checkpoints

The three unique indexes of §9, and where each one fires.

```mermaid
flowchart TD
    subgraph sync["Synchronous — diagram 1"]
        A["INSERT IngestionRequest"] --> B{{"UNIQUE<br/>ApplicationId + EventId"}}
        B -->|Violation| C["Return the original<br/>RequestId and Status"]
    end

    subgraph business["Business identity"]
        D["Same purchase submitted<br/>under two different EventIds"] --> E{{"UNIQUE<br/>ApplicationId + SourceTransactionId<br/>+ EventType + EventVersion"}}
        E -->|Violation| F["One business effect,<br/>not two"]
    end

    subgraph effects["Asynchronous — diagram 3"]
        G["INSERT RewardEffect"] --> H{{"UNIQUE<br/>ApplicationId + MemberId + SourceEventId<br/>+ CampaignId + CampaignVersion + EffectId"}}
        H -->|Violation| I["Balance and campaign usage<br/>left untouched"]
    end

    J["Worker restart mid-handler"] --> G
    K["Wolverine redelivery"] --> G
    L["Controlled replay"] --> G

    style B fill:#4a148c,color:#fff
    style E fill:#4a148c,color:#fff
    style H fill:#4a148c,color:#fff
```

Every check is a database constraint. None is a `SELECT` followed by an `INSERT` — that pattern races between two concurrent identical retries, which is precisely the case idempotency exists to handle.

`CampaignVersion` is part of the effect key so that an edited campaign cannot collide with decisions produced by its own earlier version.

`ApplicationId` leads every key. Two applications may reuse the same `SourceTransactionId` without interfering.
