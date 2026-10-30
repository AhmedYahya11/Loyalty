"use client";
import { createContext, useContext, useEffect, useState } from "react";
import { dir, type Lang } from "@/lib/i18n";
import { getLang, setLang as persistLang } from "@/lib/session";

type Ctx = { lang: Lang; setLang: (l: Lang) => void };
const LangContext = createContext<Ctx>({ lang: "en", setLang: () => {} });
export const useLang = () => useContext(LangContext);

export function Providers({ children }: { children: React.ReactNode }) {
  const [lang, setLangState] = useState<Lang>("en");

  useEffect(() => {
    setLangState(getLang());
  }, []);

  useEffect(() => {
    document.documentElement.lang = lang;
    document.documentElement.dir = dir(lang);
  }, [lang]);

  const setLang = (l: Lang) => {
    persistLang(l);
    setLangState(l);
  };

  return <LangContext.Provider value={{ lang, setLang }}>{children}</LangContext.Provider>;
}
