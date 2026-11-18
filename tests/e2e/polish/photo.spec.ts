import { expect, test } from "@playwright/test";
import { pageFor } from "../fixtures/flows";
import { personIdOf } from "../fixtures/reconciliation";

// 2x2 red PNG.
const PNG = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVR4nGP8z8DAwMDAxMDAwMDAAAANHQEDasKb6QAAAABJRU5ErkJggg==", "base64");

test("employee uploads a photo and the manager sees it", async ({ browser }) => {
  const anna = await pageFor(browser, "anna.larsen");
  await anna.goto("/me");
  const uploaded = anna.waitForResponse((r) => r.request().method() === "PUT" && r.url().endsWith("/v1/me/photo"));
  await anna.getByLabel("Photo file").setInputFiles({ name: "me.png", mimeType: "image/png", buffer: PNG });
  expect((await uploaded).status()).toBe(204);
  await expect(anna.getByText("Photo saved.")).toBeVisible();

  const annaId = await personIdOf(browser, "anna.larsen");
  const mikkel = await pageFor(browser, "mikkel.holm");
  await mikkel.goto(`/people?person=${annaId}`);
  const photo = mikkel.getByTestId("person-photo");
  await expect(photo).toBeVisible();
  await expect.poll(() => photo.evaluate((img) => (img as HTMLImageElement).naturalWidth)).toBeGreaterThan(0);

  const sofie = await pageFor(browser, "sofie.berg");
  const stranger = await sofie.request.get(`/api/proxy/v1/people/${annaId}/photo`);
  expect(stranger.status()).toBe(404);

  await anna.getByRole("button", { name: "Remove photo" }).click();
  await expect(anna.getByText("No photo yet.")).toBeVisible();
  await Promise.all([anna.context().close(), mikkel.context().close(), sofie.context().close()]);
});
