import { expect, type APIRequestContext, type Page } from "@playwright/test";
import { webUrl } from "./sign-in";

export function buildingSimUrl(): string {
  return process.env.GATEHOUSE_BUILDING_SIM_URL ?? "http://localhost:5190";
}

export interface SwipeDecision {
  decision: string;
  eventId: number;
}

/** Swipes at a door on the building simulator (LegacyPACS decides). */
export async function swipe(request: APIRequestContext, doorRef: string, holderName?: string): Promise<SwipeDecision> {
  const response = await request.post(`${buildingSimUrl()}/swipes`, { data: { doorRef, holderName: holderName ?? null } });
  expect(response.ok(), `swipe ${doorRef}: ${response.status()}`).toBe(true);
  return (await response.json()) as SwipeDecision;
}

/** Starts a simulator scenario (`forced-door`, `held-open`, `fire-alarm`, `pacs-slow`). */
export async function scenario(request: APIRequestContext, name: string, doorRef?: string): Promise<void> {
  const response = await request.post(`${buildingSimUrl()}/scenarios/${name}`, { data: { doorRef: doorRef ?? null } });
  expect(response.ok(), `scenario ${name}: ${response.status()}`).toBe(true);
}

/** Ends the simulator scenarios that have a clear step (`fire-alarm` relocks every door, `pacs-slow`): a spec starts from normal doors. */
export async function clearScenarios(request: APIRequestContext): Promise<void> {
  for (const name of ["fire-alarm", "pacs-slow"]) {
    const response = await request.post(`${buildingSimUrl()}/scenarios/${name}/clear`);
    expect(response.ok(), `clear scenario ${name}: ${response.status()}`).toBe(true);
  }
}

export interface OpenAlarm {
  alarmId: string;
  doorName: string;
  kind: "Forced" | "HeldOpen";
  raisedAt: string;
}

async function openAlarms(page: Page): Promise<OpenAlarm[]> {
  const response = await page.request.get("/api/proxy/v1/live/alarms?status=open");
  expect(response.status(), "open alarms").toBe(200);
  return (await response.json()) as OpenAlarm[];
}

/** Acknowledges every open alarm of a door through the BFF (the page holds a security staff session). */
export async function acknowledgeOpenAlarms(page: Page, doorName: string, comment: string): Promise<void> {
  for (const alarm of (await openAlarms(page)).filter((a) => a.doorName === doorName)) {
    const response = await page.request.post(`/api/proxy/v1/live/alarms/${alarm.alarmId}/acknowledge`, {
      headers: { Origin: webUrl() },
      data: { comment },
    });
    expect(response.ok(), `acknowledge ${alarm.alarmId}: ${response.status()}`).toBe(true);
  }
}

/** The open alarm of a door and kind raised at or after `since` (epoch ms); waits until it exists. */
export async function waitForAlarm(page: Page, doorName: string, kind: OpenAlarm["kind"], since: number): Promise<OpenAlarm> {
  let found: OpenAlarm | undefined;
  await expect
    .poll(
      async () => {
        found = (await openAlarms(page)).find((a) => a.doorName === doorName && a.kind === kind && Date.parse(a.raisedAt) >= since - 1_000);
        return found !== undefined;
      },
      { timeout: 15_000, intervals: [500, 1_000] },
    )
    .toBe(true);
  return found!;
}
