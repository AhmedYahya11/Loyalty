"use client";
import { useEffect, useState } from "react";
import { useLang } from "@/app/providers";
import { tr } from "@/lib/i18n";
import { api } from "@/lib/api";
import { isConnected } from "@/lib/session";

type Reward = { id: string; name: string; nameAr?: string; pointsCost: number; rewardType: string; value?: number; inventory?: number | null; perMemberLimit?: number | null; isActive: boolean };
const TYPES = ["Coupon", "Voucher", "Discount", "FreeItem", "Other"];

export default function RewardsPage() {
  const { lang } = useLang();
  const [rewards, setRewards] = useState<Reward[]>([]);
  const [name, setName] = useState("");
  const [nameAr, setNameAr] = useState("");
  const [cost, setCost] = useState("100");
  const [type, setType] = useState("Coupon");
  const [value, setValue] = useState("");
  const [inventory, setInventory] = useState("");
  const [err, setErr] = useState("");

  const load = () => api.get<Reward[]>("/api/v1/rewards").then(setRewards).catch((e) => setErr((e as Error).message));
  useEffect(() => { if (isConnected()) load(); }, []);

  const create = async () => {
    setErr("");
    try {
      await api.post("/api/v1/rewards", {
        name, nameAr: nameAr || null, pointsCost: Number(cost), rewardType: type,
        value: value ? Number(value) : null, inventory: inventory ? Number(inventory) : null, perMemberLimit: null,
      });
      setName(""); setNameAr(""); setValue(""); setInventory(""); await load();
    } catch (e) { setErr((e as Error).message); }
  };
  const remove = async (id: string) => { try { await api.del(`/api/v1/rewards/${id}`); await load(); } catch (e) { setErr((e as Error).message); } };

  if (!isConnected()) return <p className="muted">{tr("notConnected", lang)}</p>;

  return (
    <>
      <div className="topbar"><h1>{tr("rewards", lang)}</h1></div>
      <div className="card">
        <div className="row">
          <div><label>{tr("name", lang)}</label><input value={name} onChange={(e) => setName(e.target.value)} placeholder="Free Coffee" /></div>
          <div><label>{tr("nameAr", lang)}</label><input value={nameAr} onChange={(e) => setNameAr(e.target.value)} dir="rtl" /></div>
          <div><label>{tr("pointsCost", lang)}</label><input value={cost} onChange={(e) => setCost(e.target.value)} /></div>
        </div>
        <div className="row">
          <div><label>{tr("rewardType", lang)}</label><select value={type} onChange={(e) => setType(e.target.value)}>{TYPES.map((t) => <option key={t}>{t}</option>)}</select></div>
          <div><label>Value (IQD)</label><input value={value} onChange={(e) => setValue(e.target.value)} placeholder="optional" /></div>
          <div><label>{tr("inventory", lang)}</label><input value={inventory} onChange={(e) => setInventory(e.target.value)} placeholder="unlimited" /></div>
        </div>
        <button onClick={create}>{tr("create", lang)}</button>
        {err && <div className="err">{err}</div>}
      </div>

      <table>
        <thead><tr><th>{tr("name", lang)}</th><th>{tr("rewardType", lang)}</th><th>{tr("pointsCost", lang)}</th><th>{tr("inventory", lang)}</th><th>{tr("status", lang)}</th><th></th></tr></thead>
        <tbody>
          {rewards.map((r) => (
            <tr key={r.id}>
              <td>{lang === "ar" && r.nameAr ? r.nameAr : r.name}</td>
              <td>{r.rewardType}</td>
              <td>{r.pointsCost}</td>
              <td>{r.inventory ?? "∞"}</td>
              <td>{r.isActive ? <span className="badge ok">{tr("active", lang)}</span> : <span className="badge">—</span>}</td>
              <td><button className="secondary" style={{ margin: 0 }} onClick={() => remove(r.id)}>{tr("delete", lang)}</button></td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}
