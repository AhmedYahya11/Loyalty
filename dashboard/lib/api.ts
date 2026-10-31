// Thin Loyalty API client — injects X-Api-Key + Accept-Language. عميل الواجهة البرمجية.
import { getApiBase, getApiKey, getAdminToken, getLang } from "./session";

async function request<T>(method: string, path: string, body?: unknown, extraHeaders?: Record<string, string>): Promise<T> {
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    "Accept-Language": getLang(),
    ...extraHeaders,
  };
  // an admin session (username/password login) takes precedence; API key is the legacy fallback
  const adminToken = getAdminToken();
  const key = getApiKey();
  if (adminToken) headers["Authorization"] = `Bearer ${adminToken}`;
  else if (key) headers["X-Api-Key"] = key;

  const res = await fetch(`${getApiBase()}${path}`, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  const text = await res.text();
  const json = text ? JSON.parse(text) : null;
  if (!res.ok) throw new Error(json?.message ?? `${res.status} ${res.statusText}`);
  return json as T;
}

export const api = {
  get: <T>(path: string) => request<T>("GET", path),
  post: <T>(path: string, body?: unknown, headers?: Record<string, string>) => request<T>("POST", path, body, headers),
  put: <T>(path: string, body?: unknown) => request<T>("PUT", path, body),
  patch: <T>(path: string, body?: unknown) => request<T>("PATCH", path, body),
  del: <T>(path: string) => request<T>("DELETE", path),
};
