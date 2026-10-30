"use client";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useLang } from "@/app/providers";
import { tr, langName, nextLang } from "@/lib/i18n";

const links = [
  { href: "/", key: "connect" },
  { href: "/overview", key: "overview" },
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

  return (
    <div className="shell">
      <nav className="sidebar">
        <div className="brand">{tr("appTitle", lang)}</div>
        {links.map((l) => (
          <Link key={l.href} href={l.href} className={`navlink ${path === l.href ? "active" : ""}`}>
            {tr(l.key, lang)}
          </Link>
        ))}
        <div style={{ flex: 1 }} />
        <Link href="/card" className="navlink" style={{ marginBottom: 8 }}>💳 {tr("myCard", lang)}</Link>
        <button className="lang-toggle" onClick={() => setLang(nextLang[lang])}>
          🌐 {langName[nextLang[lang]]}
        </button>
      </nav>
      <main className="main">{children}</main>
    </div>
  );
}
