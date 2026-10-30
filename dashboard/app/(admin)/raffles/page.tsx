"use client";
import { useEffect, useState } from "react";
import { useLang } from "@/app/providers";
import { tr } from "@/lib/i18n";
import { api } from "@/lib/api";
import { isConnected } from "@/lib/session";

type Raffle = {
  id: string; name: string; nameAr?: string; triggerEventType: string; minAmount: number;
  entriesPerEvent: number; prizePoints: number; prizeDescription?: string; status: string;
  winnerMemberId?: string | null; totalEntries: number; participants: number;
};
type EntryRow = { memberId: string; externalCustomerId: string; entries: number };

const EVENTS = ["PurchaseCompleted", "CustomAction", "ApplicationLogin", "ReferralCompleted"];

export default function RafflesPage() {
  const { lang } = useLang();
  const [raffles, setRaffles] = useState<Raffle[]>([]);
  const [entries, setEntries] = useState<Record<string, EntryRow[]>>({});
  const [name, setName] = useState(""); const [trigger, setTrigger] = useState("PurchaseCompleted");
  const [minAmt, setMinAmt] = useState("50000"); const [per, setPer] = useState("1");
  const [prize, setPrize] = useState("1000"); const [prizeDesc, setPrizeDesc] = useState("");
  const [action, setAction] = useState("app.shared");
  const [err, setErr] = useState("");

  const load = async () => { try { setRaffles(await api.get<Raffle[]>("/api/v1/raffles")); } catch (e) { setErr((e as Error).message); } };
  useEffect(() => { if (isConnected()) load(); }, []);

  const create = async () => {
    setErr("");
    try {
      await api.post("/api/v1/raffles", {
        name, triggerEventType: trigger, minAmount: Number(minAmt) || 0, entriesPerEvent: Number(per) || 1,
        prizePoints: Number(prize) || 0, prizeDescription: prizeDesc || null,
        conditions: trigger === "CustomAction" && action.trim() ? [{ field: "attributes.action", operator: "Equals", value: action.trim() }] : [],
      });
      setName(""); setPrizeDesc(""); await load();
    } catch (e) { setErr((e as Error).message); }
  };
  const draw = async (id: string) => { setErr(""); try { await api.post(`/api/v1/raffles/${id}/draw`, {}); await load(); } catch (e) { setErr((e as Error).message); } };
  const del = async (id: string) => { try { await api.del(`/api/v1/raffles/${id}`); await load(); } catch (e) { setErr((e as Error).message); } };
  const showEntries = async (id: string) => { try { setEntries({ ...entries, [id]: (await api.get<{ items: EntryRow[] }>(`/api/v1/raffles/${id}/entries`)).items }); } catch (e) { setErr((e as Error).message); } };

  if (!isConnected()) return <p className="muted">{tr("notConnected", lang)}</p>;

  return (
    <>
      <div className="topbar"><h1>🎲 {tr("raffles", lang)}</h1></div>
      {err && <div className="err">{err}</div>}

      <div className="card">
        <div className="row">
          <div><label>{tr("name", lang)}</label><input value={name} onChange={(e) => setName(e.target.value)} placeholder="Ramadan draw" /></div>
          <div><label>{tr("triggerEvent", lang)}</label><select value={trigger} onChange={(e) => setTrigger(e.target.value)}>{EVENTS.map((x) => <option key={x}>{x}</option>)}</select></div>
          {trigger === "PurchaseCompleted" && <div><label>{tr("minAmount", lang)}</label><input value={minAmt} onChange={(e) => setMinAmt(e.target.value)} /></div>}
          {trigger === "CustomAction" && <div><label>action</label><input value={action} onChange={(e) => setAction(e.target.value)} placeholder="app.shared" /></div>}
          <div><label>{tr("entriesPerEvent", lang)}</label><input value={per} onChange={(e) => setPer(e.target.value)} /></div>
          <div><label>{tr("prizePoints", lang)}</label><input value={prize} onChange={(e) => setPrize(e.target.value)} /></div>
          <div><label>{tr("prizeDescription", lang)}</label><input value={prizeDesc} onChange={(e) => setPrizeDesc(e.target.value)} placeholder="iPhone 15" /></div>
        </div>
        <button onClick={create}>{tr("create", lang)}</button>
      </div>

      <table>
        <thead><tr><th>{tr("name", lang)}</th><th>{tr("triggerEvent", lang)}</th><th>{tr("entries", lang)}</th><th>{tr("participants2", lang)}</th><th>{tr("prizePoints", lang)}</th><th>{tr("status", lang)}</th><th></th></tr></thead>
        <tbody>{raffles.map((r) => (
          <tr key={r.id}>
            <td>🎲 {(lang === "ar" && r.nameAr ? r.nameAr : r.name)}{r.prizeDescription ? <div className="muted">{r.prizeDescription}</div> : null}</td>
            <td className="muted">{r.triggerEventType}{r.minAmount > 0 ? ` ≥ ${r.minAmount}` : ""}</td>
            <td><a onClick={() => showEntries(r.id)} style={{ cursor: "pointer" }}>{r.totalEntries}</a></td>
            <td>{r.participants}</td>
            <td>+{r.prizePoints}</td>
            <td>{r.status === "Open" ? <span className="badge ok">{tr("active", lang)}</span> : r.status === "Drawn" ? <span className="badge">{tr("drawn", lang)}</span> : <span className="badge">{r.status}</span>}
              {r.winnerMemberId ? <div className="muted">{tr("winner", lang)}: {r.winnerMemberId.slice(0, 8)}…</div> : null}</td>
            <td style={{ display: "flex", gap: 8 }}>
              {r.status === "Open" && <button className="secondary" style={{ margin: 0 }} onClick={() => draw(r.id)}>{tr("draw", lang)}</button>}
              <button className="secondary" style={{ margin: 0 }} onClick={() => del(r.id)}>{tr("delete", lang)}</button>
            </td>
          </tr>
        ))}</tbody>
      </table>

      {Object.entries(entries).map(([id, rows]) => rows.length > 0 && (
        <div key={id} className="card">
          <div className="muted">{tr("entries", lang)} — {raffles.find((r) => r.id === id)?.name}</div>
          <table><thead><tr><th>{tr("externalCustomerId", lang)}</th><th>{tr("entries", lang)}</th></tr></thead>
            <tbody>{rows.map((e) => <tr key={e.memberId}><td>{e.externalCustomerId}</td><td>{e.entries}</td></tr>)}</tbody></table>
        </div>
      ))}
    </>
  );
}
