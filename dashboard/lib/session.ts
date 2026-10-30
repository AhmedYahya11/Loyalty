// Client-side session: API base URL + key stored in localStorage. جلسة العميل.
import type { Lang } from "./i18n";

const BASE = "loyalty.apiBase";
const KEY = "loyalty.apiKey";
const LANG = "loyalty.lang";

export const getApiBase = () =>
  (typeof window !== "undefined" && localStorage.getItem(BASE)) ||
  process.env.NEXT_PUBLIC_LOYALTY_API_BASE ||
  "http://localhost:8080";
export const getApiKey = () =>
  (typeof window !== "undefined" && localStorage.getItem(KEY)) ||
  process.env.NEXT_PUBLIC_LOYALTY_API_KEY ||
  "";
export const setSession = (base: string, key: string) => {
  localStorage.setItem(BASE, base);
  localStorage.setItem(KEY, key);
};
export const getLang = (): Lang =>
  ((typeof window !== "undefined" && (localStorage.getItem(LANG) as Lang)) || "en");
export const setLang = (lang: Lang) => localStorage.setItem(LANG, lang);
export const isConnected = () => !!getApiKey();
