"use client";
import { useState } from "react";
import { useRouter } from "next/navigation";
import { useLang } from "@/app/providers";
import { tr } from "@/lib/i18n";
import { api } from "@/lib/api";
import { isConnected, getAdminUser, getAdminRole, clearAdminSession } from "@/lib/session";

export default function AccountPage() {
  const { lang } = useLang();
  const router = useRouter();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [confirm, setConfirm] = useState("");
  const [msg, setMsg] = useState(""); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);

  const submit = async () => {
    setErr(""); setMsg("");
    if (next.length < 8) { setErr(tr("password", lang) + " ≥ 8"); return; }
    if (next !== confirm) { setErr(tr("passwordMismatch", lang)); return; }
    setBusy(true);
    try {
      await api.post("/api/v1/admin/change-password", { currentPassword: current, newPassword: next });
      setMsg(tr("passwordChanged", lang)); setCurrent(""); setNext(""); setConfirm("");
    } catch (e) { setErr((e as Error).message); } finally { setBusy(false); }
  };

  if (!isConnected()) return <p className="muted">{tr("notConnected", lang)}</p>;

  return (
    <>
      <div className="topbar"><h1>{tr("account", lang)}</h1></div>

      <div className="card">
        <table><tbody>
          <tr><th>{tr("username", lang)}</th><td>👤 {getAdminUser() || "—"}</td></tr>
          <tr><th>{tr("myRole", lang)}</th><td><span className="badge">{getAdminRole() || "—"}</span></td></tr>
        </tbody></table>
      </div>

      <h2>{tr("changePassword", lang)}</h2>
      <div className="card" style={{ maxWidth: 440 }}>
        <label>{tr("currentPassword", lang)}</label>
        <input type="password" value={current} onChange={(e) => setCurrent(e.target.value)} />
        <label>{tr("newPassword", lang)}</label>
        <input type="password" value={next} onChange={(e) => setNext(e.target.value)} />
        <label>{tr("confirmPassword", lang)}</label>
        <input type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} onKeyDown={(e) => e.key === "Enter" && submit()} />
        <button onClick={submit} disabled={busy}>{busy ? "…" : tr("changePassword", lang)}</button>
        {msg && <div className="ok-msg">{msg}</div>}
        {err && <div className="err">{err}</div>}
        <p className="muted" style={{ marginTop: 10 }}>Other sessions are signed out after a change.</p>
      </div>
    </>
  );
}
