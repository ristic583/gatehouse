import { expect, test } from "@playwright/test";
import { storageStateFor } from "../fixtures/auth-state";
import { webUrl } from "../fixtures/sign-in";

interface SiteDto {
  name: string;
  floors: { floorId: string; level: number }[];
}

test("bricks user sees only bricks sites", async ({ browser }) => {
  const context = await browser.newContext({ storageState: storageStateFor("lena.schmidt") });
  const page = await context.newPage();
  await page.goto("/map");

  const site = page.getByRole("combobox", { name: "Site" });
  await expect(site).toBeVisible();
  await expect(site.locator("option")).toHaveText(["Berlin Office"]);
  await context.close();
});

test("nordlys floor is not reachable for bricks user", async ({ browser }) => {
  const anna = await browser.newContext({ storageState: storageStateFor("anna.larsen") });
  const sites = (await (await anna.request.get(`${webUrl()}/api/proxy/v1/sites`)).json()) as SiteDto[];
  const floorId = sites.find((s) => s.name === "HQ Copenhagen")?.floors.find((f) => f.level === 2)?.floorId;
  expect(floorId).toBeDefined();
  await anna.close();

  const lena = await browser.newContext({ storageState: storageStateFor("lena.schmidt") });
  const response = await lena.request.get(`${webUrl()}/api/proxy/v1/floors/${floorId ?? ""}/access-map`);
  expect(response.status()).toBe(404);
  await lena.close();
});
