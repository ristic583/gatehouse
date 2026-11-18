import { randomUUID } from "node:crypto";
import { expect, type APIRequestContext, type Browser, type Page } from "@playwright/test";
import { storageStateFor, type AuthUser } from "./auth-state";
import { inDays } from "./flows";
import { webUrl } from "./sign-in";

export interface OnboardInput {
  type: "External" | "Visitor" | "Guest";
  givenName?: string;
  familyName?: string;
  days: number;
  cardNumber?: string;
}

export interface OnboardedPerson {
  personId: string;
  email: string;
  displayName: string;
}

export function e2eEmail(): string {
  return `e2e-${randomUUID()}@nordlys.test`;
}

export function uniqueCardNumber(): string {
  return `77${Date.now().toString().slice(-10)}${Math.floor(Math.random() * 90 + 10)}`;
}

const origin = () => ({ Origin: webUrl() });

/** A BFF request context signed in as a demo user (close it when done). */
export async function apiAs(browser: Browser, user: AuthUser): Promise<APIRequestContext> {
  const context = await browser.newContext({ storageState: storageStateFor(user) });
  return context.request;
}

/** Onboards a local person through `POST /v1/people` as the signed-in sponsor. */
export async function onboard(request: APIRequestContext, input: OnboardInput): Promise<OnboardedPerson> {
  const email = e2eEmail();
  const givenName = input.givenName ?? "E2E";
  const familyName = input.familyName ?? input.type;
  const start = new Date();
  start.setHours(0, 0, 0, 0);
  const end = new Date(start);
  end.setDate(end.getDate() + input.days);
  const response = await request.post(`${webUrl()}/api/proxy/v1/people`, {
    headers: origin(),
    data: {
      type: input.type,
      givenName,
      familyName,
      email,
      start: start.toISOString(),
      end: end.toISOString(),
      timeZoneId: "Europe/Copenhagen",
      sponsorId: null,
      cardNumber: input.cardNumber ?? null,
    },
  });
  expect(response.status(), `onboard: ${await response.text()}`).toBe(201);
  const body = (await response.json()) as { summary: { personId: string; displayName: string } };
  return { personId: body.summary.personId, email, displayName: body.summary.displayName };
}

/** Issues a one-time magic link for a guest; returns its URL. */
export async function issueMagicLink(request: APIRequestContext, personId: string): Promise<string> {
  const response = await request.post(`${webUrl()}/api/proxy/v1/people/${personId}/magic-links`, { headers: origin() });
  expect(response.status(), `magic link: ${await response.text()}`).toBe(201);
  return ((await response.json()) as { url: string }).url;
}

/** Runs the lifecycle job now (tenant admin session). */
export async function runLifecycle(request: APIRequestContext): Promise<{ terminated: number; expired: number; renewalsRequested: number }> {
  const response = await request.post(`${webUrl()}/api/proxy/v1/admin/lifecycle/run`, { headers: origin() });
  expect(response.status()).toBe(200);
  return (await response.json()) as { terminated: number; expired: number; renewalsRequested: number };
}

/** Ends the sponsorship of the given people; ignores people already terminated. */
export async function endSponsorships(request: APIRequestContext, personIds: readonly string[]): Promise<void> {
  for (const id of personIds) {
    const response = await request.post(`${webUrl()}/api/proxy/v1/people/${id}/sponsorship/end`, { headers: origin() });
    expect([200, 404, 409]).toContain(response.status());
  }
}

/** Reloads the person page until the card row shows the expected state (and optional PACS status). */
export async function waitForCardState(page: Page, personId: string, state: string, pacsStatus?: string, timeout = 60_000): Promise<void> {
  await expect(async () => {
    await page.goto(`/people?person=${personId}`);
    const row = page.getByTestId("card-row").first();
    await expect(row).toContainText(state, { timeout: 2_000 });
    if (pacsStatus !== undefined) {
      await expect(row).toContainText(pacsStatus, { timeout: 2_000 });
    }
  }).toPass({ timeout });
}

/** The card id of a person's first card through the people API. */
export async function cardOf(request: APIRequestContext, personId: string): Promise<{ cardId: string; state: string; pacsStatus: string | null }> {
  const response = await request.get(`${webUrl()}/api/proxy/v1/people/${personId}`);
  expect(response.ok()).toBe(true);
  const body = (await response.json()) as { cards: { cardId: string; state: string; pacsStatus: string | null }[] };
  expect(body.cards.length).toBeGreaterThan(0);
  return body.cards[0]!;
}

/** The area id of an area code on a site, through the sites and access map APIs. */
export async function areaIdOf(request: APIRequestContext, siteCode: string, areaCode: string): Promise<string> {
  const sites = (await (await request.get(`${webUrl()}/api/proxy/v1/sites`)).json()) as { code: string; floors: { floorId: string }[] }[];
  const site = sites.find((s) => s.code === siteCode);
  expect(site, `site ${siteCode}`).toBeDefined();
  for (const floor of site!.floors) {
    const map = (await (await request.get(`${webUrl()}/api/proxy/v1/floors/${floor.floorId}/access-map`)).json()) as { areas: { areaId: string; code: string }[] };
    const area = map.areas.find((a) => a.code === areaCode);
    if (area !== undefined) {
      return area.areaId;
    }
  }
  throw new Error(`Area ${areaCode} not found on ${siteCode}`);
}

export { inDays };
