// Customer API client — injects the member Bearer token + Accept-Language. عميل واجهة العميل.
import { getCustBase, getMemberToken } from "./customer";
import { getLang } from "./session";

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    "Accept-Language": getLang(),
  };
  const token = getMemberToken();
  if (token) headers["Authorization"] = `Bearer ${token}`;

  const res = await fetch(`${getCustBase()}${path}`, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  const text = await res.text();
  const json = text ? JSON.parse(text) : null;
  if (!res.ok) throw new Error(json?.message ?? `${res.status} ${res.statusText}`);
  return json as T;
}

export const capi = {
  get: <T>(path: string) => request<T>("GET", path),
  post: <T>(path: string, body?: unknown) => request<T>("POST", path, body),
};
