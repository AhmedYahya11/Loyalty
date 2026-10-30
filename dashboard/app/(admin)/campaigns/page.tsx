"use client";
import { useEffect, useState } from "react";
import { useLang } from "@/app/providers";
import { tr } from "@/lib/i18n";
import { api } from "@/lib/api";
import { isConnected } from "@/lib/session";

type App = { id: string; name: string };
type Campaign = { id: string; name: string; nameAr?: string; triggerEventType: string; priority: number; status: string };

export default function CampaignsPage() {
  const { lang } = useLang();
  const [connected, setConnected] = useState(true);
  const [apps, setApps] = useState<App[]>([]);
  const [appId, setAppId] = useState("");
  const [spend, setSpend] = useState("1000");
  const [expiryDays, setExpiryDays] = useState("0");
  const [settingsMsg, setSettingsMsg] = useState("");

  const [campaigns, setCampaigns] = useState<Campaign[]>([]);
  const [name, setName] = useState("Base Earn");
  const [nameAr, setNameAr] = useState("نقاط الشراء");
  const [points, setPoints] = useState("");
  const [budget, setBudget] = useState("");
  const [dailyLimit, setDailyLimit] = useState("");
  const [sim, setSim] = useState("");
  const [err, setErr] = useState("");

  const loadCampaigns = async () => {
    try { setCampaigns(await api.get<Campaign[]>("/api/v1/campaigns")); } catch (e) { setErr((e as Error).message); }
  };
  useEffect(() => {
    setConnected(isConnected());
    if (!isConnected()) return;
    api.get<App[]>("/api/v1/applications").then((a) => { setApps(a); if (a[0]) setAppId(a[0].id); }).catch(() => {});
    loadCampaigns();
  }, []);

  const saveSettings = async () => {
    setSettingsMsg(""); setErr("");
    try {
      await api.put(`/api/v1/applications/${appId}/loyalty-settings`, {
        pointsUnitCode: "POINT", baseCurrency: "IQD", spendAmountPerPoint: Number(spend),
        roundingPolicy: "Floor", loyaltyStatus: "Active", pointsExpiryDays: Number(expiryDays) || 0,
      });
      setSettingsMsg(tr("saved", lang));
    } catch (e) { setErr((e as Error).message); }
  };

  const expireNow = async () => {
    setSettingsMsg(""); setErr("");
    try { const r = await api.post<{ expired: number }>("/api/v1/admin/expire-points", {}); setSettingsMsg(`⌛ ${r.expired}`); }
    catch (e) { setErr((e as Error).message); }
  };

  const createCampaign = async () => {
    setErr("");
    try {
      const effects = points
        ? [{ effectId: "bonus", type: "AwardPoints", points: Number(points) }]
        : [{ effectId: "base-earn", type: "AwardBasePoints" }];
      await api.post("/api/v1/campaigns", {
        name, nameAr: nameAr || null, triggerEventType: "PurchaseCompleted", minimumEventVersion: 1,
        priority: 100, stackingPolicy: "Stackable", conditions: [], effects,
        globalBudget: budget ? Number(budget) : null,
        memberLimits: dailyLimit ? [{ period: "CalendarDay", maximumExecutions: Number(dailyLimit) }] : [],
      });
      await loadCampaigns();
    } catch (e) { setErr((e as Error).message); }
  };

  const act = async (id: string, action: "activate" | "pause") => {
    try {
      if (action === "activate") await api.post(`/api/v1/campaigns/${id}/validate`);
      await api.post(`/api/v1/campaigns/${id}/${action}`);
      await loadCampaigns();
    } catch (e) { setErr((e as Error).message); }
  };

  const simulate = async (id: string) => {
    setSim(""); setErr("");
    try {
      const res = await api.post<{ eligible: boolean; effects: { points: number }[] }>(`/api/v1/campaigns/${id}/simulate`, {
        eventType: "PurchaseCompleted", eventVersion: 1, customerId: "sim-customer",
        occurredAt: new Date().toISOString(), attributes: { orderId: "SIM-1", amount: 50000, currency: "IQD" },
      });
      const total = res.effects.reduce((s, e) => s + e.points, 0);
      setSim(`${id.slice(0, 8)}: eligible=${res.eligible}, points=${total} (50,000 IQD sample)`);
    } catch (e) { setErr((e as Error).message); }
  };

  if (!connected) return <p className="muted">{tr("notConnected", lang)}</p>;

  return (
    <>
      <div className="topbar"><h1>{tr("campaigns", lang)}</h1></div>

      <h2>{tr("loyaltySettings", lang)}</h2>
      <div className="card">
        <div className="row">
          <div>
            <label>{tr("applications", lang)}</label>
            <select value={appId} onChange={(e) => setAppId(e.target.value)}>
              {apps.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
            </select>
          </div>
          <div><label>{tr("spendPerPoint", lang)} (IQD)</label><input value={spend} onChange={(e) => setSpend(e.target.value)} /></div>
          <div><label>{tr("pointsExpiryDays", lang)}</label><input value={expiryDays} onChange={(e) => setExpiryDays(e.target.value)} /></div>
        </div>
        <div className="row">
          <button onClick={saveSettings} disabled={!appId}>{tr("save", lang)}</button>
          <button className="secondary" onClick={expireNow}>{tr("expirePoints", lang)}</button>
        </div>
        {settingsMsg && <div className="ok-msg">{settingsMsg}</div>}
      </div>

      <h2>{tr("create", lang)}</h2>
      <div className="card">
        <div className="row">
          <div><label>{tr("name", lang)}</label><input value={name} onChange={(e) => setName(e.target.value)} /></div>
          <div><label>{tr("nameAr", lang)}</label><input value={nameAr} onChange={(e) => setNameAr(e.target.value)} dir="rtl" /></div>
          <div><label>{tr("points", lang)} (blank = base earn)</label><input value={points} onChange={(e) => setPoints(e.target.value)} placeholder="base earn" /></div>
        </div>
        <div className="row">
          <div><label>Global budget (points, optional)</label><input value={budget} onChange={(e) => setBudget(e.target.value)} placeholder="unlimited" /></div>
          <div><label>Daily limit / member (optional)</label><input value={dailyLimit} onChange={(e) => setDailyLimit(e.target.value)} placeholder="unlimited" /></div>
        </div>
        <button onClick={createCampaign}>{tr("create", lang)}</button>
        {err && <div className="err">{err}</div>}
        {sim && <div className="ok-msg">{sim}</div>}
      </div>

      <table>
        <thead><tr><th>{tr("name", lang)}</th><th>{tr("trigger", lang)}</th><th>{tr("priority", lang)}</th><th>{tr("status", lang)}</th><th></th></tr></thead>
        <tbody>
          {campaigns.map((c) => (
            <tr key={c.id}>
              <td>{lang === "ar" && c.nameAr ? c.nameAr : c.name}</td>
              <td>{c.triggerEventType}</td>
              <td>{c.priority}</td>
              <td>{c.status === "Active" ? <span className="badge ok">{c.status}</span> : <span className="badge">{c.status}</span>}</td>
              <td style={{ display: "flex", gap: 8 }}>
                {c.status !== "Active"
                  ? <button className="secondary" style={{ margin: 0 }} onClick={() => act(c.id, "activate")}>{tr("activate", lang)}</button>
                  : <button className="secondary" style={{ margin: 0 }} onClick={() => act(c.id, "pause")}>{tr("pause", lang)}</button>}
                <button className="secondary" style={{ margin: 0 }} onClick={() => simulate(c.id)}>{tr("simulate", lang)}</button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}
