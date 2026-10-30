/**
 * Provider-agnostic Loyalty event emitter TEMPLATE.
 *
 * Copy into any source system (Node/Bun/TS) and call the emit* functions POST-COMMIT from the
 * relevant hooks (order paid, refund succeeded, customer registered, logged in). It is
 * fire-and-forget with a short retry and never throws into the caller's path.
 *
 * Configure via env: LOYALTY_API, LOYALTY_API_KEY, LOYALTY_ENABLED.
 * See integration/README.md for the event contract. A worked example lives in integration/hareer/.
 */

const API = process.env.LOYALTY_API ?? "http://localhost:8080";
const KEY = process.env.LOYALTY_API_KEY ?? "";
const ENABLED = (process.env.LOYALTY_ENABLED ?? "false") === "true";

type Json = Record<string, unknown>;
interface Envelope {
  eventId: string;
  eventType: string;
  eventVersion: number;
  sourceTransactionId: string;
  customerId: string;
  occurredAt: string;
  correlationId: string;
  attributes: Json;
}

/** Deterministic eventId per business event (stable across retries). */
function stableId(seed: string): string {
  let h = 0;
  for (let i = 0; i < seed.length; i++) h = (Math.imul(31, h) + seed.charCodeAt(i)) | 0;
  const hex = (n: number) => (n >>> 0).toString(16).padStart(8, "0");
  const a = hex(h), b = hex(Math.imul(h, 2654435761)), c = hex(Math.imul(h ^ 0x9e3779b9, 40503));
  return `${a}-${b.slice(0, 4)}-5${b.slice(4, 7)}-8${c.slice(0, 3)}-${c.slice(3)}${a.slice(0, 4)}`;
}

const delay = (ms: number) => new Promise((r) => setTimeout(r, ms));

async function post(envelope: Envelope, idempotencyKey: string, attempt = 0): Promise<void> {
  if (!ENABLED || !KEY) return; // inert unless configured
  try {
    const res = await fetch(`${API}/api/v1/events`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Api-Key": KEY, "Idempotency-Key": idempotencyKey },
      body: JSON.stringify(envelope),
    });
    if (!res.ok && res.status >= 500 && attempt < 3) { await delay(200 * (attempt + 1)); return post(envelope, idempotencyKey, attempt + 1); }
    if (!res.ok) console.warn(`[loyalty] ${envelope.eventType} -> ${res.status}`);
  } catch (err) {
    if (attempt < 3) { await delay(200 * (attempt + 1)); return post(envelope, idempotencyKey, attempt + 1); }
    console.error(`[loyalty] failed to emit ${envelope.eventType}`, err);
  }
}

export function emitPurchaseCompleted(p: { orderId: string; customerId: string; amount: number; currency?: string; correlationId?: string; occurredAt?: Date }): Promise<void> {
  return post({
    eventId: stableId(`purchase:${p.orderId}`), eventType: "PurchaseCompleted", eventVersion: 1,
    sourceTransactionId: p.orderId, customerId: p.customerId, occurredAt: (p.occurredAt ?? new Date()).toISOString(),
    correlationId: p.correlationId ?? p.orderId, attributes: { orderId: p.orderId, amount: p.amount, currency: p.currency ?? "IQD" },
  }, `purchase:${p.orderId}`);
}

export function emitOrderRefunded(p: { refundId: string; originalOrderId: string; customerId: string; amount: number; currency?: string }): Promise<void> {
  return post({
    eventId: stableId(`refund:${p.refundId}`), eventType: "OrderRefunded", eventVersion: 1,
    sourceTransactionId: p.refundId, customerId: p.customerId, occurredAt: new Date().toISOString(),
    correlationId: p.originalOrderId, attributes: { refundId: p.refundId, originalOrderId: p.originalOrderId, amount: p.amount, currency: p.currency ?? "IQD" },
  }, `refund:${p.refundId}`);
}

export function emitCustomerRegistered(p: { customerId: string; channel?: string }): Promise<void> {
  return post({
    eventId: stableId(`register:${p.customerId}`), eventType: "CustomerRegistered", eventVersion: 1,
    sourceTransactionId: p.customerId, customerId: p.customerId, occurredAt: new Date().toISOString(),
    correlationId: p.customerId, attributes: { registrationChannel: p.channel ?? "app" },
  }, `register:${p.customerId}`);
}

export function emitApplicationLogin(p: { customerId: string; sessionId: string; channel?: string }): Promise<void> {
  const rewardDate = new Date().toISOString().slice(0, 10);
  return post({
    eventId: stableId(`login:${p.customerId}:${rewardDate}`), eventType: "ApplicationLogin", eventVersion: 1,
    sourceTransactionId: p.sessionId, customerId: p.customerId, occurredAt: new Date().toISOString(),
    correlationId: p.sessionId, attributes: { channel: p.channel ?? "app", sessionId: p.sessionId },
  }, `login:${p.customerId}:${rewardDate}`);
}
