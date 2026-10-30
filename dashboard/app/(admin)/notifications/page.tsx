"use client";
import { useEffect, useState } from "react";
import { useLang } from "@/app/providers";
import { tr } from "@/lib/i18n";
import { api } from "@/lib/api";
import { isConnected } from "@/lib/session";

type Item = { id: string; eventType: string; memberId?: string; payload: Record<string, unknown>; occurredAt: string; deliveredAt?: string };

export default function NotificationsPage() {
  const { lang } = useLang();
  const [items, setItems] = useState<Item[]>([]);
  const [err, setErr] = useState("");

  const load = () => api.get<{ items: Item[] }>("/api/v1/notifications").then((r) => setItems(r.items)).catch((e) => setErr((e as Error).message));
  useEffect(() => { if (isConnected()) load(); }, []);

  if (!isConnected()) return <p className="muted">{tr("notConnected", lang)}</p>;

  return (
    <>
      <div className="topbar"><h1>{tr("notifications", lang)}</h1><button className="lang-toggle" onClick={load}>↻</button></div>
      {err && <div className="err">{err}</div>}
      <table>
        <thead><tr><th>{tr("eventType", lang)}</th><th>{tr("points", lang)}</th><th>{tr("when", lang)}</th><th>{tr("deliveredAt", lang)}</th></tr></thead>
        <tbody>
          {items.map((i) => (
            <tr key={i.id}>
              <td><span className="badge ok">{i.eventType}</span></td>
              <td>{String(i.payload.awardedPoints ?? i.payload.reversedPoints ?? i.payload.signedPoints ?? "—")}</td>
              <td className="muted">{new Date(i.occurredAt).toLocaleString()}</td>
              <td className="muted">{i.deliveredAt ? new Date(i.deliveredAt).toLocaleString() : "—"}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}
