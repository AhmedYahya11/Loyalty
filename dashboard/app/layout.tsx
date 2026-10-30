import "./globals.css";
import type { Metadata } from "next";
import { Providers } from "./providers";
import { Shell } from "@/components/Shell";

export const metadata: Metadata = {
  title: "Loyalty Platform · منصة الولاء",
  description: "Multi-tenant loyalty & rewards dashboard",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" dir="ltr">
      <body>
        <Providers>
          <Shell>{children}</Shell>
        </Providers>
      </body>
    </html>
  );
}
