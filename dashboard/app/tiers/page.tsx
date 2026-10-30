"use client";
import { useEffect, useState } from "react";
import { useLang } from "../providers";
import { tr } from "@/lib/i18n";
import { api } from "@/lib/api";
import { isConnected } from "@/lib/session";

type Tier = { id: string; name: string; nameAr?: string; rank: number; thresholdMetric: string; thresholdValue: number; earnMultiplier: number; color?: string; isActive: boolean };
const METRICS = ["NetSpend", "PointsEarned", "PurchaseCount"];

export default function TiersPage() {
  const { lang } = useLang();
  const [tiers, setTiers] = useState<Tier[]>([]);
  const [name, setName] = useState("");
  const [nameAr, setNameAr] = useState("");
  const [rank, setRank] = useState("0");
  const [metric, setMetric] = useState("NetSpend");
  const [threshold, setThreshold] = useState("0");
  const [mult, setMult] = useState("1.0");
  const [color, setColor] = useState("#cd7f32");
  const [err, setErr] = useState("");

  const load = () => api.get<Tier[]>("/api/v1/tiers").then(setTiers).catch((e) => setErr((e as Error).message));
  useEffect(() => { if (isConnected()) load(); }, []);

  const create = async () => {
    setErr("");
    try {
      await api.post("/api/v1/tiers", {
        name, nameAr: nameAr || null, rank: Number(rank), thresholdMetric: metric,
        thresholdValue: Number(threshold), earnMultiplier: Number(mult), color,
      });
      setName(""); setNameAr(""); await load();
    } catch (e) { setErr((e as Error).message); }
  };
  const remove = async (id: string) => { try { await api.del(`/api/v1/tiers/${id}`); await load(); } catch (e) { setErr((e as Error).message); } };

  if (!isConnected()) return <p className="muted">{tr("notConnected", lang)}</p>;

  return (
    <>
      <div className="topbar"><h1>{tr("tiers", lang)}</h1></div>
      <div className="card">
        <div className="row">
          <div><label>{tr("name", lang)}</label><input value={name} onChange={(e) => setName(e.target.value)} placeholder="Gold" /></div>
          <div><label>{tr("nameAr", lang)}</label><input value={nameAr} onChange={(e) => setNameAr(e.target.value)} dir="rtl" /></div>
          <div><label>{tr("rank", lang)}</label><input value={rank} onChange={(e) => setRank(e.target.value)} /></div>
        </div>
        <div className="row">
          <div><label>{tr("metric", lang)}</label><select value={metric} onChange={(e) => setMetric(e.target.value)}>{METRICS.map((m) => <option key={m}>{m}</option>)}</select></div>
          <div><label>{tr("threshold", lang)}</label><input value={threshold} onChange={(e) => setThreshold(e.target.value)} /></div>
          <div><label>{tr("multiplier", lang)}</label><input value={mult} onChange={(e) => setMult(e.target.value)} /></div>
          <div><label>Color</label><input value={color} onChange={(e) => setColor(e.target.value)} /></div>
        </div>
        <button onClick={create}>{tr("create", lang)}</button>
        {err && <div className="err">{err}</div>}
      </div>

      <table>
        <thead><tr><th>{tr("rank", lang)}</th><th>{tr("name", lang)}</th><th>{tr("metric", lang)}</th><th>{tr("threshold", lang)}</th><th>{tr("multiplier", lang)}</th><th></th></tr></thead>
        <tbody>
          {tiers.map((t) => (
            <tr key={t.id}>
              <td>{t.rank}</td>
              <td><span className="badge" style={{ background: t.color ?? undefined, color: t.color ? "#000" : undefined }}>{lang === "ar" && t.nameAr ? t.nameAr : t.name}</span></td>
              <td>{t.thresholdMetric}</td>
              <td>{t.thresholdValue}</td>
              <td>×{t.earnMultiplier}</td>
              <td><button className="secondary" style={{ margin: 0 }} onClick={() => remove(t.id)}>{tr("delete", lang)}</button></td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}
