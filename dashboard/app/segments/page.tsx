"use client";
import { useEffect, useState } from "react";
import { useLang } from "../providers";
import { tr } from "@/lib/i18n";
import { api } from "@/lib/api";
import { isConnected } from "@/lib/session";

type Segment = { id: string; name: string; nameAr?: string; status: string; currentVersion: number };
const FIELDS = ["lifecycleStage", "purchaseCount", "netSpendAmount", "loginCount", "referralCount", "pointsEarned"];
const OPS = ["Equals", "NotEquals", "GreaterThan", "GreaterThanOrEqual", "LessThan", "LessThanOrEqual", "In"];

export default function SegmentsPage() {
  const { lang } = useLang();
  const [segments, setSegments] = useState<Segment[]>([]);
  const [name, setName] = useState("New Customer");
  const [nameAr, setNameAr] = useState("عميل جديد");
  const [field, setField] = useState("lifecycleStage");
  const [op, setOp] = useState("Equals");
  const [value, setValue] = useState("New");
  const [err, setErr] = useState("");

  const load = async () => {
    try { setSegments(await api.get<Segment[]>("/api/v1/segments")); } catch (e) { setErr((e as Error).message); }
  };
  useEffect(() => { if (isConnected()) load(); }, []);

  const create = async () => {
    setErr("");
    try {
      await api.post("/api/v1/segments", { name, nameAr: nameAr || null, conditions: [{ field, operator: op, value }] });
      await load();
    } catch (e) { setErr((e as Error).message); }
  };
  const act = async (id: string, action: "validate" | "activate" | "pause") => {
    try { await api.post(`/api/v1/segments/${id}/${action}`); await load(); } catch (e) { setErr((e as Error).message); }
  };

  if (!isConnected()) return <p className="muted">{tr("notConnected", lang)}</p>;

  return (
    <>
      <div className="topbar"><h1>{tr("segments", lang)}</h1></div>
      <div className="card">
        <div className="row">
          <div><label>{tr("name", lang)}</label><input value={name} onChange={(e) => setName(e.target.value)} /></div>
          <div><label>{tr("nameAr", lang)}</label><input value={nameAr} onChange={(e) => setNameAr(e.target.value)} dir="rtl" /></div>
        </div>
        <div className="row">
          <div><label>{tr("field", lang)}</label><select value={field} onChange={(e) => setField(e.target.value)}>{FIELDS.map((f) => <option key={f}>{f}</option>)}</select></div>
          <div><label>{tr("operator", lang)}</label><select value={op} onChange={(e) => setOp(e.target.value)}>{OPS.map((o) => <option key={o}>{o}</option>)}</select></div>
          <div><label>{tr("value", lang)}</label><input value={value} onChange={(e) => setValue(e.target.value)} /></div>
        </div>
        <button onClick={create}>{tr("create", lang)}</button>
        {err && <div className="err">{err}</div>}
      </div>

      <table>
        <thead><tr><th>{tr("name", lang)}</th><th>{tr("status", lang)}</th><th></th></tr></thead>
        <tbody>
          {segments.map((s) => (
            <tr key={s.id}>
              <td>{lang === "ar" && s.nameAr ? s.nameAr : s.name}</td>
              <td>{s.status === "Active" ? <span className="badge ok">{s.status}</span> : <span className="badge">{s.status}</span>}</td>
              <td style={{ display: "flex", gap: 8 }}>
                {s.status === "Draft" && <button className="secondary" style={{ margin: 0 }} onClick={() => act(s.id, "validate")}>{tr("validate", lang)}</button>}
                {s.status !== "Active" && <button className="secondary" style={{ margin: 0 }} onClick={() => act(s.id, "activate")}>{tr("activate", lang)}</button>}
                {s.status === "Active" && <button className="secondary" style={{ margin: 0 }} onClick={() => act(s.id, "pause")}>{tr("pause", lang)}</button>}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}
