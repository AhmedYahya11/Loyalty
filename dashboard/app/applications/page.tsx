"use client";
import { useEffect, useState } from "react";
import { useLang } from "../providers";
import { tr } from "@/lib/i18n";
import { api } from "@/lib/api";
import { isConnected } from "@/lib/session";

type App = { id: string; name: string; nameAr?: string; parentId?: string; isActive: boolean };

export default function ApplicationsPage() {
  const { lang } = useLang();
  const [apps, setApps] = useState<App[]>([]);
  const [name, setName] = useState("");
  const [nameAr, setNameAr] = useState("");
  const [parentId, setParentId] = useState("");
  const [err, setErr] = useState("");
  const [connected, setConnected] = useState(true);

  const load = async () => {
    try { setApps(await api.get<App[]>("/api/v1/applications")); setErr(""); }
    catch (e) { setErr((e as Error).message); }
  };
  useEffect(() => { setConnected(isConnected()); if (isConnected()) load(); }, []);

  const create = async () => {
    setErr("");
    try {
      await api.post("/api/v1/applications", { name, nameAr: nameAr || null, parentId: parentId || null });
      setName(""); setNameAr(""); setParentId(""); await load();
    } catch (e) { setErr((e as Error).message); }
  };

  if (!connected) return <p className="muted">{tr("notConnected", lang)}</p>;

  return (
    <>
      <div className="topbar"><h1>{tr("applications", lang)}</h1></div>

      <div className="card">
        <div className="row">
          <div><label>{tr("name", lang)}</label><input value={name} onChange={(e) => setName(e.target.value)} /></div>
          <div><label>{tr("nameAr", lang)}</label><input value={nameAr} onChange={(e) => setNameAr(e.target.value)} dir="rtl" /></div>
          <div><label>Parent ID</label><input value={parentId} onChange={(e) => setParentId(e.target.value)} placeholder="(root if empty)" /></div>
        </div>
        <button onClick={create}>{tr("create", lang)}</button>
        {err && <div className="err">{err}</div>}
      </div>

      <table>
        <thead><tr><th>{tr("name", lang)}</th><th>{tr("nameAr", lang)}</th><th>ID</th><th>{tr("status", lang)}</th></tr></thead>
        <tbody>
          {apps.map((a) => (
            <tr key={a.id}>
              <td>{a.name}</td>
              <td dir="rtl">{a.nameAr ?? "—"}</td>
              <td className="muted">{a.id}</td>
              <td>{a.isActive ? <span className="badge ok">{tr("active", lang)}</span> : <span className="badge">—</span>}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}
