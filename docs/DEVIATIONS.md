# Deviations from the MVP spec (Phases 1–3)

The authoritative design is `docs/design/MVP_IMPLEMENTATION_PLAN.md`. Phase 1 = core pipeline;
Phase 2 = CVM (value profiles, lifecycle, segments, audience targeting, all event types);
Phase 3 = **full campaign engine** (cross-campaign stacking, member limits, global budgets,
validate/simulate), the **RulesEngine adapter**, and **refund reversal**. Bilingual (AR/EN)
throughout. These are the intentional, documented deltas.

## Phase 3 status (done)
- Cross-campaign stacking: one base × highest eligible multiplier + summed stackable bonuses ✅
  (reference case 149.50 ×1.5 +50 → 273; base counted once across campaigns)
- Member limits via CampaignUsage (CalendarDay/Month/AllTime), enforced + incremented ✅
- Global budget with atomic conditional reservation (`UPDATE … WHERE remaining >= n`) ✅
- Campaign validate + simulate; activation requires Validated ✅
- Refund reversal: compensating ledger Reverse of the original earn (idempotent) ✅
- Microsoft RulesEngine adapter behind the same `ILoyaltyRuleEvaluator` port, toggled by
  `LOYALTY_RULES_ENGINE=rulesengine` (default remains the deterministic operator evaluator);
  parity unit-tested ✅

### Phase 3 remaining deltas
| Area | Spec | Phase 3 choice | When |
|---|---|---|---|
| Budget attribution under stacking | budget charged on the campaign's issued points | per-campaign independent points for budget/usage; member award uses combined stacking | acceptable approximation; revisit if needed |
| CVM/segment-version snapshot | freeze on request / store in RewardDecision | still resolved live; RewardDecision not persisted as a row | P4 |
| Simulate | 202-style + full pipeline | synchronous single-campaign eligibility + computed effects | fine for admin preview |

---

## Phases 1–2 deltas

## Phase 2 status (done)
- ApplicationCvmSettings with nearest-ancestor inheritance ✅
- MemberValueProfile + projectors (Register/Login/Purchase/Refund/Referral, OccurredAt min/max) ✅
- Deterministic lifecycle evaluation (per-event) + daily inactivity sweep job + admin trigger ✅
- Rule-based segments (definition/version/membership) + activation + membership transitions ✅
- Campaign audience targeting (lifecycle + required/excluded segments), enforced in reward decision ✅
- All six event types registered with business keys; ManualAdjustment posts a signed Adjust ledger entry ✅

### Phase 2 remaining deltas
| Area | Spec | Phase 2 choice | When |
|---|---|---|---|
| Segment activation | 202 + background batch evaluation | synchronous activation (evaluates existing profiles inline) | fine at current scale; async batch when member bases are large |
| CVM snapshot | freeze effective CVM settings on the request | CVM resolved live at processing time | freeze alongside loyalty snapshot in P3 |
| Lifecycle sweep | all operational apps | apps with their own active CVM settings (inherited-only descendants covered on next event) | full inherited sweep later |
| Refund effect | compensating ledger reversal of the original earn | profile net-spend projection only (no auto points reversal) | reversal-by-original-tx in P3 |
| Segment versioning in campaigns | store versioned segment refs in CampaignVersion at activation | store segment ids; evaluate against current active memberships | versioned snapshot in P3 |
| Result events | MemberLifecycleChangedV1 / PointsAwardedV1 outbox | not emitted (no consumer yet) | P4 (webhooks/notifications) |

---

## Phase 1 deltas

| Area | Spec | Phase 1 choice | Why / when |
|---|---|---|---|
| Rules engine | Microsoft RulesEngine behind `ILoyaltyRuleEvaluator` | Deterministic `OperatorRuleEvaluator` behind the same port | Faster, test-friendly slice. RulesEngine adapter drops in with no caller changes (P3). |
| Worker host | Separate `Loyalty.Worker` process | API host self-processes via a **durable local queue** (Wolverine + Postgres) | Cross-process delivery needs the Postgres transport; wired in P5. `Loyalty.Worker` is scaffolded. |
| Schema creation | EF migrations | `EnsureCreated` + idempotent balancing-trigger SQL at startup | Keeps the slice simple; switch to migrations before prod. |
| Campaign stacking | base + stackable bonuses × highest eligible multiplier (cross-campaign) | per-campaign-effect computation (base × multiplier, fixed bonuses) + exclusivity by priority | Full stacking/limits/budgets land with the campaign engine (P3). Single-campaign reference case (§4 → 273) already passes. |
| CVM (lifecycle + segments) | included in MVP | implemented in P2 ✅ | see Phase 2 status above |
| Event types | Purchase, Refund, Register, Login, Referral, ManualAdjustment | all six implemented in P2 ✅ | — |
| Result events | `PointsAwardedV1` outbox + consumers | not emitted yet | P2/P4 (webhooks/notifications). |
| Deferred balance trigger | deferred constraint trigger | implemented (see `DbBootstrap`) ✅ | — |
| Idempotency (4 boundaries) | HTTP key, source-event, business-event, effect/ledger | all four implemented ✅ | — |
| Bilingual AR/EN | not in spec | added: localized API errors (`Accept-Language`), `NameAr` on Application/Campaign, RTL-ready dashboard | user requirement |

## Roadmap
- **P2** CVM (value profile, lifecycle job, segments, audience targeting) + remaining event types. ✅ DONE
- **P3** Campaign engine (stacking, member limits, global budget, validate/simulate) + RulesEngine adapter + refund reversal. ✅ DONE (CVM/segment-version snapshots moved to P4).
- **P4** Dashboard depth + analytics + CVM/segment-version snapshots + result events.
- **P5** Reliability/ops: Postgres transport (dedicated worker), retries/dead-letter ops, reconciliation, rate limits, OpenTelemetry, migrations, load tests.
- **P6** Hareer live integration (emit from the real repo, map tenants→applications, backfill).
