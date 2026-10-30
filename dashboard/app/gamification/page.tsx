"use client";
import { useEffect, useState } from "react";
import { useLang } from "../providers";
import { tr } from "@/lib/i18n";
import { api } from "@/lib/api";
import { isConnected } from "@/lib/session";

type Achievement = { id: string; name: string; nameAr?: string; metric: string; threshold: number; bonusPoints: number; isActive: boolean };
type Challenge = { id: string; name: string; metric: string; targetValue: number; bonusPoints: number; startsAtUtc: string; endsAtUtc: string; isActive: boolean };
type Row = { rank: number; externalCustomerId: string; value: number };
const METRICS = ["PurchaseCount", "NetSpend", "PointsEarned", "LoginCount", "ReferralCount", "Streak"];

export default function GamificationPage() {
  const { lang } = useLang();
  const [achievements, setAchievements] = useState<Achievement[]>([]);
  const [challenges, setChallenges] = useState<Challenge[]>([]);
  const [board, setBoard] = useState<Row[]>([]);
  const [boardMetric, setBoardMetric] = useState("PointsEarned");
  // achievement form
  const [aName, setAName] = useState(""); const [aMetric, setAMetric] = useState("PurchaseCount"); const [aThreshold, setAThreshold] = useState("1"); const [aBonus, setABonus] = useState("50");
  // challenge form
  const [cName, setCName] = useState(""); const [cMetric, setCMetric] = useState("PurchaseCount"); const [cTarget, setCTarget] = useState("5"); const [cBonus, setCBonus] = useState("100"); const [cDays, setCDays] = useState("30");
  const [err, setErr] = useState("");

  const loadAll = async () => {
    try {
      setAchievements(await api.get<Achievement[]>("/api/v1/achievements"));
      setChallenges(await api.get<Challenge[]>("/api/v1/challenges"));
      setBoard((await api.get<{ items: Row[] }>(`/api/v1/leaderboard?metric=${boardMetric}&limit=10`)).items);
    } catch (e) { setErr((e as Error).message); }
  };
  useEffect(() => { if (isConnected()) loadAll(); }, []);
  useEffect(() => { if (isConnected()) api.get<{ items: Row[] }>(`/api/v1/leaderboard?metric=${boardMetric}&limit=10`).then((r) => setBoard(r.items)).catch(() => {}); }, [boardMetric]);

  const createAchievement = async () => {
    setErr("");
    try { await api.post("/api/v1/achievements", { name: aName, metric: aMetric, threshold: Number(aThreshold), bonusPoints: Number(aBonus) }); setAName(""); await loadAll(); }
    catch (e) { setErr((e as Error).message); }
  };
  const createChallenge = async () => {
    setErr("");
    try {
      const now = new Date();
      await api.post("/api/v1/challenges", {
        name: cName, metric: cMetric, targetValue: Number(cTarget), bonusPoints: Number(cBonus),
        startsAtUtc: now.toISOString(), endsAtUtc: new Date(now.getTime() + Number(cDays) * 864e5).toISOString(),
      }); setCName(""); await loadAll();
    } catch (e) { setErr((e as Error).message); }
  };
  const delA = async (id: string) => { try { await api.del(`/api/v1/achievements/${id}`); await loadAll(); } catch (e) { setErr((e as Error).message); } };
  const delC = async (id: string) => { try { await api.del(`/api/v1/challenges/${id}`); await loadAll(); } catch (e) { setErr((e as Error).message); } };

  if (!isConnected()) return <p className="muted">{tr("notConnected", lang)}</p>;

  return (
    <>
      <div className="topbar"><h1>{tr("gamification", lang)}</h1></div>
      {err && <div className="err">{err}</div>}

      <h2>🏅 {tr("achievements", lang)}</h2>
      <div className="card">
        <div className="row">
          <div><label>{tr("name", lang)}</label><input value={aName} onChange={(e) => setAName(e.target.value)} placeholder="First Purchase" /></div>
          <div><label>{tr("metric", lang)}</label><select value={aMetric} onChange={(e) => setAMetric(e.target.value)}>{METRICS.map((m) => <option key={m}>{m}</option>)}</select></div>
          <div><label>{tr("threshold", lang)}</label><input value={aThreshold} onChange={(e) => setAThreshold(e.target.value)} /></div>
          <div><label>{tr("bonusPoints", lang)}</label><input value={aBonus} onChange={(e) => setABonus(e.target.value)} /></div>
        </div>
        <button onClick={createAchievement}>{tr("create", lang)}</button>
      </div>
      <table>
        <thead><tr><th>{tr("name", lang)}</th><th>{tr("metric", lang)}</th><th>{tr("threshold", lang)}</th><th>{tr("bonusPoints", lang)}</th><th></th></tr></thead>
        <tbody>{achievements.map((a) => (
          <tr key={a.id}><td>🏅 {a.name}</td><td>{a.metric}</td><td>{a.threshold}</td><td>+{a.bonusPoints}</td>
            <td><button className="secondary" style={{ margin: 0 }} onClick={() => delA(a.id)}>{tr("delete", lang)}</button></td></tr>
        ))}</tbody>
      </table>

      <h2>🎯 {tr("challenges", lang)}</h2>
      <div className="card">
        <div className="row">
          <div><label>{tr("name", lang)}</label><input value={cName} onChange={(e) => setCName(e.target.value)} placeholder="5 buys this month" /></div>
          <div><label>{tr("metric", lang)}</label><select value={cMetric} onChange={(e) => setCMetric(e.target.value)}>{METRICS.map((m) => <option key={m}>{m}</option>)}</select></div>
          <div><label>{tr("target", lang)}</label><input value={cTarget} onChange={(e) => setCTarget(e.target.value)} /></div>
          <div><label>{tr("bonusPoints", lang)}</label><input value={cBonus} onChange={(e) => setCBonus(e.target.value)} /></div>
          <div><label>Days</label><input value={cDays} onChange={(e) => setCDays(e.target.value)} /></div>
        </div>
        <button onClick={createChallenge}>{tr("create", lang)}</button>
      </div>
      <table>
        <thead><tr><th>{tr("name", lang)}</th><th>{tr("metric", lang)}</th><th>{tr("target", lang)}</th><th>{tr("bonusPoints", lang)}</th><th>{tr("status", lang)}</th><th></th></tr></thead>
        <tbody>{challenges.map((c) => (
          <tr key={c.id}><td>🎯 {c.name}</td><td>{c.metric}</td><td>{c.targetValue}</td><td>+{c.bonusPoints}</td>
            <td>{c.isActive ? <span className="badge ok">{tr("active", lang)}</span> : <span className="badge">—</span>}</td>
            <td><button className="secondary" style={{ margin: 0 }} onClick={() => delC(c.id)}>{tr("delete", lang)}</button></td></tr>
        ))}</tbody>
      </table>

      <h2>🏆 {tr("leaderboard", lang)}</h2>
      <div className="card">
        <label>{tr("metric", lang)}</label>
        <select value={boardMetric} onChange={(e) => setBoardMetric(e.target.value)} style={{ maxWidth: 240 }}>{METRICS.filter((m) => m !== "Streak" || true).map((m) => <option key={m}>{m}</option>)}</select>
      </div>
      <table>
        <thead><tr><th>#</th><th>{tr("externalCustomerId", lang)}</th><th>{boardMetric}</th></tr></thead>
        <tbody>{board.map((r) => (
          <tr key={r.rank}><td>{r.rank === 1 ? "🥇" : r.rank === 2 ? "🥈" : r.rank === 3 ? "🥉" : r.rank}</td><td>{r.externalCustomerId}</td><td>{r.value}</td></tr>
        ))}</tbody>
      </table>
    </>
  );
}
