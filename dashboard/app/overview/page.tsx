"use client";
import { useEffect, useState } from "react";
import { useLang } from "../providers";
import { tr } from "@/lib/i18n";
import { api } from "@/lib/api";
import { isConnected } from "@/lib/session";

type Overview = {
  members: number; activeCampaigns: number; activeSegments: number;
  pointsIssued: number; pointsReversed: number; activeBalance: number;
  lifecycle: Record<string, number>;
};

function Card({ label, value }: { label: string; value: number | string }) {
  return (
    <div className="card" style={{ flex: 1, minWidth: 160 }}>
      <div className="muted">{label}</div>
      <div style={{ fontSize: 28, fontWeight: 700, marginTop: 6 }}>{value}</div>
    </div>
  );
}

export default function OverviewPage() {
  const { lang } = useLang();
  const [o, setO] = useState<Overview | null>(null);
  const [err, setErr] = useState("");

  useEffect(() => {
    if (!isConnected()) return;
    api.get<Overview>("/api/v1/analytics/overview").then(setO).catch((e) => setErr((e as Error).message));
  }, []);

  if (!isConnected()) return <p className="muted">{tr("notConnected", lang)}</p>;
  if (err) return <p className="err">{err}</p>;
  if (!o) return <p className="muted">…</p>;

  const maxLc = Math.max(1, ...Object.values(o.lifecycle));

  return (
    <>
      <div className="topbar"><h1>{tr("overview", lang)}</h1></div>
      <div className="row">
        <Card label={tr("totalMembers", lang)} value={o.members} />
        <Card label={tr("campaigns", lang)} value={o.activeCampaigns} />
        <Card label={tr("segments", lang)} value={o.activeSegments} />
      </div>
      <div className="row">
        <Card label={tr("pointsIssued", lang)} value={o.pointsIssued} />
        <Card label={tr("pointsReversed", lang)} value={o.pointsReversed} />
        <Card label={tr("activeBalanceTotal", lang)} value={o.activeBalance} />
      </div>

      <h2>{tr("lifecycleDistribution", lang)}</h2>
      <div className="card">
        {Object.entries(o.lifecycle).map(([stage, count]) => (
          <div key={stage} style={{ display: "flex", alignItems: "center", gap: 12, margin: "8px 0" }}>
            <div style={{ width: 90, color: "var(--muted)" }}>{stage}</div>
            <div style={{ flex: 1, background: "var(--panel-2)", borderRadius: 6 }}>
              <div style={{ width: `${(count / maxLc) * 100}%`, minWidth: count ? 24 : 0, background: "var(--accent)", height: 22, borderRadius: 6, textAlign: "center", color: "#fff", fontSize: 12, lineHeight: "22px" }}>
                {count || ""}
              </div>
            </div>
          </div>
        ))}
      </div>
    </>
  );
}
