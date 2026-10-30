"use client";
import { useState } from "react";
import { useLang } from "../providers";
import { tr } from "@/lib/i18n";
import { api } from "@/lib/api";
import { isConnected } from "@/lib/session";

type Wallet = { walletId: string; pointsUnitCode: string; activeBalance: number; pendingBalance: number; reservedBalance: number };
type Member = { memberId: string; externalCustomerId: string; defaultWalletId: string; status: string; wallet: Wallet };
type Txn = { transactionId: string; transactionType: string; totalDebit: number; totalCredit: number; occurredAt: string };
type Segment = { segmentId: string; name: string; nameAr?: string };
type Profile = { lifecycleStage: string; purchaseCount: number; netSpendAmount: number; pointsEarned: number; activeSegments: Segment[] };

export default function MembersPage() {
  const { lang } = useLang();
  const [ext, setExt] = useState("hareer-user-100");
  const [member, setMember] = useState<Member | null>(null);
  const [txns, setTxns] = useState<Txn[]>([]);
  const [profile, setProfile] = useState<Profile | null>(null);
  const [err, setErr] = useState("");

  const create = async () => {
    setErr("");
    try { setMember(await api.post<Member>("/api/v1/members", { externalCustomerId: ext })); setTxns([]); setProfile(null); }
    catch (e) { setErr((e as Error).message); }
  };
  const lookup = async () => {
    setErr("");
    try {
      const m = await api.get<Member>(`/api/v1/members/by-external-id/${encodeURIComponent(ext)}`);
      setMember(m);
      const res = await api.get<{ items: Txn[] }>(`/api/v1/members/${m.memberId}/wallet/transactions`);
      setTxns(res.items);
      try { setProfile(await api.get<Profile>(`/api/v1/members/${m.memberId}/value-profile`)); } catch { setProfile(null); }
    } catch (e) { setErr((e as Error).message); setMember(null); setProfile(null); }
  };

  if (!isConnected()) return <p className="muted">{tr("notConnected", lang)}</p>;

  return (
    <>
      <div className="topbar"><h1>{tr("members", lang)}</h1></div>
      <div className="card">
        <label>{tr("externalCustomerId", lang)}</label>
        <input value={ext} onChange={(e) => setExt(e.target.value)} />
        <div className="row">
          <button onClick={create}>{tr("create", lang)}</button>
          <button className="secondary" onClick={lookup}>{tr("lookup", lang)}</button>
        </div>
        {err && <div className="err">{err}</div>}
      </div>

      {profile && (
        <>
          <h2>{tr("valueProfile", lang)}</h2>
          <div className="card">
            <table>
              <tbody>
                <tr><th>{tr("lifecycleStage", lang)}</th><td><span className="badge ok">{profile.lifecycleStage}</span></td></tr>
                <tr><th>{tr("purchaseCount", lang)}</th><td>{profile.purchaseCount}</td></tr>
                <tr><th>{tr("netSpend", lang)}</th><td>{profile.netSpendAmount} IQD</td></tr>
                <tr><th>{tr("pointsEarned", lang)}</th><td>{profile.pointsEarned}</td></tr>
                <tr><th>{tr("activeSegments", lang)}</th><td>{profile.activeSegments.map((s) => (lang === "ar" && s.nameAr ? s.nameAr : s.name)).join("، ") || "—"}</td></tr>
              </tbody>
            </table>
          </div>
        </>
      )}

      {member && (
        <>
          <h2>{tr("wallet", lang)}</h2>
          <div className="card">
            <p className="muted">{member.memberId}</p>
            <table>
              <tbody>
                <tr><th>{tr("activeBalance", lang)}</th><td>{member.wallet.activeBalance} {member.wallet.pointsUnitCode}</td></tr>
                <tr><th>Pending</th><td>{member.wallet.pendingBalance}</td></tr>
                <tr><th>Reserved</th><td>{member.wallet.reservedBalance}</td></tr>
              </tbody>
            </table>
          </div>
          {txns.length > 0 && (
            <>
              <h2>{tr("transactions", lang)}</h2>
              <table>
                <thead><tr><th>Type</th><th>Debit</th><th>Credit</th><th>Occurred</th></tr></thead>
                <tbody>
                  {txns.map((t) => (
                    <tr key={t.transactionId}>
                      <td>{t.transactionType}</td><td>{t.totalDebit}</td><td>{t.totalCredit}</td>
                      <td className="muted">{new Date(t.occurredAt).toLocaleString()}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}
        </>
      )}
    </>
  );
}
