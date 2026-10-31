"use client";
import { useEffect, useState } from "react";
import { useLang } from "@/app/providers";
import { tr } from "@/lib/i18n";
import { api } from "@/lib/api";
import { isConnected } from "@/lib/session";

type Customer = {
  memberId: string; externalCustomerId: string; status: string; lifecycle?: string | null;
  tier?: string | null; persona?: string | null; pointsEarned: number; netSpend: number;
  activeBalance: number; createdAt: string;
};

export default function CustomersPage() {
  const { lang } = useLang();
  const [rows, setRows] = useState<Customer[]>([]);
  const [search, setSearch] = useState("");
  const [err, setErr] = useState("");

  const load = async (s = "") => {
    try { setRows((await api.get<{ items: Customer[] }>(`/api/v1/customers?limit=200${s ? `&search=${encodeURIComponent(s)}` : ""}`)).items); }
    catch (e) { setErr((e as Error).message); }
  };
  useEffect(() => { if (isConnected()) load(); }, []);

  if (!isConnected()) return <p className="muted">{tr("notConnected", lang)}</p>;

  return (
    <>
      <div className="topbar"><h1>{tr("customers", lang)}</h1></div>
      <div className="card">
        <div className="row" style={{ alignItems: "end" }}>
          <div style={{ flex: 1 }}>
            <label>{tr("search", lang)} — {tr("externalCustomerId", lang)}</label>
            <input value={search} onChange={(e) => setSearch(e.target.value)} onKeyDown={(e) => e.key === "Enter" && load(search)} placeholder="cust-…" />
          </div>
          <button onClick={() => load(search)}>{tr("search", lang)}</button>
        </div>
        {err && <div className="err">{err}</div>}
      </div>

      <table>
        <thead><tr>
          <th>{tr("externalCustomerId", lang)}</th><th>{tr("tier", lang)}</th><th>{tr("persona", lang)}</th>
          <th>{tr("lifecycleStage", lang)}</th><th>{tr("activeBalance", lang)}</th><th>{tr("netSpend", lang)}</th>
          <th>{tr("status", lang)}</th><th>{tr("joined", lang)}</th>
        </tr></thead>
        <tbody>
          {rows.map((c) => (
            <tr key={c.memberId}>
              <td>{c.externalCustomerId}</td>
              <td>{c.tier ? <span className="badge ok">{c.tier}</span> : "—"}</td>
              <td>{c.persona ? tr(c.persona, lang) : "—"}</td>
              <td>{c.lifecycle ?? "—"}</td>
              <td>{c.activeBalance.toLocaleString()}</td>
              <td>{c.netSpend.toLocaleString()} IQD</td>
              <td>{c.status === "Active" ? <span className="badge ok">{c.status}</span> : <span className="badge">{c.status}</span>}</td>
              <td className="muted">{new Date(c.createdAt).toLocaleDateString()}</td>
            </tr>
          ))}
          {rows.length === 0 && <tr><td colSpan={8} className="muted">—</td></tr>}
        </tbody>
      </table>
    </>
  );
}
