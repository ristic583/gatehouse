import { expect, test, type Page } from "@playwright/test";
import { pageFor } from "../fixtures/flows";
import { personIdOf, resolveTask } from "../fixtures/reconciliation";
import { resetDemoAccess } from "../fixtures/reset";

test.setTimeout(120_000);

test.beforeAll(async ({ browser }) => {
  await resetDemoAccess(browser, "nordlys");
});

/** The sponsor or manager deactivates the person's card from the people page. */
async function deactivateCard(page: Page, personId: string): Promise<void> {
  await page.goto(`/people?person=${personId}`);
  const row = page.getByTestId("card-row").first();
  await expect(row).toContainText("Active");
  await row.getByRole("button", { name: "Deactivate" }).click();
  await row.getByLabel("Deactivation reason").fill("E2E card drill");
  await row.getByRole("button", { name: "Confirm deactivate" }).click();
  await expect(row).toContainText("Deactivated");
}

test("employee activates own card", async ({ browser }) => {
  const annaId = await personIdOf(browser, "anna.larsen");
  const mikkel = await pageFor(browser, "mikkel.holm");
  await deactivateCard(mikkel, annaId);

  const anna = await pageFor(browser, "anna.larsen");
  await anna.goto("/me");
  const card = anna.getByTestId("my-card");
  await expect(card).toContainText("Deactivated");
  await card.getByRole("button", { name: "Activate" }).click();
  await expect(card).toContainText("Active", { timeout: 15_000 });
  await expect(card.getByRole("button", { name: "Activate" })).toHaveCount(0);
  await Promise.all([anna.context().close(), mikkel.context().close()]);
});

test("external cannot activate own card; sponsor can", async ({ browser }) => {
  const oliverId = await personIdOf(browser, "oliver.kruse");
  const mikkel = await pageFor(browser, "mikkel.holm");
  await deactivateCard(mikkel, oliverId);

  const oliver = await pageFor(browser, "oliver.kruse");
  await oliver.goto("/me");
  await expect(oliver.getByTestId("my-card")).toContainText("Deactivated");
  await expect(oliver.getByText("Your sponsor or manager activates your card.")).toBeVisible();
  await expect(oliver.getByRole("button", { name: "Activate" })).toHaveCount(0);

  await mikkel.goto(`/people?person=${oliverId}`);
  const row = mikkel.getByTestId("card-row").first();
  await row.getByRole("button", { name: "Activate" }).click();
  await expect(async () => {
    await oliver.reload();
    await expect(oliver.getByTestId("my-card")).toContainText("Active", { timeout: 2_000 });
  }).toPass({ timeout: 20_000 });
  await Promise.all([oliver.context().close(), mikkel.context().close()]);
});

test("pin is masked, revealed only to the assigned staff and re-masks after 30 s", async ({ browser }) => {
  const pin = String(Math.floor(100_000 + Math.random() * 899_999));
  const anna = await pageFor(browser, "anna.larsen");
  await anna.goto("/me");
  const pinInput = anna.getByLabel("New PIN (4-8 digits)");
  await expect(pinInput).toHaveAttribute("type", "password");
  await pinInput.fill(pin);
  await anna.getByRole("button", { name: "Set PIN" }).click();
  await expect(anna.getByText("PIN saved.")).toBeVisible();
  await expect(anna.locator("body")).not.toContainText(pin);
  const requested = anna.waitForResponse((r) => r.request().method() === "POST" && r.url().endsWith("/v1/me/pin/reveal-requests"));
  await anna.getByRole("button", { name: "I forgot my PIN" }).click();
  const { taskId } = (await (await requested).json()) as { taskId: string };
  await expect(anna.getByText("Security staff will contact you.")).toBeVisible();

  const freja = await pageFor(browser, "freja.dahl");
  await freja.goto("/console?tab=tasks");
  await expect(freja.getByRole("button", { name: "Open", exact: true })).toBeVisible();
  await expect(freja.getByText("Loading…")).toHaveCount(0);
  await expect(freja.getByTestId("pin-reveal")).toHaveCount(0);

  const jonas = await pageFor(browser, "jonas.nielsen");
  await jonas.goto("/console?tab=tasks");
  const row = jonas.getByTestId(`task-${taskId}`);
  await expect(row.getByTestId("task-person")).toHaveText("Person: Anna Larsen");
  await expect(row.getByTestId("pin-reveal")).toHaveCount(0);
  await row.getByRole("button", { name: "Claim" }).click();
  await row.getByTestId("pin-reveal").click();
  await expect(row.getByTestId("pin-value")).toHaveText(pin);
  await expect(row.getByTestId("pin-value")).toHaveText("••••", { timeout: 35_000 });
  await expect(jonas.locator("body")).not.toContainText(pin);

  await resolveTask(jonas, taskId, "Confirmed manually");
  await Promise.all([anna.context().close(), freja.context().close(), jonas.context().close()]);
});
