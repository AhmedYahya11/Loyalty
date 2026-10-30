#!/usr/bin/env node
/**
 * Runnable Hareer → Loyalty demo (no Hareer repo needed).
 *
 * Bootstraps the Loyalty API as if onboarding Hareer, then emits a Hareer-shaped
 * PurchaseCompleted event and prints the resulting wallet balance.
 *
 *   LOYALTY_API=http://localhost:8080 BOOTSTRAP_TOKEN=dev-bootstrap-token-change-me \
 *     node integration/hareer/mock-emitter.mjs
 */

const API = process.env.LOYALTY_API ?? "http://localhost:8080";
const BOOTSTRAP_TOKEN = process.env.BOOTSTRAP_TOKEN ?? "dev-bootstrap-token-change-me";

async function call(method, path, body, headers = {}) {
  const res = await fetch(`${API}${path}`, {
    method,
    headers: { "Content-Type": "application/json", ...headers },
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await res.text();
  const json = text ? JSON.parse(text) : null;
  if (!res.ok) throw new Error(`${method} ${path} -> ${res.status} ${text}`);
  return json;
}

async function main() {
  console.log(`→ Loyalty API: ${API}`);

  // 1. onboard Hareer (root application + root account)
  const boot = await call("POST", "/api/v1/bootstrap", { name: "Hareer", nameAr: "حرير" }, { "X-Bootstrap-Token": BOOTSTRAP_TOKEN });
  const appId = boot.application.id;
  const apiKey = boot.rootAccount?.apiKey;
  if (!apiKey) throw new Error("Root already bootstrapped; re-run against a fresh database to get the key.");
  const auth = { "X-Api-Key": apiKey };
  console.log(`✓ Application ${appId} (key ${boot.rootAccount.keyPrefix}…)`);

  // 2. loyalty settings: 1 point per 1000 IQD
  await call("PUT", `/api/v1/applications/${appId}/loyalty-settings`, {
    pointsUnitCode: "POINT", baseCurrency: "IQD", spendAmountPerPoint: 1000,
    roundingPolicy: "Floor", loyaltyStatus: "Active",
  }, auth);
  console.log("✓ Loyalty settings (1 point / 1000 IQD)");

  // 3. member (Hareer User.id)
  const member = await call("POST", "/api/v1/members", { externalCustomerId: "hareer-user-100" }, auth);
  console.log(`✓ Member ${member.memberId}`);

  // 4. base-earn campaign
  const campaign = await call("POST", "/api/v1/campaigns", {
    name: "Base Earn", nameAr: "نقاط الشراء",
    triggerEventType: "PurchaseCompleted", minimumEventVersion: 1, priority: 100, stackingPolicy: "Stackable",
    conditions: [], effects: [{ effectId: "base-earn", type: "AwardBasePoints" }],
  }, auth);
  await call("POST", `/api/v1/campaigns/${campaign.id}/validate`, {}, auth);
  await call("POST", `/api/v1/campaigns/${campaign.id}/activate`, {}, auth);
  console.log(`✓ Campaign ${campaign.id} active`);

  // 5. emit a Hareer purchase (50,000 IQD → 50 points)
  const orderId = "ORDER-" + Math.floor(Date.now() / 1000);
  const submit = await call("POST", "/api/v1/events", {
    eventId: crypto.randomUUID(),
    eventType: "PurchaseCompleted", eventVersion: 1,
    sourceTransactionId: orderId, customerId: "hareer-user-100",
    occurredAt: new Date().toISOString(), correlationId: orderId,
    attributes: { orderId, amount: 50000, currency: "IQD" },
  }, { ...auth, "Idempotency-Key": `hareer:purchase:${orderId}` });
  console.log(`✓ Event accepted: request ${submit.requestId}`);

  // 6. poll request status
  let status;
  for (let i = 0; i < 40; i++) {
    status = await call("GET", `/api/v1/requests/${submit.requestId}`, null, auth);
    if (["Completed", "Rejected", "Failed"].includes(status.status)) break;
    await new Promise((r) => setTimeout(r, 500));
  }
  console.log(`✓ Request ${status.status}, awarded ${status.awardedPoints ?? 0} ${status.pointsUnitCode ?? ""}`);

  // 7. wallet
  const wallet = await call("GET", `/api/v1/members/${member.memberId}/wallet`, null, auth);
  console.log(`✓ Wallet active balance: ${wallet.activeBalance} ${wallet.pointsUnitCode}`);
}

main().catch((e) => { console.error("✗", e.message); process.exit(1); });
