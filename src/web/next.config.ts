import type { NextConfig } from "next";

const isDev = process.env.NODE_ENV === "development";

/**
 * SEC-08: browser security headers on every response.
 *
 * CSP without a nonce on purpose: pages are prerendered static shells (`cacheComponents`, `partialPrefetching`), and a per-request
 * nonce would force every page to render dynamically. Next.js inlines its bootstrap scripts, so `script-src` needs 'unsafe-inline'
 * ('unsafe-eval' only in `next dev`). The policy still blocks scripts, frames, objects and connections from any other origin, and
 * `frame-ancestors 'none'` stops clickjacking. The browser only talks to this origin: API calls and the live hub (Server-Sent
 * Events / long polling) go through `/api/proxy`, photos are served from `/api/proxy`, 3D textures are canvas-generated
 * (data:/blob:). `form-action` is left open because the sign-in form redirects to the tenant's identity provider, whose host
 * differs per tenant and environment.
 */
const contentSecurityPolicy = [
  "default-src 'self'",
  `script-src 'self' 'unsafe-inline'${isDev ? " 'unsafe-eval'" : ""}`,
  "style-src 'self' 'unsafe-inline'",
  "img-src 'self' data: blob:",
  "font-src 'self' data:",
  `connect-src 'self'${isDev ? " ws: wss:" : ""}`,
  "worker-src 'self' blob:",
  "object-src 'none'",
  "base-uri 'self'",
  "frame-ancestors 'none'",
].join("; ");

const securityHeaders = [
  { key: "Content-Security-Policy", value: contentSecurityPolicy },
  { key: "X-Frame-Options", value: "DENY" },
  { key: "X-Content-Type-Options", value: "nosniff" },
  { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
  // Browsers ignore HSTS on plain http (RFC 6797 §8.1), so it is safe to send always; production is served over https only.
  ...(isDev ? [] : [{ key: "Strict-Transport-Security", value: "max-age=31536000" }]),
];

const nextConfig: NextConfig = {
  // Production image (deploy/Dockerfile.web) runs the standalone server.
  output: "standalone",
  cacheComponents: true,
  poweredByHeader: false,
  headers: async () => [{ source: "/:path*", headers: securityHeaders }],
  partialPrefetching: true,
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
};

export default nextConfig;
