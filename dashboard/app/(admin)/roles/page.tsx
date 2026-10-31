"use client";
import { useEffect, useState } from "react";
import { useLang } from "@/app/providers";
import { tr } from "@/lib/i18n";
import { api } from "@/lib/api";
import { isConnected, getAdminRole } from "@/lib/session";

type RoleDef = { role: string; description: string };
type AdminUser = { id: string; username: string; role: string; isActive: boolean; createdAt: string };

export default function RolesPage() {
  const { lang } = useLang();
  const [roles, setRoles] = useState<RoleDef[]>([]);
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [uName, setUName] = useState(""); const [uPass, setUPass] = useState(""); const [uRole, setURole] = useState("Manager");
  const [err, setErr] = useState(""); const [msg, setMsg] = useState("");
  const isSuper = getAdminRole() === "SuperAdmin";

  const load = async () => {
    try {
      setRoles(await api.get<RoleDef[]>("/api/v1/admin/roles"));
      setUsers(await api.get<AdminUser[]>("/api/v1/admin/users"));
    } catch (e) { setErr((e as Error).message); }
  };
  useEffect(() => { if (isConnected()) load(); }, []);

  const create = async () => {
    setErr(""); setMsg("");
    try { await api.post("/api/v1/admin/users", { username: uName, password: uPass, role: uRole }); setUName(""); setUPass(""); setMsg("✓"); await load(); }
    catch (e) { setErr((e as Error).message); }
  };
  const setRole = async (id: string, role: string) => { try { await api.put(`/api/v1/admin/users/${id}/role`, { role }); await load(); } catch (e) { setErr((e as Error).message); } };
  const deactivate = async (id: string) => { try { await api.post(`/api/v1/admin/users/${id}/deactivate`, {}); await load(); } catch (e) { setErr((e as Error).message); } };

  if (!isConnected()) return <p className="muted">{tr("notConnected", lang)}</p>;

  const roleNames = roles.map((r) => r.role).filter((r) => isSuper || r !== "SuperAdmin");

  return (
    <>
      <div className="topbar"><h1>🔐 {tr("roles", lang)}</h1></div>
      {err && <div className="err">{err}</div>}
      {msg && <div className="badge ok">{msg}</div>}

      <h2>{tr("role", lang)}</h2>
      <div className="card">
        {roles.map((r) => (
          <div key={r.role} className="stat"><span><b>{r.role}</b></span><span className="muted">{r.description}</span></div>
        ))}
      </div>

      <h2>{tr("createUser", lang)}</h2>
      <div className="card">
        <div className="row" style={{ alignItems: "end" }}>
          <div><label>{tr("username", lang)}</label><input value={uName} onChange={(e) => setUName(e.target.value)} /></div>
          <div><label>{tr("password", lang)}</label><input type="password" value={uPass} onChange={(e) => setUPass(e.target.value)} /></div>
          <div><label>{tr("role", lang)}</label><select value={uRole} onChange={(e) => setURole(e.target.value)}>{roleNames.map((r) => <option key={r}>{r}</option>)}</select></div>
          <button onClick={create}>{tr("createUser", lang)}</button>
        </div>
      </div>

      <h2>{tr("users", lang)}</h2>
      <table>
        <thead><tr><th>{tr("username", lang)}</th><th>{tr("role", lang)}</th><th>{tr("status", lang)}</th><th></th></tr></thead>
        <tbody>
          {users.map((u) => (
            <tr key={u.id}>
              <td>👤 {u.username}</td>
              <td>
                <select value={u.role} onChange={(e) => setRole(u.id, e.target.value)} style={{ maxWidth: 160 }}>
                  {roles.map((r) => r.role).filter((r) => isSuper || r !== "SuperAdmin").map((r) => <option key={r}>{r}</option>)}
                </select>
              </td>
              <td>{u.isActive ? <span className="badge ok">{tr("active", lang)}</span> : <span className="badge">—</span>}</td>
              <td>{u.isActive && <button className="secondary" style={{ margin: 0 }} onClick={() => deactivate(u.id)}>{tr("deactivate", lang)}</button>}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}
