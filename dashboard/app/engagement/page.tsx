"use client";
import { useEffect, useState } from "react";
import { useLang } from "../providers";
import { tr } from "@/lib/i18n";
import { api } from "@/lib/api";
import { isConnected } from "@/lib/session";

type Settings = { referralEnabled: boolean; referrerPoints: number; refereePoints: number; birthdayEnabled: boolean; birthdayBonusPoints: number };
type StampCard = { id: string; name: string; nameAr?: string; triggerEventType: string; stampsRequired: number; rewardPoints: number; isActive: boolean };

export default function EngagementPage() {
  const { lang } = useLang();
  const [appId, setAppId] = useState("");
  const [s, setS] = useState<Settings>({ referralEnabled: false, referrerPoints: 0, refereePoints: 0, birthdayEnabled: false, birthdayBonusPoints: 0 });
  const [cards, setCards] = useState<StampCard[]>([]);
  // stamp-card form
  const [cName, setCName] = useState(""); const [cReq, setCReq] = useState("10"); const [cPts, setCPts] = useState("100");
  const [msg, setMsg] = useState(""); const [err, setErr] = useState("");

  const loadAll = async () => {
    try {
      const me = await api.get<{ applicationId: string }>("/api/v1/me");
      setAppId(me.applicationId);
      setS(await api.get<Settings>(`/api/v1/applications/${me.applicationId}/engagement-settings`));
      setCards(await api.get<StampCard[]>("/api/v1/stamp-cards"));
    } catch (e) { setErr((e as Error).message); }
  };
  useEffect(() => { if (isConnected()) loadAll(); }, []);

  const saveSettings = async () => {
    setErr(""); setMsg("");
    try { await api.put(`/api/v1/applications/${appId}/engagement-settings`, s); setMsg("✓"); }
    catch (e) { setErr((e as Error).message); }
  };
  const addCard = async () => {
    setErr("");
    try { await api.post("/api/v1/stamp-cards", { name: cName, stampsRequired: Number(cReq), rewardPoints: Number(cPts), triggerEventType: "PurchaseCompleted" }); setCName(""); await loadAll(); }
    catch (e) { setErr((e as Error).message); }
  };
  const delCard = async (id: string) => { try { await api.del(`/api/v1/stamp-cards/${id}`); await loadAll(); } catch (e) { setErr((e as Error).message); } };
  const runSweep = async () => {
    setErr(""); setMsg("");
    try { const r = await api.post<{ rewarded: number }>("/api/v1/admin/birthday-sweep", {}); setMsg(`🎂 ${r.rewarded}`); }
    catch (e) { setErr((e as Error).message); }
  };

  if (!isConnected()) return <p className="muted">{tr("notConnected", lang)}</p>;

  return (
    <>
      <div className="topbar"><h1>{tr("engagement", lang)}</h1></div>
      {err && <div className="err">{err}</div>}
      {msg && <div className="badge ok">{msg}</div>}

      <h2>🎁 {tr("referralProgram", lang)}</h2>
      <div className="card">
        <div className="row" style={{ alignItems: "end" }}>
          <label style={{ display: "flex", gap: 8, alignItems: "center" }}>
            <input type="checkbox" checked={s.referralEnabled} onChange={(e) => setS({ ...s, referralEnabled: e.target.checked })} /> {tr("enabled", lang)}
          </label>
          <div><label>{tr("referrerPoints", lang)}</label><input value={s.referrerPoints} onChange={(e) => setS({ ...s, referrerPoints: Number(e.target.value) || 0 })} /></div>
          <div><label>{tr("refereePoints", lang)}</label><input value={s.refereePoints} onChange={(e) => setS({ ...s, refereePoints: Number(e.target.value) || 0 })} /></div>
        </div>
      </div>

      <h2>🎂 {tr("birthdayReward", lang)}</h2>
      <div className="card">
        <div className="row" style={{ alignItems: "end" }}>
          <label style={{ display: "flex", gap: 8, alignItems: "center" }}>
            <input type="checkbox" checked={s.birthdayEnabled} onChange={(e) => setS({ ...s, birthdayEnabled: e.target.checked })} /> {tr("enabled", lang)}
          </label>
          <div><label>{tr("bonusPointsLabel", lang)}</label><input value={s.birthdayBonusPoints} onChange={(e) => setS({ ...s, birthdayBonusPoints: Number(e.target.value) || 0 })} /></div>
          <button onClick={saveSettings}>{tr("save", lang)}</button>
          <button className="secondary" onClick={runSweep}>{tr("runBirthdaySweep", lang)}</button>
        </div>
      </div>

      <h2>🎟️ {tr("stampCards", lang)}</h2>
      <div className="card">
        <div className="row" style={{ alignItems: "end" }}>
          <div><label>{tr("name", lang)}</label><input value={cName} onChange={(e) => setCName(e.target.value)} placeholder="Buy 10 get bonus" /></div>
          <div><label>{tr("stampsRequired", lang)}</label><input value={cReq} onChange={(e) => setCReq(e.target.value)} /></div>
          <div><label>{tr("rewardPoints", lang)}</label><input value={cPts} onChange={(e) => setCPts(e.target.value)} /></div>
          <button onClick={addCard}>{tr("create", lang)}</button>
        </div>
      </div>
      <table>
        <thead><tr><th>{tr("name", lang)}</th><th>{tr("triggerEvent", lang)}</th><th>{tr("stampsRequired", lang)}</th><th>{tr("rewardPoints", lang)}</th><th></th></tr></thead>
        <tbody>{cards.map((c) => (
          <tr key={c.id}><td>🎟️ {(lang === "ar" && c.nameAr ? c.nameAr : c.name)}</td><td className="muted">{c.triggerEventType}</td><td>{c.stampsRequired}</td><td>+{c.rewardPoints}</td>
            <td><button className="secondary" style={{ margin: 0 }} onClick={() => delCard(c.id)}>{tr("delete", lang)}</button></td></tr>
        ))}</tbody>
      </table>
    </>
  );
}
