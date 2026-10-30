# Deviations from the MVP spec (Phase 1)

The authoritative design is `docs/design/MVP_IMPLEMENTATION_PLAN.md`. Phase 1 delivers the
**core pipeline vertical slice** (a purchase earns points, end to end) plus bilingual (AR/EN)
support and a Hareer integration contract. These are the intentional, documented deltas.

| Area | Spec | Phase 1 choice | Why / when |
|---|---|---|---|
| Rules engine | Microsoft RulesEngine behind `ILoyaltyRuleEvaluator` | Deterministic `OperatorRuleEvaluator` behind the same port | Faster, test-friendly slice. RulesEngine adapter drops in with no caller changes (P3). |
| Worker host | Separate `Loyalty.Worker` process | API host self-processes via a **durable local queue** (Wolverine + Postgres) | Cross-process delivery needs the Postgres transport; wired in P5. `Loyalty.Worker` is scaffolded. |
| Schema creation | EF migrations | `EnsureCreated` + idempotent balancing-trigger SQL at startup | Keeps the slice simple; switch to migrations before prod. |
| Campaign stacking | base + stackable bonuses × highest eligible multiplier (cross-campaign) | per-campaign-effect computation (base × multiplier, fixed bonuses) + exclusivity by priority | Full stacking/limits/budgets land with the campaign engine (P3). Single-campaign reference case (§4 → 273) already passes. |
| CVM (lifecycle + segments) | included in MVP | **not yet** — settings/profiles/segments/targeting | P2. Loyalty pipeline runs without CVM, as the spec allows. |
| Event types | Purchase, Refund, Register, Login, Referral, ManualAdjustment | **PurchaseCompleted** implemented end-to-end; others contracted (keys/attrs defined) | P2 adds the remaining typed contracts + projectors. |
| Result events | `PointsAwardedV1` outbox + consumers | not emitted yet | P2/P4 (webhooks/notifications). |
| Deferred balance trigger | deferred constraint trigger | implemented (see `DbBootstrap`) ✅ | — |
| Idempotency (4 boundaries) | HTTP key, source-event, business-event, effect/ledger | all four implemented ✅ | — |
| Bilingual AR/EN | not in spec | added: localized API errors (`Accept-Language`), `NameAr` on Application/Campaign, RTL-ready dashboard | user requirement |

## Roadmap
- **P2** CVM (value profile, lifecycle job, segments, audience targeting) + remaining event types + result events.
- **P3** Campaign engine (priority/stacking/exclusivity, member limits, global budget, validate/simulate) + RulesEngine adapter.
- **P4** Dashboard depth + analytics.
- **P5** Reliability/ops: Postgres transport (dedicated worker), retries/dead-letter ops, reconciliation, rate limits, OpenTelemetry, migrations, load tests.
- **P6** Hareer live integration (emit from the real repo, map tenants→applications, backfill).
