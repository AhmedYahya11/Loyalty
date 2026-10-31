import "./globals.css";
import type { Metadata } from "next";
import { Providers } from "./providers";

export const metadata: Metadata = {
  title: "KĪNŪTU · كينوتو",
  description: "KĪNŪTU — multi-tenant loyalty & rewards platform",
};

// Runs before React hydrates: set <html> lang/dir from the saved language so there is no
// left-to-right → right-to-left flash on refresh (ar/ku are RTL).
const setInitialLang = `(function(){try{var l=localStorage.getItem('loyalty.lang')||'en';var rtl=l==='ar'||l==='ku';document.documentElement.lang=l;document.documentElement.dir=rtl?'rtl':'ltr';}catch(e){}})();`;

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" dir="ltr" suppressHydrationWarning>
      <head><script dangerouslySetInnerHTML={{ __html: setInitialLang }} /></head>
      <body>
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
