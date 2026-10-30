#!/usr/bin/env node
/**
 * Generic historical backfill for the Loyalty platform — replays past events from an EXPORT
 * FILE that the source system provides (JSON array). It never connects to a source database.
 *
 *   LOYALTY_API=http://localhost:8080 LOYALTY_API_KEY=lk_live_… \
 *     node integration/backfill.mjs --file orders.json \
 *       --event PurchaseCompleted \
 *       --map-customer userId --map-order id --map-amount payableAmount \
 *       --map-currency currency --map-occurred paidAt
 *
 * Idempotent: re-running with the same file is safe (business key = source + orderId).
 */
import { readFileSync } from "node:fs";
import { createHash } from "node:crypto";

const API = process.env.LOYALTY_API ?? "http://localhost:8080";
const KEY = process.env.LOYALTY_API_KEY;

function arg(flag, fallback) {
  const i = process.argv.indexOf(flag);
  return i >= 0 && process.argv[i + 1] ? process.argv[i + 1] : fallback;
}

function stableId(seed) {
  const h = createHash("sha256").update(seed).digest("hex");
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-5${h.slice(13, 16)}-8${h.slice(17, 20)}-${h.slice(20, 32)}`;
}

async function emit(envelope, idempotencyKey) {
  const res = await fetch(`${API}/api/v1/events`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Api-Key": KEY, "Idempotency-Key": idempotencyKey },
    body: JSON.stringify(envelope),
  });
  return { status: res.status, body: await res.text() };
}

async function main() {
  if (!KEY) throw new Error("LOYALTY_API_KEY env is required");
  const file = arg("--file");
  if (!file) throw new Error("--file <export.json> is required");
  const eventType = arg("--event", "PurchaseCompleted");
  const mapCustomer = arg("--map-customer", "userId");
  const mapOrder = arg("--map-order", "id");
  const mapAmount = arg("--map-amount", "payableAmount");
  const mapCurrency = arg("--map-currency", "currency");
  const mapOccurred = arg("--map-occurred", "occurredAt");
  const concurrency = Number(arg("--concurrency", "8"));

  const records = JSON.parse(readFileSync(file, "utf8"));
  if (!Array.isArray(records)) throw new Error("export file must be a JSON array");
  console.log(`→ ${API} · ${records.length} record(s) · event ${eventType}`);

  const counts = { accepted: 0, duplicate: 0, rejected: 0, error: 0 };

  async function handle(rec) {
    try {
      const orderId = String(rec[mapOrder]);
      const customerId = String(rec[mapCustomer]);
      const amount = Number(rec[mapAmount]);
      const currency = rec[mapCurrency] ?? "IQD";
      const occurredAt = rec[mapOccurred] ? new Date(rec[mapOccurred]).toISOString() : new Date().toISOString();
      if (!orderId || !customerId || Number.isNaN(amount)) { counts.error++; return; }

      const envelope = {
        eventId: stableId(`backfill:${eventType}:${orderId}`),
        eventType, eventVersion: 1, sourceTransactionId: orderId, customerId, occurredAt,
        correlationId: `backfill:${orderId}`,
        attributes: { orderId, amount, currency },
      };
      const { status, body } = await emit(envelope, `backfill:${eventType}:${orderId}`);
      if (status === 202) JSON.parse(body).duplicate ? counts.duplicate++ : counts.accepted++;
      else counts.rejected++;
    } catch { counts.error++; }
  }

  // simple bounded concurrency
  for (let i = 0; i < records.length; i += concurrency) {
    await Promise.all(records.slice(i, i + concurrency).map(handle));
    process.stdout.write(`\r  processed ${Math.min(i + concurrency, records.length)}/${records.length}`);
  }
  console.log("");
  console.log(`✓ accepted=${counts.accepted} duplicate=${counts.duplicate} rejected=${counts.rejected} error=${counts.error}`);
}

main().catch((e) => { console.error("✗", e.message); process.exit(1); });
