"use client";
import { useState } from "react";
import { useLang } from "../providers";
import { tr } from "@/lib/i18n";
import { api } from "@/lib/api";
import { isConnected } from "@/lib/session";

type Submit = { requestId: string; status: string };
type Status = { requestId: string; status: string; awardedPoints?: number; pointsUnitCode?: string; statusReason?: string };

export default function EventsPage() {
  const { lang } = useLang();
  const [customerId, setCustomerId] = useState("hareer-user-100");
  const [amount, setAmount] = useState("50000");
  const [requestId, setRequestId] = useState("");
  const [status, setStatus] = useState<Status | null>(null);
  const [err, setErr] = useState("");

  const submit = async () => {
    setErr(""); setStatus(null);
    const orderId = "ORDER-" + Math.floor(Date.now() / 1000);
    try {
      const res = await api.post<Submit>("/api/v1/events", {
        eventId: crypto.randomUUID(), eventType: "PurchaseCompleted", eventVersion: 1,
        sourceTransactionId: orderId, customerId, occurredAt: new Date().toISOString(), correlationId: orderId,
        attributes: { orderId, amount: Number(amount), currency: "IQD" },
      }, { "Idempotency-Key": `dashboard:purchase:${orderId}` });
      setRequestId(res.requestId);
      // poll a few times
      for (let i = 0; i < 20; i++) {
        const s = await api.get<Status>(`/api/v1/requests/${res.requestId}`);
        setStatus(s);
        if (["Completed", "Rejected", "Failed"].includes(s.status)) break;
        await new Promise((r) => setTimeout(r, 500));
      }
    } catch (e) { setErr((e as Error).message); }
  };

  const lookup = async () => {
    setErr("");
    try { setStatus(await api.get<Status>(`/api/v1/requests/${requestId}`)); }
    catch (e) { setErr((e as Error).message); }
  };

  if (!isConnected()) return <p className="muted">{tr("notConnected", lang)}</p>;

  return (
    <>
      <div className="topbar"><h1>{tr("events", lang)}</h1></div>

      <h2>{tr("submitTestEvent", lang)}</h2>
      <div className="card">
        <div className="row">
          <div><label>{tr("externalCustomerId", lang)}</label><input value={customerId} onChange={(e) => setCustomerId(e.target.value)} /></div>
          <div><label>{tr("amount", lang)}</label><input value={amount} onChange={(e) => setAmount(e.target.value)} /></div>
        </div>
        <button onClick={submit}>{tr("submitTestEvent", lang)}</button>
        {err && <div className="err">{err}</div>}
      </div>

      <h2>{tr("requestId", lang)}</h2>
      <div className="card">
        <input value={requestId} onChange={(e) => setRequestId(e.target.value)} placeholder="request id" />
        <button className="secondary" onClick={lookup}>{tr("lookup", lang)}</button>
        {status && (
          <table style={{ marginTop: 14 }}>
            <tbody>
              <tr><th>{tr("status", lang)}</th><td>{status.status === "Completed" ? <span className="badge ok">{status.status}</span> : <span className="badge">{status.status}</span>}</td></tr>
              <tr><th>{tr("awardedPoints", lang)}</th><td>{status.awardedPoints ?? "—"} {status.pointsUnitCode ?? ""}</td></tr>
              {status.statusReason && <tr><th>Reason</th><td>{status.statusReason}</td></tr>}
            </tbody>
          </table>
        )}
      </div>
    </>
  );
}
