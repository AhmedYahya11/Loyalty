/**
 * Hareer → Loyalty Platform event emitter (reference implementation).
 *
 * Drop-in module for the Hareer backend (Elysia/Bun). Call the emit* functions POST-COMMIT
 * from the matching Hareer service (payment success, refund success, signup, login). It is
 * fire-and-forget with a short retry and never throws into the caller's path.
 *
 * وحدة إرسال أحداث الولاء من حرير — تُستدعى بعد نجاح المعاملة (post-commit).
 */

const LOYALTY_API = process.env.LOYALTY_API ?? "http://localhost:8080";
const LOYALTY_API_KEY = process.env.LOYALTY_API_KEY ?? "";

type Json = Record<string, unknown>;

interface LoyaltyEnvelope {
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
  // UUIDv5-like stability without a dependency: hash seed into a uuid-shaped string.
  let h = 0;
  for (let i = 0; i < seed.length; i++) h = (Math.imul(31, h) + seed.charCodeAt(i)) | 0;
  const hex = (n: number) => (n >>> 0).toString(16).padStart(8, "0");
  const a = hex(h), b = hex(Math.imul(h, 2654435761)), c = hex(Math.imul(h ^ 0x9e3779b9, 40503));
  return `${a}-${b.slice(0, 4)}-5${b.slice(4, 7)}-8${c.slice(0, 3)}-${c.slice(3)}${a.slice(0, 4)}`;
}

async function post(envelope: LoyaltyEnvelope, idempotencyKey: string, attempt = 0): Promise<void> {
  try {
    const res = await fetch(`${LOYALTY_API}/api/v1/events`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Api-Key": LOYALTY_API_KEY,
        "Idempotency-Key": idempotencyKey,
      },
      body: JSON.stringify(envelope),
    });
    if (!res.ok && res.status >= 500 && attempt < 3) {
      await new Promise((r) => setTimeout(r, 200 * (attempt + 1)));
      return post(envelope, idempotencyKey, attempt + 1);
    }
    // 2xx/4xx are final; log 4xx for observability (do not throw into the caller)
    if (!res.ok) console.warn(`[loyalty] ${envelope.eventType} -> ${res.status} ${await res.text()}`);
  } catch (err) {
    if (attempt < 3) {
      await new Promise((r) => setTimeout(r, 200 * (attempt + 1)));
      return post(envelope, idempotencyKey, attempt + 1);
    }
    console.error(`[loyalty] failed to emit ${envelope.eventType}`, err);
  }
}

// ---- Hareer domain shapes (subset of prisma models) ----
interface HareerOrder { id: string; userId: string; nanoId?: string | null; payableAmount: number; subtotal: number; discountAmount: number; }
interface HareerRefund { id: string; amount: number; }

/** Eligible spend policy (business decision — default: payableAmount). */
export function eligibleSpend(order: HareerOrder): number {
  return order.payableAmount; // alt: order.subtotal - order.discountAmount
}

export function emitPurchaseCompleted(order: HareerOrder, occurredAt = new Date()): Promise<void> {
  const envelope: LoyaltyEnvelope = {
    eventId: stableId(`purchase:${order.id}`),
    eventType: "PurchaseCompleted",
    eventVersion: 1,
    sourceTransactionId: order.id,
    customerId: order.userId,
    occurredAt: occurredAt.toISOString(),
    correlationId: order.nanoId ?? order.id,
    attributes: { orderId: order.id, amount: eligibleSpend(order), currency: "IQD" },
  };
  return post(envelope, `hareer:purchase:${order.id}`);
}

export function emitOrderRefunded(order: HareerOrder, refund: HareerRefund, occurredAt = new Date()): Promise<void> {
  const envelope: LoyaltyEnvelope = {
    eventId: stableId(`refund:${refund.id}`),
    eventType: "OrderRefunded",
    eventVersion: 1,
    sourceTransactionId: refund.id,
    customerId: order.userId,
    occurredAt: occurredAt.toISOString(),
    correlationId: order.nanoId ?? order.id,
    attributes: { refundId: refund.id, originalOrderId: order.id, amount: refund.amount, currency: "IQD" },
  };
  return post(envelope, `hareer:refund:${refund.id}`);
}

export function emitCustomerRegistered(userId: string, channel = "app", occurredAt = new Date()): Promise<void> {
  const envelope: LoyaltyEnvelope = {
    eventId: stableId(`register:${userId}`),
    eventType: "CustomerRegistered",
    eventVersion: 1,
    sourceTransactionId: userId,
    customerId: userId,
    occurredAt: occurredAt.toISOString(),
    correlationId: userId,
    attributes: { registrationChannel: channel },
  };
  return post(envelope, `hareer:register:${userId}`);
}

export function emitApplicationLogin(userId: string, sessionId: string, channel = "app", occurredAt = new Date()): Promise<void> {
  const rewardDate = occurredAt.toISOString().slice(0, 10);
  const envelope: LoyaltyEnvelope = {
    eventId: stableId(`login:${userId}:${rewardDate}`),
    eventType: "ApplicationLogin",
    eventVersion: 1,
    sourceTransactionId: sessionId,
    customerId: userId,
    occurredAt: occurredAt.toISOString(),
    correlationId: sessionId,
    attributes: { channel, sessionId },
  };
  return post(envelope, `hareer:login:${userId}:${rewardDate}`);
}
