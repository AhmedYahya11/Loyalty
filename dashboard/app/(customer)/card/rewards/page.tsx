"use client";
import { useEffect, useState } from "react";
import { QRCodeSVG } from "qrcode.react";
import { useLang } from "../../../providers";
import { tr } from "@/lib/i18n";
import { capi } from "@/lib/customerApi";
import { isCustConnected } from "@/lib/customer";

type Reward = { id: string; name: string; nameAr?: string; pointsCost: number; affordable: boolean };
type Redemption = { id: string; rewardName: string; pointsCost: number; status: string; couponCode: string };

export default function CustomerRewards() {
  const { lang } = useLang();
  const [rewards, setRewards] = useState<Reward[]>([]);
  const [reds, setReds] = useState<Redemption[]>([]);
  const [err, setErr] = useState("");

  const load = async () => {
    try {
      setRewards(await capi.get<Reward[]>("/api/v1/customer/rewards"));
      setReds((await capi.get<{ items: Redemption[] }>("/api/v1/customer/redemptions")).items);
    } catch (e) { setErr((e as Error).message); }
  };
  useEffect(() => { if (isCustConnected()) load(); }, []);

  const redeem = async (id: string) => { setErr(""); try { await capi.post(`/api/v1/customer/redemptions`, { rewardId: id }); await load(); } catch (e) { setErr((e as Error).message); } };
  const cancel = async (id: string) => { try { await capi.post(`/api/v1/customer/redemptions/${id}/cancel`); await load(); } catch (e) { setErr((e as Error).message); } };

  if (typeof window !== "undefined" && !isCustConnected()) return <p className="muted">{tr("notConnectedCust", lang)}</p>;

  const reserved = reds.find((r) => r.status === "Reserved");

  return (
    <>
      <h1 style={{ fontSize: 20 }}>🎁 {tr("myRewards", lang)}</h1>
      {err && <div className="err">{err}</div>}

      {reserved && (
        <div className="card" style={{ textAlign: "center" }}>
          <div className="muted">{tr("showAtCheckout", lang)}</div>
          <div style={{ margin: "12px 0" }}><span className="qr-wrap"><QRCodeSVG value={reserved.couponCode} size={140} /></span></div>
          <div style={{ fontWeight: 700, letterSpacing: 2 }}>{reserved.couponCode}</div>
          <div className="muted">{reserved.rewardName} · {reserved.pointsCost} pts</div>
          <button className="secondary" onClick={() => cancel(reserved.id)}>{tr("cancel", lang)}</button>
        </div>
      )}

      <div className="card">
        {rewards.length === 0 ? <span className="muted">{tr("noRewards", lang)}</span> : rewards.map((r) => (
          <div key={r.id} className="reward-row">
            <div>
              <div style={{ fontWeight: 600 }}>{(lang === "ar" && r.nameAr ? r.nameAr : r.name)}</div>
              <div className="muted">{r.pointsCost.toLocaleString()} pts {r.affordable ? <span className="badge ok">{tr("affordableTag", lang)}</span> : null}</div>
            </div>
            <button disabled={!r.affordable} style={{ opacity: r.affordable ? 1 : 0.5 }} onClick={() => redeem(r.id)}>{tr("redeemFor", lang)}</button>
          </div>
        ))}
      </div>

      {reds.length > 0 && (
        <>
          <h2>{tr("myCoupons", lang)}</h2>
          <div className="card">
            {reds.map((r) => (
              <div key={r.id} className="stat">
                <span>{r.rewardName} <span className="muted">· {r.couponCode}</span></span>
                <span className={r.status === "Confirmed" ? "badge ok" : "badge"}>{r.status}</span>
              </div>
            ))}
          </div>
        </>
      )}
    </>
  );
}
