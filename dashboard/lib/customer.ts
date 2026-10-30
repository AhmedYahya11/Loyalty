// Customer-app session: API base + member access token in localStorage. جلسة العميل.
const BASE = "loyalty.custBase";
const TOKEN = "loyalty.memberToken";

export const getCustBase = () =>
  (typeof window !== "undefined" && localStorage.getItem(BASE)) ||
  process.env.NEXT_PUBLIC_LOYALTY_API_BASE ||
  "http://localhost:8080";
export const getMemberToken = () =>
  (typeof window !== "undefined" && localStorage.getItem(TOKEN)) || "";
export const setCustSession = (token: string, base?: string) => {
  localStorage.setItem(TOKEN, token);
  if (base) localStorage.setItem(BASE, base);
};
export const clearCustSession = () => localStorage.removeItem(TOKEN);
export const isCustConnected = () => !!getMemberToken();
