"use client";
import { useEffect, useState } from "react";
import { useLang } from "@/app/providers";
import { tr } from "@/lib/i18n";
import { api } from "@/lib/api";
import { isConnected } from "@/lib/session";

type PersonaCount = { persona: string; count: number };
type Summary = { members: number; evaluated: number; avgRecency: number; avgFrequency: number; avgMonetary: number; personas: PersonaCount[] };

const COLORS: Record<string, string> = {
  Champions: "#22c55e", LoyalCustomers: "#5b8cff", PotentialLoyalist: "#38bdf8", NewCustomers: "#a78bfa",
  Promising: "#2dd4bf", NeedAttention: "#facc15", AtRisk: "#fb923c", Hibernating: "#94a3b8", Lost: "#ef4444",
};

export default function RfmPage() {
  const { lang } = useLang();
  const [s, setS] = useState<Summary | null>(null);
  const [msg, setMsg] = useState("");
  const [err, setErr] = useState("");

  const load = async () => { try { setS(await api.get<Summary>("/api/v1/analytics/rfm")); } catch (e) { setErr((e as Error).message); } };
  useEffect(() => { if (isConnected()) load(); }, []);

  const recompute = async () => {
    setErr(""); setMsg("");
    try { const r = await api.post<{ scored: number }>("/api/v1/admin/rfm-recompute", {}); setMsg(`✓ ${r.scored}`); await load(); }
    catch (e) { setErr((e as Error).message); }
  };

  if (!isConnected()) return <p className="muted">{tr("notConnected", lang)}</p>;

  const max = s ? Math.max(1, ...s.personas.map((p) => p.count)) : 1;

  return (
    <>
      <div className="topbar"><h1>🧭 {tr("rfm", lang)}</h1></div>
      {err && <div className="err">{err}</div>}
      {msg && <div className="badge ok">{msg}</div>}

      <div className="card">
        <div className="row" style={{ alignItems: "center" }}>
          <button onClick={recompute}>{tr("recompute", lang)}</button>
          {s && <span className="muted">{tr("members_evaluated", lang)}: {s.evaluated} / {s.members}</span>}
        </div>
      </div>

      {s && (
        <>
          <div className="card">
            <div className="stat"><span>{tr("recency", lang)}</span><b>{s.avgRecency.toFixed(1)} / 5</b></div>
            <div className="stat"><span>{tr("frequency", lang)}</span><b>{s.avgFrequency.toFixed(1)} / 5</b></div>
            <div className="stat" style={{ borderBottom: "none" }}><span>{tr("monetary", lang)}</span><b>{s.avgMonetary.toFixed(1)} / 5</b></div>
          </div>

          <h2>{tr("persona", lang)}</h2>
          <div className="card">
            {s.personas.length === 0 ? <span className="muted">— ({tr("recompute", lang)})</span> : s.personas.map((p) => (
              <div key={p.persona} style={{ marginBottom: 12 }}>
                <div style={{ display: "flex", justifyContent: "space-between", fontSize: 14, marginBottom: 4 }}>
                  <span><span style={{ display: "inline-block", width: 10, height: 10, borderRadius: 3, background: COLORS[p.persona] ?? "#888", marginInlineEnd: 8 }} />{tr(p.persona, lang)}</span>
                  <b>{p.count}</b>
                </div>
                <div style={{ background: "var(--panel-2)", borderRadius: 6, height: 10 }}>
                  <div style={{ width: `${(p.count / max) * 100}%`, background: COLORS[p.persona] ?? "#888", height: 10, borderRadius: 6 }} />
                </div>
              </div>
            ))}
          </div>
        </>
      )}
    </>
  );
}
