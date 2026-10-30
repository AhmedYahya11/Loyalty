"use client";
import { useEffect, useState } from "react";
import { useLang } from "@/app/providers";
import { tr } from "@/lib/i18n";
import { getApiBase, getApiKey, setSession } from "@/lib/session";
import { api } from "@/lib/api";

export default function ConnectPage() {
  const { lang } = useLang();
  const [base, setBase] = useState("http://localhost:8080");
  const [key, setKey] = useState("");
  const [rootName, setRootName] = useState("Hareer");
  const [token, setToken] = useState("dev-bootstrap-token-change-me");
  const [msg, setMsg] = useState("");
  const [err, setErr] = useState("");

  useEffect(() => {
    setBase(getApiBase());
    setKey(getApiKey());
  }, []);

  const save = () => {
    setSession(base, key);
    setMsg(tr("saved", lang));
    setErr("");
  };

  const bootstrap = async () => {
    setErr(""); setMsg("");
    try {
      setSession(base, key);
      const res = await api.post<{ rootAccount?: { apiKey: string }; created: boolean }>(
        "/api/v1/bootstrap", { name: rootName }, { "X-Bootstrap-Token": token });
      if (res.rootAccount?.apiKey) {
        setKey(res.rootAccount.apiKey);
        setSession(base, res.rootAccount.apiKey);
        setMsg("Root created. API key saved. " + tr("saved", lang));
      } else {
        setMsg("Root already exists. Enter an existing API key.");
      }
    } catch (e) { setErr((e as Error).message); }
  };

  return (
    <>
      <div className="topbar"><h1>{tr("connect", lang)}</h1></div>

      <div className="card">
        <label>{tr("apiBase", lang)}</label>
        <input value={base} onChange={(e) => setBase(e.target.value)} />
        <label>{tr("apiKey", lang)}</label>
        <input value={key} onChange={(e) => setKey(e.target.value)} placeholder="lk_live_…" />
        <button onClick={save}>{tr("save", lang)}</button>
        {msg && <div className="ok-msg">{msg}</div>}
        {err && <div className="err">{err}</div>}
      </div>

      <h2>{tr("bootstrap", lang)}</h2>
      <div className="card">
        <p className="muted">First run only — creates the root application and returns an API key once.</p>
        <div className="row">
          <div>
            <label>{tr("rootName", lang)}</label>
            <input value={rootName} onChange={(e) => setRootName(e.target.value)} />
          </div>
          <div>
            <label>{tr("bootstrapToken", lang)}</label>
            <input value={token} onChange={(e) => setToken(e.target.value)} />
          </div>
        </div>
        <button onClick={bootstrap}>{tr("bootstrap", lang)}</button>
      </div>
    </>
  );
}
