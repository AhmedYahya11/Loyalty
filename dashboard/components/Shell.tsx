"use client";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { useLang } from "@/app/providers";
import { tr, langName, nextLang } from "@/lib/i18n";
import { isConnected, getAdminUser, getAdminRole, canManageUsers, clearAdminSession } from "@/lib/session";

const links = [
  { href: "/overview", key: "overview" },
  { href: "/customers", key: "customers" },
  { href: "/rfm", key: "rfm" },
  { href: "/reports", key: "reports" },
  { href: "/applications", key: "applications" },
  { href: "/campaigns", key: "campaigns" },
  { href: "/segments", key: "segments" },
  { href: "/tiers", key: "tiers" },
  { href: "/rewards", key: "rewards" },
  { href: "/gamification", key: "gamification" },
  { href: "/engagement", key: "engagement" },
  { href: "/raffles", key: "raffles" },
  { href: "/members", key: "members" },
  { href: "/events", key: "events" },
  { href: "/notifications", key: "notifications" },
  { href: "/webhooks", key: "webhooks" },
];

export function Shell({ children }: { children: React.ReactNode }) {
  const { lang, setLang } = useLang();
  const path = usePathname();
  const router = useRouter();
  const [ready, setReady] = useState(false);
  useEffect(() => { setReady(true); }, []);

  // the login page renders full-screen, without the dashboard chrome
  const isLogin = path === "/";
  useEffect(() => { if (ready && !isLogin && !isConnected()) router.replace("/"); }, [ready, isLogin, router]);

  if (isLogin) return <>{children}</>;
  if (!ready) return null;

  const manage = canManageUsers();
  const logout = () => { clearAdminSession(); router.push("/"); };

  return (
    <div className="shell">
      <nav className="sidebar">
        <div className="brand">{tr("appTitle", lang)}</div>
        {links.map((l) => (
          <Link key={l.href} href={l.href} className={`navlink ${path === l.href ? "active" : ""}`}>
            {tr(l.key, lang)}
          </Link>
        ))}
        {manage && (
          <Link href="/roles" className={`navlink ${path === "/roles" ? "active" : ""}`}>🔐 {tr("roles", lang)}</Link>
        )}
        <div style={{ flex: 1 }} />
        {getAdminUser() && (
          <Link href="/account" className={`navlink ${path === "/account" ? "active" : ""}`} style={{ fontSize: 12 }}>
            👤 {getAdminUser()} · <span className="badge">{getAdminRole()}</span>
          </Link>
        )}
        <Link href="/card" className="navlink" style={{ marginBottom: 8 }}>💳 {tr("myCard", lang)}</Link>
        <div className="row" style={{ gap: 8, margin: 0 }}>
          <button className="lang-toggle" onClick={() => setLang(nextLang[lang])}>🌐 {langName[nextLang[lang]]}</button>
          {getAdminUser() && <button className="lang-toggle" onClick={logout}>{tr("logout", lang)}</button>}
        </div>
      </nav>
      <main className="main">{children}</main>
    </div>
  );
}
