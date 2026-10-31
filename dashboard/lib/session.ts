// Client-side session: API base URL + key stored in localStorage. جلسة العميل.
import type { Lang } from "./i18n";

const BASE = "loyalty.apiBase";
const KEY = "loyalty.apiKey";
const LANG = "loyalty.lang";
const ADMIN = "loyalty.adminToken";
const ADMIN_USER = "loyalty.adminUser";
const ADMIN_ROLE = "loyalty.adminRole";
const ADMIN_MANAGE = "loyalty.adminManage";

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

// --- admin session (username/password login) ---
export const getAdminToken = () => (typeof window !== "undefined" && localStorage.getItem(ADMIN)) || "";
export const getAdminUser = () => (typeof window !== "undefined" && localStorage.getItem(ADMIN_USER)) || "";
export const getAdminRole = () => (typeof window !== "undefined" && localStorage.getItem(ADMIN_ROLE)) || "";
export const canManageUsers = () => typeof window !== "undefined" && localStorage.getItem(ADMIN_MANAGE) === "1";
export const setAdminSession = (token: string, username: string, role: string, manage: boolean) => {
  localStorage.setItem(ADMIN, token);
  localStorage.setItem(ADMIN_USER, username);
  localStorage.setItem(ADMIN_ROLE, role);
  localStorage.setItem(ADMIN_MANAGE, manage ? "1" : "0");
};
export const clearAdminSession = () => [ADMIN, ADMIN_USER, ADMIN_ROLE, ADMIN_MANAGE].forEach((k) => localStorage.removeItem(k));

// connected = logged in as an admin, or (legacy) an API key is set
export const isConnected = () => !!getAdminToken() || !!getApiKey();
