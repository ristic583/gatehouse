import { expect, test } from "@playwright/test";
import { pageFor } from "../fixtures/flows";
import { apiAs, e2eEmail, endSponsorships, inDays } from "../fixtures/lifecycle";

const created: string[] = [];

test.afterAll(async ({ browser }) => {
  await endSponsorships(await apiAs(browser, "mikkel.holm"), created);
});

test("sponsor onboards a visitor within the cap", async ({ browser }) => {
  const mikkel = await pageFor(browser, "mikkel.holm");
  const email = e2eEmail();
  await mikkel.goto("/people");
  await mikkel.getByRole("button", { name: "Add person" }).click();
  const dialog = mikkel.getByTestId("onboard-dialog");
  await dialog.getByLabel("Type").selectOption("Visitor");
  await dialog.getByLabel("Given name").fill("Vera");
  await dialog.getByLabel("Family name").fill("Visitor");
  await dialog.getByLabel("Email").fill(email);
  await dialog.getByLabel("Start").fill(inDays(0));
  await dialog.getByLabel("End").fill(inDays(30));
  await dialog.getByRole("button", { name: "Add" }).click();
  await expect(dialog.getByRole("alert")).toHaveText("Longer than the tenant allows for this type");

  await dialog.getByLabel("End").fill(inDays(3));
  const saved = mikkel.waitForResponse((r) => r.request().method() === "POST" && r.url().endsWith("/api/proxy/v1/people"));
  await dialog.getByRole("button", { name: "Add" }).click();
  const response = await saved;
  expect(response.status()).toBe(201);
  created.push(((await response.json()) as { summary: { personId: string } }).summary.personId);

  await expect(dialog).toBeHidden();
  await expect(mikkel.getByTestId("person-detail")).toContainText("Vera Visitor");
  await expect(mikkel.getByTestId("person-detail")).toContainText("Visitor");
  await expect(mikkel.getByTestId("person-detail")).toContainText("Mikkel Holm");
  await mikkel.context().close();
});

test("visitor without end date cannot be saved", async ({ browser }) => {
  const mikkel = await pageFor(browser, "mikkel.holm");
  let posted = false;
  mikkel.on("request", (r) => {
    if (r.method() === "POST" && r.url().endsWith("/api/proxy/v1/people")) {
      posted = true;
    }
  });
  await mikkel.goto("/people");
  await mikkel.getByRole("button", { name: "Add person" }).click();
  const dialog = mikkel.getByTestId("onboard-dialog");
  await dialog.getByLabel("Type").selectOption("Visitor");
  await dialog.getByLabel("Given name").fill("Nora");
  await dialog.getByLabel("Family name").fill("Noend");
  await dialog.getByLabel("Email").fill(e2eEmail());
  await dialog.getByLabel("Start").fill(inDays(0));
  await dialog.getByRole("button", { name: "Add" }).click();

  await expect(dialog).toBeVisible();
  expect(await dialog.getByLabel("End").evaluate((el) => (el as HTMLInputElement).validity.valueMissing)).toBe(true);
  expect(posted).toBe(false);
  await mikkel.context().close();
});
