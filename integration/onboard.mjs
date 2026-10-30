#!/usr/bin/env node
/**
 * Generic onboarding CLI for the Loyalty platform — works for ANY customer/tenant.
 *
 *   # 1) one-time platform bootstrap: create the root application + root management key
 *   BOOTSTRAP_TOKEN=dev-bootstrap-token-change-me \
 *     node integration/onboard.mjs --bootstrap --name "Platform Root" --name-ar "الجذر"
 *
 *   # 2) onboard a customer (a child application) using the root key printed above
 *   ROOT_KEY=lk_live_… node integration/onboard.mjs --tenant "Acme Retail" --name-ar "أكمي" \
 *     --unit POINT --currency IQD --spend-per-point 1000
 *
 * Prints the tenant's own API key (store it in that customer's system as LOYALTY_API_KEY).
 */

const API = process.env.LOYALTY_API ?? "http://localhost:8080";

function arg(flag, fallback) {
  const i = process.argv.indexOf(flag);
  return i >= 0 && process.argv[i + 1] && !process.argv[i + 1].startsWith("--") ? process.argv[i + 1] : fallback;
}
const has = (flag) => process.argv.includes(flag);

async function call(method, path, body, headers = {}) {
  const res = await fetch(`${API}${path}`, {
    method,
    headers: { "Content-Type": "application/json", ...headers },
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await res.text();
  if (!res.ok) throw new Error(`${method} ${path} -> ${res.status} ${text}`);
  return text ? JSON.parse(text) : null;
}

async function bootstrap() {
  const token = process.env.BOOTSTRAP_TOKEN;
  if (!token) throw new Error("BOOTSTRAP_TOKEN env is required for --bootstrap");
  const name = arg("--name", "Platform Root");
  const nameAr = arg("--name-ar", null);
  const res = await call("POST", "/api/v1/bootstrap", { name, nameAr }, { "X-Bootstrap-Token": token });
  if (!res.rootAccount?.apiKey) {
    console.log(`Root already exists: application ${res.application.id}. Use its existing root key.`);
    return;
  }
  console.log("✓ Root application:", res.application.id);
  console.log("✓ ROOT_KEY (save this — root management key, shown once):");
  console.log("   " + res.rootAccount.apiKey);
}

async function onboardTenant() {
  const rootKey = process.env.ROOT_KEY;
  if (!rootKey) throw new Error("ROOT_KEY env is required to onboard a tenant");
  const auth = { "X-Api-Key": rootKey };

  const name = arg("--tenant");
  if (!name) throw new Error("--tenant <name> is required");
  const nameAr = arg("--name-ar", null);
  const unit = arg("--unit", "POINT");
  const currency = arg("--currency", "IQD");
  const spendPerPoint = Number(arg("--spend-per-point", "1000"));
  const welcomePoints = Number(arg("--welcome-points", "0"));

  // find the root application (parentId == null) that the root key can see
  const apps = await call("GET", "/api/v1/applications", null, auth);
  const root = apps.find((a) => !a.parentId);
  if (!root) throw new Error("No root application visible to ROOT_KEY");

  // create the tenant as a child application
  const tenant = await call("POST", "/api/v1/applications", { name, nameAr, parentId: root.id }, auth);
  console.log(`✓ Tenant application: ${tenant.id} (${name})`);

  // dedicated service account (events key) for this tenant
  const acct = await call("POST", `/api/v1/applications/${tenant.id}/accounts`, { accountName: `${name}-events` }, auth);
  const tenantKey = acct.apiKey;
  const tenantAuth = { "X-Api-Key": tenantKey };

  // loyalty + CVM settings
  await call("PUT", `/api/v1/applications/${tenant.id}/loyalty-settings`, {
    pointsUnitCode: unit, baseCurrency: currency, spendAmountPerPoint: spendPerPoint,
    roundingPolicy: "Floor", loyaltyStatus: "Active",
  }, auth);
  await call("PUT", `/api/v1/applications/${tenant.id}/cvm-settings`, {
    cvmStatus: "Active", newPeriodDays: 30, atRiskAfterDays: 30, dormantAfterDays: 90,
    churnedAfterDays: 180, highValueNetSpendThreshold: 1000000, frequentPurchaseThreshold: 5, evaluationWindowDays: 365,
  }, auth);
  console.log(`✓ Settings: 1 ${unit} per ${spendPerPoint} ${currency}; CVM active`);

  // base earn campaign on purchases
  const baseEarn = await call("POST", "/api/v1/campaigns", {
    name: "Base Earn", nameAr: "نقاط الشراء", triggerEventType: "PurchaseCompleted", minimumEventVersion: 1,
    priority: 100, stackingPolicy: "Stackable", conditions: [], effects: [{ effectId: "base-earn", type: "AwardBasePoints" }],
  }, tenantAuth);
  await call("POST", `/api/v1/campaigns/${baseEarn.id}/validate`, {}, tenantAuth);
  await call("POST", `/api/v1/campaigns/${baseEarn.id}/activate`, {}, tenantAuth);
  console.log("✓ Campaign: Base Earn (active)");

  // optional welcome campaign on registration
  if (welcomePoints > 0) {
    const welcome = await call("POST", "/api/v1/campaigns", {
      name: "Welcome", nameAr: "ترحيب", triggerEventType: "CustomerRegistered", minimumEventVersion: 1,
      priority: 100, stackingPolicy: "Stackable", conditions: [], effects: [{ effectId: "welcome", type: "AwardPoints", points: welcomePoints }],
    }, tenantAuth);
    await call("POST", `/api/v1/campaigns/${welcome.id}/validate`, {}, tenantAuth);
    await call("POST", `/api/v1/campaigns/${welcome.id}/activate`, {}, tenantAuth);
    console.log(`✓ Campaign: Welcome (+${welcomePoints} on registration)`);
  }

  console.log("");
  console.log("✓ LOYALTY_API_KEY for this tenant (store in their system, shown once):");
  console.log("   " + tenantKey);
  console.log("   application id: " + tenant.id);
}

async function main() {
  console.log(`→ Loyalty API: ${API}`);
  if (has("--bootstrap")) await bootstrap();
  else if (has("--tenant")) await onboardTenant();
  else {
    console.log("Usage:\n  --bootstrap --name <root>            (needs BOOTSTRAP_TOKEN)\n  --tenant <name> [--unit --currency --spend-per-point --welcome-points]   (needs ROOT_KEY)");
    process.exit(1);
  }
}

main().catch((e) => { console.error("✗", e.message); process.exit(1); });
