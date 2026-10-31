"use client";
import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { useLang } from "@/app/providers";
import { tr } from "@/lib/i18n";
import { getApiBase, getApiKey, setSession, setAdminSession, getAdminUser } from "@/lib/session";
import { api } from "@/lib/api";

type LoginResponse = { token: string; username: string; role: string; applicationId: string };
type MeResponse = { canManageUsers: boolean };

export default function LoginPage() {
  const { lang } = useLang();
  const router = useRouter();
  const [username, setUsername] = useState("superadmin");
  const [password, setPassword] = useState("");
  const [err, setErr] = useState("");
  const [busy, setBusy] = useState(false);
  const [showAdvanced, setShowAdvanced] = useState(false);

  // advanced (legacy API key / bootstrap)
  const [base, setBase] = useState("http://localhost:8080");
  const [key, setKey] = useState("");
  const [rootName, setRootName] = useState("Hareer");
  const [token, setToken] = useState("dev-bootstrap-token-change-me");
  const [msg, setMsg] = useState("");

  useEffect(() => { setBase(getApiBase()); setKey(getApiKey()); if (getAdminUser()) router.replace("/overview"); }, [router]);

  const login = async () => {
    setErr(""); setBusy(true);
    try {
      const r = await api.post<LoginResponse>("/api/v1/admin/login", { username, password });
      setAdminSession(r.token, r.username, r.role, false);
      let manage = false;
      try { manage = (await api.get<MeResponse>("/api/v1/admin/me")).canManageUsers; } catch {}
      setAdminSession(r.token, r.username, r.role, manage);
      router.push("/overview");
    } catch (e) { setErr((e as Error).message); } finally { setBusy(false); }
  };

  const saveKey = () => { setSession(base, key); setMsg(tr("saved", lang)); setErr(""); };
  const bootstrap = async () => {
    setErr(""); setMsg("");
    try {
      setSession(base, key);
      const res = await api.post<{ rootAccount?: { apiKey: string } }>("/api/v1/bootstrap", { name: rootName }, { "X-Bootstrap-Token": token });
      if (res.rootAccount?.apiKey) { setKey(res.rootAccount.apiKey); setSession(base, res.rootAccount.apiKey); setMsg("Root created. API key saved."); }
      else setMsg("Root already exists. Enter an existing API key.");
    } catch (e) { setErr((e as Error).message); }
  };

  return (
    <div style={{ maxWidth: 420, margin: "8vh auto" }}>
      <h1 style={{ textAlign: "center", marginBottom: 4 }}>💠 {tr("appTitle", lang)}</h1>
      <p className="muted" style={{ textAlign: "center", marginBottom: 20 }}>{tr("signIn", lang)}</p>

      <div className="card">
        <label>{tr("username", lang)}</label>
        <input value={username} onChange={(e) => setUsername(e.target.value)} autoFocus onKeyDown={(e) => e.key === "Enter" && login()} />
        <label>{tr("password", lang)}</label>
        <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} onKeyDown={(e) => e.key === "Enter" && login()} />
        <button onClick={login} disabled={busy} style={{ width: "100%" }}>{busy ? "…" : tr("signIn", lang)}</button>
        {err && <div className="err">{err}</div>}
      </div>

      <p className="muted" style={{ textAlign: "center", cursor: "pointer" }} onClick={() => setShowAdvanced((v) => !v)}>
        {showAdvanced ? "▾" : "▸"} {tr("advancedConnect", lang)}
      </p>
      {showAdvanced && (
        <div className="card">
          <label>{tr("apiBase", lang)}</label>
          <input value={base} onChange={(e) => setBase(e.target.value)} />
          <label>{tr("apiKey", lang)}</label>
          <input value={key} onChange={(e) => setKey(e.target.value)} placeholder="lk_live_…" />
          <div className="row"><button className="secondary" onClick={saveKey}>{tr("save", lang)}</button></div>
          <h2>{tr("bootstrap", lang)}</h2>
          <p className="muted">First run only — creates the root application + an API key (shown once).</p>
          <div className="row">
            <div><label>{tr("rootName", lang)}</label><input value={rootName} onChange={(e) => setRootName(e.target.value)} /></div>
            <div><label>{tr("bootstrapToken", lang)}</label><input value={token} onChange={(e) => setToken(e.target.value)} /></div>
          </div>
          <button className="secondary" onClick={bootstrap}>{tr("bootstrap", lang)}</button>
          {msg && <div className="ok-msg">{msg}</div>}
        </div>
      )}
    </div>
  );
}
