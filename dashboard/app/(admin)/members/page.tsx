"use client";
import { useState } from "react";
import { QRCodeSVG } from "qrcode.react";
import { useLang } from "@/app/providers";
import { tr } from "@/lib/i18n";
import { api } from "@/lib/api";
import { isConnected } from "@/lib/session";

type Wallet = { walletId: string; pointsUnitCode: string; activeBalance: number; pendingBalance: number; reservedBalance: number };
type Member = { memberId: string; externalCustomerId: string; defaultWalletId: string; status: string; wallet: Wallet; referralCode?: string; birthDate?: string };
type Stamp = { stampCardId: string; name: string; nameAr?: string; stamps: number; stampsRequired: number; completedCount: number; rewardPoints: number };
type Txn = { transactionId: string; transactionType: string; totalDebit: number; totalCredit: number; occurredAt: string };
type Segment = { segmentId: string; name: string; nameAr?: string };
type Tier = { name: string; nameAr?: string; rank: number; earnMultiplier: number };
type Rfm = { recency: number; frequency: number; monetary: number; persona?: string | null };
type Profile = { lifecycleStage: string; tier?: Tier | null; currentStreak: number; longestStreak: number; purchaseCount: number; netSpendAmount: number; pointsEarned: number; pointsRedeemed: number; activeSegments: Segment[]; rfm?: Rfm };
type Reward = { id: string; name: string; nameAr?: string; pointsCost: number };
type Redemption = { id: string; rewardName: string; pointsCost: number; status: string; couponCode: string };
type Badge = { achievementId: string; name: string; nameAr?: string; icon?: string };

export default function MembersPage() {
  const { lang } = useLang();
  const [ext, setExt] = useState("hareer-user-100");
  const [member, setMember] = useState<Member | null>(null);
  const [txns, setTxns] = useState<Txn[]>([]);
  const [profile, setProfile] = useState<Profile | null>(null);
  const [rewards, setRewards] = useState<Reward[]>([]);
  const [redemptions, setRedemptions] = useState<Redemption[]>([]);
  const [badges, setBadges] = useState<Badge[]>([]);
  const [stamps, setStamps] = useState<Stamp[]>([]);
  const [rewardId, setRewardId] = useState("");
  const [custLink, setCustLink] = useState("");
  const [err, setErr] = useState("");

  const refresh = async (memberId: string) => {
    const m = await api.get<Member>(`/api/v1/members/${memberId}`);
    setMember(m);
    setCustLink("");
    setTxns((await api.get<{ items: Txn[] }>(`/api/v1/members/${memberId}/wallet/transactions`)).items);
    try { setProfile(await api.get<Profile>(`/api/v1/members/${memberId}/value-profile`)); } catch { setProfile(null); }
    setRedemptions((await api.get<{ items: Redemption[] }>(`/api/v1/members/${memberId}/redemptions`)).items);
    try { setBadges((await api.get<{ items: Badge[] }>(`/api/v1/members/${memberId}/achievements`)).items); } catch { setBadges([]); }
    try { setStamps((await api.get<{ items: Stamp[] }>(`/api/v1/members/${memberId}/stamp-cards`)).items); } catch { setStamps([]); }
    const rw = await api.get<Reward[]>("/api/v1/rewards?activeOnly=true");
    setRewards(rw); if (rw[0]) setRewardId(rw[0].id);
  };

  const create = async () => {
    setErr("");
    try { const m = await api.post<Member>("/api/v1/members", { externalCustomerId: ext }); await refresh(m.memberId); }
    catch (e) { setErr((e as Error).message); }
  };
  const lookup = async () => {
    setErr("");
    try { const m = await api.get<Member>(`/api/v1/members/by-external-id/${encodeURIComponent(ext)}`); await refresh(m.memberId); }
    catch (e) { setErr((e as Error).message); setMember(null); setProfile(null); }
  };
  const reserve = async () => {
    if (!member || !rewardId) return;
    setErr("");
    try { await api.post(`/api/v1/members/${member.memberId}/redemptions`, { rewardId }); await refresh(member.memberId); }
    catch (e) { setErr((e as Error).message); }
  };
  const customerLink = async () => {
    if (!member) return;
    setErr(""); setCustLink("");
    try {
      const r = await api.post<{ token: string }>(`/api/v1/members/${member.memberId}/access-token`, {});
      setCustLink(`${location.origin}/card?token=${r.token}`);
    } catch (e) { setErr((e as Error).message); }
  };
  const act = async (id: string, action: "confirm" | "cancel") => {
    if (!member) return;
    try { await api.post(`/api/v1/redemptions/${id}/${action}`); await refresh(member.memberId); }
    catch (e) { setErr((e as Error).message); }
  };

  if (!isConnected()) return <p className="muted">{tr("notConnected", lang)}</p>;

  const reservedCoupon = redemptions.find((r) => r.status === "Reserved");

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
                <tr><th>{tr("tier", lang)}</th><td>{profile.tier ? <span className="badge ok">{(lang === "ar" && profile.tier.nameAr ? profile.tier.nameAr : profile.tier.name)} (×{profile.tier.earnMultiplier})</span> : "—"}</td></tr>
                <tr><th>{tr("streak", lang)}</th><td>🔥 {profile.currentStreak} (max {profile.longestStreak})</td></tr>
                {profile.rfm?.persona && <tr><th>{tr("persona", lang)}</th><td><span className="badge ok">🧭 {tr(profile.rfm.persona, lang)}</span> <span className="muted">R{profile.rfm.recency} F{profile.rfm.frequency} M{profile.rfm.monetary}</span></td></tr>}
                <tr><th>{tr("badges", lang)}</th><td>{badges.length > 0 ? badges.map((b) => <span key={b.achievementId} className="badge ok" style={{ marginInlineEnd: 6 }}>🏅 {(lang === "ar" && b.nameAr ? b.nameAr : b.name)}</span>) : "—"}</td></tr>
                <tr><th>{tr("purchaseCount", lang)}</th><td>{profile.purchaseCount}</td></tr>
                <tr><th>{tr("netSpend", lang)}</th><td>{profile.netSpendAmount} IQD</td></tr>
                <tr><th>{tr("pointsEarned", lang)}</th><td>{profile.pointsEarned}</td></tr>
                <tr><th>{tr("pointsRedeemed", lang)}</th><td>{profile.pointsRedeemed}</td></tr>
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
                <tr><th>{tr("pending", lang)}</th><td>{member.wallet.pendingBalance}</td></tr>
                <tr><th>{tr("reserved", lang)}</th><td>{member.wallet.reservedBalance}</td></tr>
                {member.referralCode && <tr><th>{tr("referralCode", lang)}</th><td><span className="badge ok" style={{ letterSpacing: 1 }}>🎁 {member.referralCode}</span></td></tr>}
                {member.birthDate && <tr><th>{tr("birthDate", lang)}</th><td>🎂 {member.birthDate}</td></tr>}
              </tbody>
            </table>
            <button className="secondary" onClick={customerLink}>💳 {tr("customerAppLink", lang)}</button>
            {custLink && (
              <div style={{ marginTop: 12, display: "flex", gap: 16, alignItems: "center", flexWrap: "wrap" }}>
                <div style={{ background: "#fff", padding: 10, borderRadius: 8 }}><QRCodeSVG value={custLink} size={110} /></div>
                <div style={{ flex: 1, minWidth: 200 }}>
                  <div className="muted">{tr("shareCustomerLink", lang)}</div>
                  <a href={custLink} target="_blank" rel="noreferrer" style={{ wordBreak: "break-all", fontSize: 13 }}>{custLink}</a>
                </div>
              </div>
            )}
          </div>

          {stamps.length > 0 && (
            <>
              <h2>🎟️ {tr("stampCards", lang)}</h2>
              <table>
                <thead><tr><th>{tr("name", lang)}</th><th>{tr("progress", lang)}</th><th>{tr("rewardPoints", lang)}</th><th>✓</th></tr></thead>
                <tbody>
                  {stamps.map((st) => (
                    <tr key={st.stampCardId}>
                      <td>{(lang === "ar" && st.nameAr ? st.nameAr : st.name)}</td>
                      <td>{"●".repeat(st.stamps)}{"○".repeat(Math.max(0, st.stampsRequired - st.stamps))} <span className="muted">{st.stamps}/{st.stampsRequired}</span></td>
                      <td>+{st.rewardPoints}</td>
                      <td>{st.completedCount}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}

          <h2>{tr("redeem", lang)}</h2>
          <div className="card">
            {rewards.length === 0 ? <p className="muted">{tr("noActiveRewards", lang)}</p> : (
              <div className="row" style={{ alignItems: "end" }}>
                <div>
                  <label>{tr("rewards", lang)}</label>
                  <select value={rewardId} onChange={(e) => setRewardId(e.target.value)}>
                    {rewards.map((r) => <option key={r.id} value={r.id}>{(lang === "ar" && r.nameAr ? r.nameAr : r.name)} — {r.pointsCost} pts</option>)}
                  </select>
                </div>
                <button onClick={reserve}>{tr("redeem", lang)}</button>
              </div>
            )}
            {reservedCoupon && (
              <div style={{ marginTop: 16, display: "flex", gap: 20, alignItems: "center" }}>
                <div style={{ background: "#fff", padding: 10, borderRadius: 8 }}><QRCodeSVG value={reservedCoupon.couponCode} size={120} /></div>
                <div>
                  <div className="muted">{tr("couponCode", lang)}</div>
                  <div style={{ fontSize: 22, fontWeight: 700, letterSpacing: 1 }}>{reservedCoupon.couponCode}</div>
                  <div className="muted">{reservedCoupon.rewardName} · {reservedCoupon.pointsCost} pts</div>
                </div>
              </div>
            )}
          </div>

          {redemptions.length > 0 && (
            <>
              <h2>{tr("redemptions", lang)}</h2>
              <table>
                <thead><tr><th>{tr("rewards", lang)}</th><th>{tr("couponCode", lang)}</th><th>{tr("pointsCost", lang)}</th><th>{tr("status", lang)}</th><th></th></tr></thead>
                <tbody>
                  {redemptions.map((r) => (
                    <tr key={r.id}>
                      <td>{r.rewardName}</td>
                      <td className="muted">{r.couponCode}</td>
                      <td>{r.pointsCost}</td>
                      <td>{r.status === "Confirmed" ? <span className="badge ok">{r.status}</span> : <span className="badge">{r.status}</span>}</td>
                      <td style={{ display: "flex", gap: 8 }}>
                        {r.status === "Reserved" && <>
                          <button className="secondary" style={{ margin: 0 }} onClick={() => act(r.id, "confirm")}>{tr("confirm", lang)}</button>
                          <button className="secondary" style={{ margin: 0 }} onClick={() => act(r.id, "cancel")}>{tr("cancel", lang)}</button>
                        </>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}

          {txns.length > 0 && (
            <>
              <h2>{tr("transactions", lang)}</h2>
              <table>
                <thead><tr><th>{tr("type", lang)}</th><th>{tr("debit", lang)}</th><th>{tr("credit", lang)}</th><th>{tr("occurred", lang)}</th></tr></thead>
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
