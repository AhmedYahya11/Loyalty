"use client";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useLang } from "@/app/providers";
import { tr, langName, nextLang } from "@/lib/i18n";
import { isCustConnected, clearCustSession } from "@/lib/customer";

const tabs = [
  { href: "/card", key: "myCard", ico: "💳" },
  { href: "/card/rewards", key: "myRewards", ico: "🎁" },
  { href: "/card/history", key: "history", ico: "🧾" },
];

export function CustomerShell({ children }: { children: React.ReactNode }) {
  const { lang, setLang } = useLang();
  const path = usePathname();
  const router = useRouter();
  const connected = typeof window !== "undefined" && isCustConnected();

  return (
    <div className="cust">
      <div className="cust-head">
        <div className="brand">
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img src="/img/logo.jpg" alt="" className="brand-logo" />
          {tr("appTitle", lang)}
        </div>
        <div style={{ display: "flex", gap: 8 }}>
          <button className="lang-toggle" onClick={() => setLang(nextLang[lang])}>
            🌐 {langName[nextLang[lang]]}
          </button>
          {connected && (
            <button className="lang-toggle" onClick={() => { clearCustSession(); router.push("/card"); location.reload(); }}>
              {tr("signOut", lang)}
            </button>
          )}
        </div>
      </div>
      <div className="cust-body">{children}</div>
      {connected && (
        <nav className="cust-nav">
          {tabs.map((t) => (
            <Link key={t.href} href={t.href} className={path === t.href ? "active" : ""}>
              <span className="ico">{t.ico}</span>{tr(t.key, lang)}
            </Link>
          ))}
        </nav>
      )}
    </div>
  );
}
