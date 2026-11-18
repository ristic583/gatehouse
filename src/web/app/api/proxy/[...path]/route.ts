import { NextResponse, type NextRequest } from "next/server";
import { getAuthConfig } from "@/lib/auth/config";
import { getCurrentPlatformSession, getCurrentSession } from "@/lib/auth/current-session";
import { PLATFORM_SESSION_COOKIES, proxySessionFor } from "@/lib/auth/platform-session";
import { isSameOrigin } from "@/lib/auth/csrf";

interface RouteContext {
  params: Promise<{ path: string[] }>;
}

const FORWARDED_REQUEST_HEADERS = ["accept", "content-type"];
const FORWARDED_RESPONSE_HEADERS = ["content-type", "location", "etag", "cache-control", "x-content-type-options", "content-disposition"];

async function forward(request: NextRequest, context: RouteContext): Promise<Response> {
  const config = getAuthConfig();
  if (request.method !== "GET" && !isSameOrigin(request, config.publicUrl)) {
    return NextResponse.json({ error: "forbidden" }, { status: 403 });
  }

  const { path } = await context.params;
  // ADR-009 §1: platform paths only with a platform session, every other path only with a tenant session.
  const wanted = proxySessionFor(path, {
    tenant: true,
    platform: PLATFORM_SESSION_COOKIES.some((name) => request.cookies.has(name)),
  });
  const accessToken =
    wanted === "platform"
      ? (await getCurrentPlatformSession())?.data.accessToken
      : wanted === "tenant"
        ? (await getCurrentSession())?.data.accessToken
        : undefined;
  if (accessToken === undefined) {
    return NextResponse.json({ error: "unauthorized" }, { status: 401 });
  }

  const target = `${config.apiBaseUrl}/api/${path.map(encodeURIComponent).join("/")}${request.nextUrl.search}`;

  const headers = new Headers({ Authorization: `Bearer ${accessToken}` });
  for (const name of FORWARDED_REQUEST_HEADERS) {
    const value = request.headers.get(name);
    if (value !== null) {
      headers.set(name, value);
    }
  }

  const hasBody = request.method !== "GET" && request.method !== "DELETE";
  const upstream = await fetch(target, {
    method: request.method,
    headers,
    body: hasBody ? await request.arrayBuffer() : undefined,
    cache: "no-store",
    redirect: "manual",
    signal: request.signal,
  });

  const responseHeaders = new Headers();
  for (const name of FORWARDED_RESPONSE_HEADERS) {
    const value = upstream.headers.get(name);
    if (value !== null) {
      responseHeaders.set(name, value);
    }
  }
  if (upstream.headers.get("content-type")?.startsWith("text/event-stream") === true) {
    // SignalR ServerSentEvents: stream unbuffered (body is passed through as a stream, never read here).
    responseHeaders.set("cache-control", "no-cache, no-transform");
    responseHeaders.set("x-accel-buffering", "no");
  }
  return new Response(upstream.body, { status: upstream.status, headers: responseHeaders });
}

export const GET = forward;
export const POST = forward;
export const PUT = forward;
export const PATCH = forward;
export const DELETE = forward;
