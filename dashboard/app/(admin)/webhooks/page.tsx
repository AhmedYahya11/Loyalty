"use client";
import { useEffect, useState } from "react";
import { useLang } from "@/app/providers";
import { tr } from "@/lib/i18n";
import { api } from "@/lib/api";
import { isConnected } from "@/lib/session";

type Hook = { id: string; url: string; eventTypes: string[]; isActive: boolean };

export default function WebhooksPage() {
  const { lang } = useLang();
  const [hooks, setHooks] = useState<Hook[]>([]);
  const [url, setUrl] = useState("");
  const [secret, setSecret] = useState("");
  const [err, setErr] = useState("");

  const load = () => api.get<Hook[]>("/api/v1/webhooks").then(setHooks).catch((e) => setErr((e as Error).message));
  useEffect(() => { if (isConnected()) load(); }, []);

  const create = async () => {
    setErr(""); setSecret("");
    try {
      const res = await api.post<{ secret: string }>("/api/v1/webhooks", { url, eventTypes: [] });
      setSecret(res.secret); setUrl(""); await load();
    } catch (e) { setErr((e as Error).message); }
  };
  const toggle = async (h: Hook) => {
    try { await api.patch(`/api/v1/webhooks/${h.id}/status`, { isActive: !h.isActive }); await load(); } catch (e) { setErr((e as Error).message); }
  };
  const remove = async (id: string) => {
    try { await api.del(`/api/v1/webhooks/${id}`); await load(); } catch (e) { setErr((e as Error).message); }
  };
  const dispatch = async () => {
    try { const r = await api.post<{ dispatched: number }>("/api/v1/webhooks/dispatch"); await load(); alert(`dispatched ${r.dispatched}`); } catch (e) { setErr((e as Error).message); }
  };

  if (!isConnected()) return <p className="muted">{tr("notConnected", lang)}</p>;

  return (
    <>
      <div className="topbar"><h1>{tr("webhooks", lang)}</h1><button className="lang-toggle" onClick={dispatch}>{tr("dispatch", lang)}</button></div>
      <div className="card">
        <label>{tr("url", lang)}</label>
        <input value={url} onChange={(e) => setUrl(e.target.value)} placeholder="https://example.com/hook" />
        <button onClick={create}>{tr("addWebhook", lang)}</button>
        {secret && <div className="ok-msg">{tr("secret", lang)}: <code>{secret}</code> — save it now, shown once.</div>}
        {err && <div className="err">{err}</div>}
      </div>

      <table>
        <thead><tr><th>{tr("url", lang)}</th><th>{tr("status", lang)}</th><th></th></tr></thead>
        <tbody>
          {hooks.map((h) => (
            <tr key={h.id}>
              <td className="muted">{h.url}</td>
              <td>{h.isActive ? <span className="badge ok">{tr("active", lang)}</span> : <span className="badge">—</span>}</td>
              <td style={{ display: "flex", gap: 8 }}>
                <button className="secondary" style={{ margin: 0 }} onClick={() => toggle(h)}>{h.isActive ? tr("pause", lang) : tr("activate", lang)}</button>
                <button className="secondary" style={{ margin: 0 }} onClick={() => remove(h.id)}>{tr("delete", lang)}</button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}
