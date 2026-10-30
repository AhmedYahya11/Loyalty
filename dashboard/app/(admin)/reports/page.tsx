"use client";
import { useEffect, useState } from "react";
import { useLang } from "@/app/providers";
import { tr } from "@/lib/i18n";
import { api } from "@/lib/api";
import { isConnected } from "@/lib/session";

type Campaign = { id: string; name: string; nameAr?: string; status: string; executions: number; members: number; pointsIssued: number; budgetInitial?: number | null; budgetRemaining?: number | null };
type Reward = { id: string; name: string; nameAr?: string; pointsCost: number; reserved: number; confirmed: number; cancelled: number; redemptionRate: number };
type Stamp = { id: string; name: string; nameAr?: string; rewardPoints: number; completions: number; participants: number };
type Summary = {
  campaigns: Campaign[]; rewards: Reward[]; stampCards: Stamp[];
  averageOrderValue: number; totalPurchases: number; totalNetSpend: number;
  liabilities: { outstandingPoints: number; reservedPoints: number };
};

export default function ReportsPage() {
  const { lang } = useLang();
  const [s, setS] = useState<Summary | null>(null);
  const [err, setErr] = useState("");
  const nm = (o: { name: string; nameAr?: string }) => (lang === "ar" && o.nameAr ? o.nameAr : o.name);

  useEffect(() => {
    if (!isConnected()) return;
    api.get<Summary>("/api/v1/analytics/promotions").then(setS).catch((e) => setErr((e as Error).message));
  }, []);

  if (!isConnected()) return <p className="muted">{tr("notConnected", lang)}</p>;
  if (err) return <div className="err">{err}</div>;
  if (!s) return <p className="muted">…</p>;

  return (
    <>
      <div className="topbar"><h1>📊 {tr("reports", lang)}</h1></div>

      <div className="row" style={{ gap: 16, flexWrap: "wrap" }}>
        <div className="card" style={{ flex: 1, minWidth: 180 }}><div className="muted">{tr("aov", lang)}</div><div style={{ fontSize: 26, fontWeight: 700 }}>{s.averageOrderValue.toLocaleString()} <span className="muted" style={{ fontSize: 13 }}>IQD</span></div></div>
        <div className="card" style={{ flex: 1, minWidth: 180 }}><div className="muted">{tr("outstandingPoints", lang)}</div><div style={{ fontSize: 26, fontWeight: 700 }}>{s.liabilities.outstandingPoints.toLocaleString()}</div></div>
        <div className="card" style={{ flex: 1, minWidth: 180 }}><div className="muted">{tr("reservedPoints", lang)}</div><div style={{ fontSize: 26, fontWeight: 700 }}>{s.liabilities.reservedPoints.toLocaleString()}</div></div>
      </div>

      <h2>{tr("campaignPerformance", lang)}</h2>
      <table>
        <thead><tr><th>{tr("name", lang)}</th><th>{tr("status", lang)}</th><th>{tr("executions", lang)}</th><th>{tr("members", lang)}</th><th>{tr("pointsIssued", lang)}</th><th>{tr("budget", lang)}</th></tr></thead>
        <tbody>{s.campaigns.map((c) => (
          <tr key={c.id}>
            <td>{nm(c)}</td>
            <td>{c.status === "Active" ? <span className="badge ok">{tr("active", lang)}</span> : <span className="badge">{c.status}</span>}</td>
            <td>{c.executions}</td><td>{c.members}</td><td>{c.pointsIssued.toLocaleString()}</td>
            <td>{c.budgetInitial == null ? "∞" : `${(c.budgetRemaining ?? 0).toLocaleString()} / ${c.budgetInitial.toLocaleString()}`}</td>
          </tr>
        ))}</tbody>
      </table>

      <h2>{tr("rewardPerformance", lang)}</h2>
      <table>
        <thead><tr><th>{tr("name", lang)}</th><th>{tr("reserved", lang)}</th><th>{tr("confirmedLabel", lang)}</th><th>{tr("cancel", lang)}</th><th>{tr("redemptionRate", lang)}</th></tr></thead>
        <tbody>{s.rewards.map((r) => (
          <tr key={r.id}><td>{nm(r)}</td><td>{r.reserved}</td><td>{r.confirmed}</td><td>{r.cancelled}</td><td>{Math.round(r.redemptionRate * 100)}%</td></tr>
        ))}</tbody>
      </table>

      {s.stampCards.length > 0 && (
        <>
          <h2>🎟️ {tr("stampPerformance", lang)}</h2>
          <table>
            <thead><tr><th>{tr("name", lang)}</th><th>{tr("participants", lang)}</th><th>{tr("completions", lang)}</th><th>{tr("rewardPoints", lang)}</th></tr></thead>
            <tbody>{s.stampCards.map((c) => (
              <tr key={c.id}><td>{nm(c)}</td><td>{c.participants}</td><td>{c.completions}</td><td>+{c.rewardPoints}</td></tr>
            ))}</tbody>
          </table>
        </>
      )}
    </>
  );
}
