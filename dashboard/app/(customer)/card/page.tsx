"use client";
import { useEffect, useState } from "react";
import { QRCodeSVG } from "qrcode.react";
import { useLang } from "../../providers";
import { tr } from "@/lib/i18n";
import { capi } from "@/lib/customerApi";
import { isCustConnected, setCustSession } from "@/lib/customer";
import { isConnected } from "@/lib/session";
import Link from "next/link";

type Wallet = { pointsUnitCode: string; activeBalance: number; pendingBalance: number; reservedBalance: number };
type Member = { memberId: string; externalCustomerId: string; wallet: Wallet; referralCode?: string };
type Tier = { name: string; nameAr?: string; earnMultiplier: number };
type Profile = { lifecycleStage: string; tier?: Tier | null; currentStreak: number; longestStreak: number; pointsEarned: number; pointsRedeemed: number; referralCount: number };
type Badge = { achievementId: string; name: string; nameAr?: string };
type Raffle = { raffleId: string; name: string; nameAr?: string; myEntries: number; status: string; won: boolean; prizeDescription?: string };
type Me = { member: Member; profile: Profile };

export default function CardHome() {
  const { lang } = useLang();
  const [connected, setConnected] = useState(false);
  const [token, setToken] = useState("");
  const [me, setMe] = useState<Me | null>(null);
  const [badges, setBadges] = useState<Badge[]>([]);
  const [raffles, setRaffles] = useState<Raffle[]>([]);
  const [err, setErr] = useState("");

  const load = async () => {
    try {
      setMe(await capi.get<Me>("/api/v1/customer/me"));
      setBadges((await capi.get<{ items: Badge[] }>("/api/v1/customer/achievements")).items);
      try { setRaffles((await capi.get<{ items: Raffle[] }>("/api/v1/customer/raffles")).items.filter((r) => r.myEntries > 0 || r.won)); } catch { setRaffles([]); }
    }
    catch (e) { setErr((e as Error).message); }
  };

  useEffect(() => {
    const url = new URL(window.location.href);
    const t = url.searchParams.get("token");
    if (t) { setCustSession(t); url.searchParams.delete("token"); window.history.replaceState({}, "", url.toString()); }
    const ok = isCustConnected();
    setConnected(ok);
    if (ok) load();
  }, []);

  const connect = () => {
    if (!token.trim()) return;
    setCustSession(token.trim());
    setConnected(true);
    load();
  };

  if (!connected) {
    const admin = typeof window !== "undefined" && isConnected();
    return (
      <div className="card">
        <h1 style={{ fontSize: 20 }}>{tr("connectCustomer", lang)}</h1>
        {admin && (
          <div style={{ background: "rgba(177,138,67,0.08)", border: "1px solid var(--border)", borderRadius: 10, padding: "12px 14px", margin: "6px 0 16px" }}>
            <p className="muted" style={{ margin: "0 0 10px" }}>{tr("adminCardHint", lang)}</p>
            <Link href="/members" className="lang-toggle" style={{ textDecoration: "none", display: "inline-flex", alignItems: "center", gap: 6 }}>
              👥 {tr("goToMembers", lang)}
            </Link>
          </div>
        )}
        <p className="muted">{tr("notConnectedCust", lang)}</p>
        <label>{tr("pasteToken", lang)}</label>
        <input value={token} onChange={(e) => setToken(e.target.value)} placeholder="mt_…" />
        <button onClick={connect}>{tr("connectBtn", lang)}</button>
        {err && <div className="err">{err}</div>}
      </div>
    );
  }

  if (err) return <div className="err">{err}</div>;
  if (!me) return <p className="muted">…</p>;

  const { member, profile } = me;
  const tierName = profile.tier ? (lang === "ar" && profile.tier.nameAr ? profile.tier.nameAr : profile.tier.name) : null;
  const cardCode = member.referralCode ?? member.externalCustomerId;

  return (
    <>
      <div className="loyalty-card">
        <div className="muted" style={{ color: "rgba(255,255,255,.85)" }}>{tr("availablePoints", lang)}</div>
        <div className="pts">{member.wallet.activeBalance.toLocaleString()}</div>
        <div style={{ opacity: .9, fontSize: 13 }}>{member.wallet.pointsUnitCode}</div>
        <div className="row2">
          <div>
            <div style={{ fontWeight: 700 }}>{member.externalCustomerId}</div>
            {tierName && <span className="tier-pill">⭐ {tierName} ×{profile.tier!.earnMultiplier}</span>}
          </div>
          <div style={{ fontSize: 13 }}>🔥 {profile.currentStreak}</div>
        </div>
      </div>

      <div className="card" style={{ textAlign: "center" }}>
        <div className="muted">{tr("showAtCheckout", lang)}</div>
        <div style={{ margin: "12px 0" }}><span className="qr-wrap"><QRCodeSVG value={cardCode} size={150} /></span></div>
        <div style={{ fontWeight: 700, letterSpacing: 2 }}>{cardCode}</div>
      </div>

      <h2>🏅 {tr("badges", lang)}</h2>
      <div className="card">
        {badges.length === 0 ? <span className="muted">—</span> :
          badges.map((b) => <span key={b.achievementId} className="badge ok" style={{ marginInlineEnd: 6, display: "inline-block", marginBottom: 6 }}>🏅 {(lang === "ar" && b.nameAr ? b.nameAr : b.name)}</span>)}
      </div>

      {raffles.length > 0 && (
        <>
          <h2>🎲 {tr("raffles", lang)}</h2>
          <div className="card">
            {raffles.map((r) => (
              <div key={r.raffleId} className="stat">
                <span>{(lang === "ar" && r.nameAr ? r.nameAr : r.name)}{r.prizeDescription ? <span className="muted"> · {r.prizeDescription}</span> : null}</span>
                <b>{r.won ? tr("youWon", lang) : `🎟️ ${r.myEntries}`}</b>
              </div>
            ))}
          </div>
        </>
      )}

      <div className="card">
        <div className="stat"><span>{tr("pointsEarnedShort", lang)}</span><b>{profile.pointsEarned.toLocaleString()}</b></div>
        <div className="stat"><span>{tr("pointsRedeemedShort", lang)}</span><b>{profile.pointsRedeemed.toLocaleString()}</b></div>
        <div className="stat"><span>{tr("streak", lang)}</span><b>🔥 {profile.currentStreak} (max {profile.longestStreak})</b></div>
        <div className="stat" style={{ borderBottom: "none" }}><span>{tr("referrals", lang)}</span><b>{profile.referralCount}</b></div>
        {member.referralCode && <div className="stat" style={{ borderTop: "1px solid var(--border)" }}><span>{tr("referralCode", lang)}</span><b>🎁 {member.referralCode}</b></div>}
      </div>
    </>
  );
}
