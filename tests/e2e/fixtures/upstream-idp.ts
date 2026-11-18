import { expect, request as playwrightRequest } from "@playwright/test";
import { demoPassword } from "./demo-users";

export const upstreamIdpUrl = () => (process.env.GATEHOUSE_UPSTREAM_IDP_URL ?? "http://localhost:8081").replace(/\/+$/, "");
export const ACME_REALM = "acme-oidc";
export const acmeDiscoveryUrl = () => `${upstreamIdpUrl()}/realms/${ACME_REALM}/.well-known/openid-configuration`;
export const ACME_CLIENT_ID = "gatehouse-broker-acme";

export function acmeClientSecret(): string {
  const value = process.env.GATEHOUSE_ACME_BROKER_CLIENT_SECRET;
  if (value === undefined || value === "") throw new Error("GATEHOUSE_ACME_BROKER_CLIENT_SECRET is not set (Parameters:acme-broker-client-secret).");
  return value;
}

function adminPassword(): string {
  const value = process.env.GATEHOUSE_UPSTREAM_IDP_ADMIN_PASSWORD;
  if (value === undefined || value === "") throw new Error("GATEHOUSE_UPSTREAM_IDP_ADMIN_PASSWORD is not set (Parameters:upstream-idp-password).");
  return value;
}

/** Creates a user in realm `acme-oidc` (username = email, password = the demo password) via the upstream admin API. */
export async function createUpstreamUser(email: string, givenName: string, familyName: string): Promise<void> {
  const api = await playwrightRequest.newContext({ baseURL: upstreamIdpUrl() });
  try {
    const token = await api.post("/realms/master/protocol/openid-connect/token", {
      form: { grant_type: "password", client_id: "admin-cli", username: process.env.GATEHOUSE_UPSTREAM_IDP_ADMIN_USER ?? "admin", password: adminPassword() },
    });
    expect(token.ok(), `upstream admin token: ${token.status()}`).toBe(true);
    const accessToken = ((await token.json()) as { access_token: string }).access_token;
    const created = await api.post(`/admin/realms/${ACME_REALM}/users`, {
      headers: { Authorization: `Bearer ${accessToken}` },
      data: {
        username: email,
        email,
        emailVerified: true,
        enabled: true,
        firstName: givenName,
        lastName: familyName,
        credentials: [{ type: "password", value: demoPassword(), temporary: false }],
      },
    });
    expect([201, 409], `create upstream user ${email}: ${created.status()}`).toContain(created.status());
  } finally {
    await api.dispose();
  }
}
