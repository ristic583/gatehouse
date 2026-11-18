import { expect, test, type Page } from "@playwright/test";
import { cloudPacsAssignments, cloudPacsUrl } from "../fixtures/cloudpacs";
import { pageFor, requestAndApprove } from "../fixtures/flows";
import { openFloor } from "../fixtures/map";
import { resetDemoAccess } from "../fixtures/reset";

const SITE = "Warehouse Aarhus";
const FLOOR = "Warehouse floor";

test.setTimeout(180_000);

async function openAarhusLive(page: Page): Promise<void> {
  await page.goto("/live");
  await page.getByRole("combobox", { name: "Site" }).selectOption({ label: SITE });
  await page.getByRole("combobox", { name: "Floor" }).selectOption({ label: FLOOR });
  await expect(page.getByTestId("live-status")).toHaveAttribute("data-status", "connected", { timeout: 20_000 });
}

// Own data: the spec creates its own request/approval (anna → Warehouse Office after a demo reset), asserts occupancy
// relative to the value it read, and the replay looks for the swipes it created.
test.describe.configure({ mode: "serial" });

let swipedFrom = 0;

test.afterAll(async ({ browser }) => {
  await resetDemoAccess(browser, "nordlys");
});

test("occupancy changes with in and out swipes", async ({ browser, request }) => {
  await resetDemoAccess(browser, "nordlys", "Connector");
  const anna = await requestAndApprove(browser, {
    site: SITE,
    floor: FLOOR,
    code: "AAR-OFFICE",
    areaName: "Warehouse Office",
    approvers: ["mikkel.holm"],
    justification: "Occupancy drill in the warehouse office",
  });
  await expect(async () => {
    await openFloor(anna, FLOOR, SITE);
    await expect(anna.getByTestId("area-AAR-OFFICE")).toHaveAttribute("data-state", "HasAccess", { timeout: 2_000 });
  }).toPass({ timeout: 90_000 });
  await anna.context().close();
  let annaRef: string | undefined;
  await expect
    .poll(async () => {
      const assignments = [...(await cloudPacsAssignments(request))].reverse();
      annaRef = assignments.find((a) => a.groupId === "AG-AAR-OFFICE" && a.active && a.holderName === "Anna Larsen")?.externalRef;
      return annaRef !== undefined;
    }, { timeout: 30_000 })
    .toBe(true);

  const jonas = await pageFor(browser, "jonas.nielsen");
  await openAarhusLive(jonas);
  const counter = jonas.getByTestId("occupancy-AAR-OFFICE");
  // Read the starting value only once the counter has loaded (a number is shown).
  await expect(counter).toContainText(/\d+/, { timeout: 20_000 });
  const before = Number((await counter.textContent())?.match(/\d+/)?.[0] ?? "0");
  const swipeAs = (direction: "In" | "Out") =>
    request.post(`${cloudPacsUrl()}/dev/swipes`, { data: { doorRef: "D-AAR-OFFICE", cardNumber: null, externalRef: annaRef, direction } });
  swipedFrom = Date.now();
  expect((await swipeAs("In")).ok()).toBe(true);
  await expect(counter).toContainText(`≈ ${before + 1} inside`, { timeout: 30_000 });
  expect((await swipeAs("Out")).ok()).toBe(true);
  await expect(counter).toContainText(`≈ ${before} inside`, { timeout: 30_000 });
  await jonas.context().close();
});

test("replay shows earlier door state", async ({ browser }) => {
  expect(swipedFrom, "the occupancy test created the swipes").toBeGreaterThan(0);
  const jonas = await pageFor(browser, "jonas.nielsen");
  await openAarhusLive(jonas);
  const replayEvents = jonas.waitForResponse((r) => /\/v1\/live\/floors\/[^/]+\/events/.test(r.url()));
  await jonas.getByRole("button", { name: "Replay" }).click();
  await expect(jonas.getByTestId("replay")).toBeVisible();
  type ReplayEvent = { occurredAt: string; areaName: string | null; personDisplayName: string | null };
  const events = (await (await replayEvents).json()) as ReplayEvent[];
  expect(
    events.some(
      (e) => e.areaName === "Warehouse Office" && e.personDisplayName === "Anna Larsen" && Date.parse(e.occurredAt) >= swipedFrom - 1_000,
    ),
    "the replay window contains the swipes this spec created",
  ).toBe(true);
  await expect(jonas.getByTestId("live-floor-plan")).toBeVisible({ timeout: 20_000 });
  const slider = jonas.getByLabel("Replay time");
  await slider.fill("0");
  await expect(jonas.getByTestId("replay-time")).not.toBeEmpty();
  await jonas.getByRole("button", { name: "Back to live" }).click();
  await expect(jonas.getByTestId("replay")).toHaveCount(0);
  await jonas.context().close();
});
