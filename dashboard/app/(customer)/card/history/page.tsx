"use client";
import { useEffect, useState } from "react";
import { useLang } from "../../../providers";
import { tr } from "@/lib/i18n";
import { capi } from "@/lib/customerApi";
import { isCustConnected } from "@/lib/customer";

type Txn = { transactionId: string; transactionType: string; totalDebit: number; totalCredit: number; occurredAt: string };
type Stamp = { stampCardId: string; name: string; nameAr?: string; stamps: number; stampsRequired: number; completedCount: number; rewardPoints: number };

export default function CustomerHistory() {
  const { lang } = useLang();
  const [txns, setTxns] = useState<Txn[]>([]);
  const [stamps, setStamps] = useState<Stamp[]>([]);
  const [err, setErr] = useState("");

  useEffect(() => {
    if (!isCustConnected()) return;
    (async () => {
      try {
        setTxns((await capi.get<{ items: Txn[] }>("/api/v1/customer/transactions")).items);
        setStamps((await capi.get<{ items: Stamp[] }>("/api/v1/customer/stamp-cards")).items);
      } catch (e) { setErr((e as Error).message); }
    })();
  }, []);

  if (typeof window !== "undefined" && !isCustConnected()) return <p className="muted">{tr("notConnectedCust", lang)}</p>;

  return (
    <>
      <h1 style={{ fontSize: 20 }}>🧾 {tr("history", lang)}</h1>
      {err && <div className="err">{err}</div>}

      {stamps.length > 0 && (
        <>
          <h2>🎟️ {tr("stampCards", lang)}</h2>
          <div className="card">
            {stamps.map((st) => (
              <div key={st.stampCardId} className="stat">
                <span>{(lang === "ar" && st.nameAr ? st.nameAr : st.name)}</span>
                <span>{"●".repeat(st.stamps)}{"○".repeat(Math.max(0, st.stampsRequired - st.stamps))} <span className="muted">{st.stamps}/{st.stampsRequired}</span></span>
              </div>
            ))}
          </div>
        </>
      )}

      <h2>{tr("recentActivity", lang)}</h2>
      <div className="card">
        {txns.length === 0 ? <span className="muted">—</span> : txns.map((t) => {
          const net = t.totalCredit - t.totalDebit;
          const earn = t.transactionType === "Earn" || (t.transactionType === "Adjust" && net > 0);
          return (
            <div key={t.transactionId} className="stat">
              <span>{t.transactionType}<br /><span className="muted">{new Date(t.occurredAt).toLocaleDateString()}</span></span>
              <b style={{ color: earn ? "var(--accent-2)" : "var(--muted)" }}>{earn ? "+" : "−"}{Math.abs(net) || t.totalCredit}</b>
            </div>
          );
        })}
      </div>
    </>
  );
}
