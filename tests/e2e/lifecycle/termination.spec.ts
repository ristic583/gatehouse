import { expect, test } from "@playwright/test";
import { cloudPacsAssignments } from "../fixtures/cloudpacs";
import { pageFor } from "../fixtures/flows";
import { apiAs, areaIdOf, cardOf, endSponsorships, onboard, uniqueCardNumber, waitForCardState } from "../fixtures/lifecycle";
import { resetDemoAccess } from "../fixtures/reset";
import { webUrl } from "../fixtures/sign-in";

test.setTimeout(240_000);

const created: string[] = [];

test.afterAll(async ({ browser }) => {
  await endSponsorships(await apiAs(browser, "mikkel.holm"), created);
  await resetDemoAccess(browser, "nordlys");
});

test("ending sponsorship revokes access and cards, tracked to confirmation", async ({ browser, request }) => {
  await resetDemoAccess(browser, "nordlys", "Connector");
  const mikkel = await apiAs(browser, "mikkel.holm");
  const erik = await apiAs(browser, "erik.moller");
  const external = await onboard(mikkel, { type: "External", givenName: "Tess", familyName: "Termination", days: 60, cardNumber: uniqueCardNumber() });
  created.push(external.personId);

  const areaId = await areaIdOf(mikkel, "AAR-WH", "AAR-DOCK");
  const start = new Date();
  const end = new Date(start.getTime() + 14 * 24 * 3600 * 1000);
  const requested = await mikkel.post(`${webUrl()}/api/proxy/v1/access-requests`, {
    headers: { Origin: webUrl() },
    data: { personId: external.personId, areaId, requestedStart: start.toISOString(), requestedEnd: end.toISOString(), justification: "E2E termination drill at the dock" },
  });
  expect(requested.status(), await requested.text()).toBe(201);
  const { entitlementId } = (await requested.json()) as { entitlementId: string };

  await expect(async () => {
    const inbox = (await (await erik.get(`${webUrl()}/api/proxy/v1/approvals/inbox`)).json()) as { entitlementId: string; stepId: string }[];
    const item = inbox.find((i) => i.entitlementId === entitlementId);
    expect(item).toBeDefined();
    const approved = await erik.post(`${webUrl()}/api/proxy/v1/approvals/${item!.stepId}/approve`, { headers: { Origin: webUrl() }, data: {} });
    expect(approved.ok(), await approved.text()).toBe(true);
  }).toPass({ timeout: 20_000 });

  await expect(async () => {
    const assignments = await cloudPacsAssignments(request);
    expect(assignments.some((a) => a.externalRef === external.personId && a.active)).toBe(true);
  }).toPass({ timeout: 90_000 });

  const card = await cardOf(mikkel, external.personId);
  const activated = await mikkel.post(`${webUrl()}/api/proxy/v1/cards/${card.cardId}/activate`, { headers: { Origin: webUrl() } });
  expect(activated.status(), await activated.text()).toBe(202);

  const page = await pageFor(browser, "mikkel.holm");
  await waitForCardState(page, external.personId, "Active", "PACS: Confirmed", 90_000);

  await page.goto(`/people?person=${external.personId}`);
  const detail = page.getByTestId("person-detail");
  await detail.getByRole("button", { name: "End sponsorship" }).click();
  await page.getByRole("alertdialog", { name: "End sponsorship" }).getByRole("button", { name: "Yes, end it" }).click();
  await expect(detail).toContainText("Terminated", { timeout: 15_000 });

  await expect(async () => {
    await page.goto(`/people?person=${external.personId}`);
    await expect(page.getByTestId("person-entitlement").filter({ hasText: "Loading Dock" })).toContainText("revoke: Confirmed", { timeout: 2_000 });
  }).toPass({ timeout: 90_000 });
  await waitForCardState(page, external.personId, "Revoked", "PACS: Confirmed", 90_000);
  await expect(async () => {
    const assignments = await cloudPacsAssignments(request);
    expect(assignments.some((a) => a.externalRef === external.personId && a.active)).toBe(false);
  }).toPass({ timeout: 30_000 });
  await page.context().close();
});
