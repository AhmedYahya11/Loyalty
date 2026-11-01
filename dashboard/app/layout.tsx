import "./globals.css";
import type { Metadata } from "next";
import { Providers } from "./providers";

export const metadata: Metadata = {
  title: "KĪNŪTU · كينوتو",
  description: "KĪNŪTU — multi-tenant loyalty & rewards platform",
  icons: { icon: "/img/logo.jpg" },
};

// Runs before React hydrates: set <html> lang/dir from the saved language so there is no
// left-to-right → right-to-left flash on refresh (ar/ku are RTL).
const setInitialLang = `(function(){try{var l=localStorage.getItem('loyalty.lang')||'en';var rtl=l==='ar'||l==='ku';document.documentElement.lang=l;document.documentElement.dir=rtl?'rtl':'ltr';}catch(e){}})();`;

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" dir="ltr" suppressHydrationWarning>
      <head>
        <script dangerouslySetInnerHTML={{ __html: setInitialLang }} />
        <link rel="preconnect" href="https://fonts.googleapis.com" />
        <link rel="preconnect" href="https://fonts.gstatic.com" crossOrigin="anonymous" />
        {/* non-blocking font load: start as print media, flip to all once loaded.
            lowercase `onload` is rendered as a literal HTML attribute by React. */}
        <link
          rel="stylesheet"
          media="print"
          href="https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@500;600;700;800&family=Inter:wght@400;500;600&family=IBM+Plex+Sans+Arabic:wght@400;500;600;700&display=swap"
          {...({ onload: "this.media='all'" } as Record<string, string>)}
        />
        <noscript>
          <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@500;600;700;800&family=Inter:wght@400;500;600&family=IBM+Plex+Sans+Arabic:wght@400;500;600;700&display=swap" />
        </noscript>
      </head>
      <body>
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
