"use client";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useLang } from "@/app/providers";
import { tr } from "@/lib/i18n";

const links = [
  { href: "/", key: "connect" },
  { href: "/overview", key: "overview" },
  { href: "/applications", key: "applications" },
  { href: "/campaigns", key: "campaigns" },
  { href: "/segments", key: "segments" },
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
        <button className="lang-toggle" onClick={() => setLang(lang === "en" ? "ar" : "en")}>
          {lang === "en" ? "العربية" : "English"}
        </button>
      </nav>
      <main className="main">{children}</main>
    </div>
  );
}
