import "server-only";
import { isDemoPersona } from "./demo-personas";
import { beginLogin, completeLogin, type CompletedLogin } from "./oidc";

/**
 * One-click sign-in for the public demo. The visitor picks a demo persona; the BFF walks the normal Authorization Code + PKCE
 * flow on the server (Gatehouse email screen → Keycloak broker → the tenant's upstream IdP → callback) with the shared demo
 * password, which never reaches the browser. Off unless `GATEHOUSE_DEMO_SIGN_IN=true`, and only for the personas in
 * `demo-personas.ts`.
 */
export function isDemoSignInEnabled(): boolean {
  return process.env.GATEHOUSE_DEMO_SIGN_IN === "true" && (process.env.GATEHOUSE_DEMO_PASSWORD ?? "") !== "";
}

const MAX_HOPS = 20;

/** Cookies per origin and path, enough for two Keycloaks that use the same cookie names. */
export class CookieJar {
  private readonly cookies = new Map<string, Map<string, { value: string; path: string }>>();

  store(url: URL, setCookies: string[]): void {
    const jar = this.cookies.get(url.origin) ?? new Map<string, { value: string; path: string }>();
    for (const header of setCookies) {
      const [pair = "", ...attributes] = header.split(";");
      const eq = pair.indexOf("=");
      if (eq <= 0) continue;
      const name = pair.slice(0, eq).trim();
      const value = pair.slice(eq + 1).trim();
      const pathAttr = attributes.map((a) => a.trim()).find((a) => a.toLowerCase().startsWith("path="));
      const path = pathAttr ? pathAttr.slice(5) || "/" : "/";
      const expired = attributes.some((a) => /^\s*max-age=0\s*$/i.test(a)) || value === "";
      const key = `${path}|${name}`;
      if (expired) jar.delete(key);
      else jar.set(key, { value, path });
    }
    this.cookies.set(url.origin, jar);
  }

  header(url: URL): string {
    const jar = this.cookies.get(url.origin);
    if (!jar) return "";
    return [...jar.entries()]
      .filter(([, c]) => url.pathname.startsWith(c.path) || `${url.pathname}/` === c.path)
      .map(([key, c]) => `${key.slice(key.indexOf("|") + 1)}=${c.value}`)
      .join("; ");
  }
}

/** The first `<form>` of a Keycloak page: its action and its hidden and named inputs. */
export function parseForm(html: string, base: URL): { action: URL; fields: Record<string, string> } | null {
  const form = /<form\b[^>]*\baction="([^"]*)"[^>]*>([\s\S]*?)<\/form>/i.exec(html);
  if (!form) return null;
  const fields: Record<string, string> = {};
  for (const input of (form[2] ?? "").matchAll(/<input\b[^>]*>/gi)) {
    const tag = input[0];
    const name = /\bname="([^"]*)"/i.exec(tag)?.[1];
    if (!name) continue;
    fields[decodeEntities(name)] = decodeEntities(/\bvalue="([^"]*)"/i.exec(tag)?.[1] ?? "");
  }
  return { action: new URL(decodeEntities(form[1] ?? ""), base), fields };
}

function decodeEntities(value: string): string {
  return value
    .replace(/&amp;/g, "&")
    .replace(/&quot;/g, '"')
    .replace(/&#39;|&#x27;/g, "'")
    .replace(/&lt;/g, "<")
    .replace(/&gt;/g, ">")
    .replace(/&#x2F;|&#47;/g, "/")
    .replace(/&#x3D;|&#61;/g, "=");
}

/**
 * Optional rewrites for fetches made from inside a container: `GATEHOUSE_DEMO_URL_MAP="https://public/kc=http://keycloak:8080/kc"`
 * (comma-separated). Cookies and redirects keep using the public URLs.
 */
function urlMap(): [string, string][] {
  return (process.env.GATEHOUSE_DEMO_URL_MAP ?? "")
    .split(",")
    .map((entry) => entry.trim())
    .filter((entry) => entry.includes("="))
    .map((entry) => {
      const i = entry.indexOf("=");
      return [entry.slice(0, i).replace(/\/+$/, ""), entry.slice(i + 1).replace(/\/+$/, "")] as [string, string];
    });
}

function physical(url: URL, map: [string, string][]): string {
  const href = url.href;
  for (const [from, to] of map) if (href.startsWith(from)) return to + href.slice(from.length);
  return href;
}

type Fetch = typeof fetch;

/** Signs a demo persona in and returns the same result as the callback route would. */
export async function signInAsDemoPersona(
  email: string,
  returnTo: string,
  callbackPrefix: string,
  password = process.env.GATEHOUSE_DEMO_PASSWORD ?? "",
  fetcher: Fetch = fetch,
): Promise<CompletedLogin> {
  if (!isDemoPersona(email)) throw new Error("Not a demo persona.");
  const username = email.split("@")[0] ?? email;
  const start = await beginLogin(returnTo, email);
  const jar = new CookieJar();
  const map = urlMap();

  let url = start.url;
  let init: { method: string; body?: URLSearchParams } = { method: "GET" };
  for (let hop = 0; hop < MAX_HOPS; hop++) {
    if (url.href.startsWith(callbackPrefix)) return completeLogin(url, start.binding);

    const headers: Record<string, string> = { accept: "text/html" };
    const cookie = jar.header(url);
    if (cookie) headers.cookie = cookie;
    if (init.body) headers["content-type"] = "application/x-www-form-urlencoded";
    const response = await fetcher(physical(url, map), { method: init.method, headers, body: init.body, redirect: "manual" });
    jar.store(url, response.headers.getSetCookie());

    const location = response.headers.get("location");
    if (response.status >= 300 && response.status < 400 && location) {
      url = new URL(location, url);
      init = { method: "GET" };
      continue;
    }
    if (response.status !== 200) throw new Error(`Demo sign-in stopped at HTTP ${response.status}.`);

    const form = parseForm(await response.text(), url);
    if (!form) throw new Error("Demo sign-in found no form to continue with.");
    const fields = new URLSearchParams(form.fields);
    // Login forms get the persona; SAML auto-post and other hidden-field forms are submitted as they are.
    if ("username" in form.fields || "password" in form.fields) {
      fields.set("username", url.pathname.includes("/realms/gatehouse/") ? email : username);
      if ("password" in form.fields) fields.set("password", password);
    }
    url = form.action;
    init = { method: "POST", body: fields };
  }
  throw new Error("Demo sign-in took too many steps.");
}
