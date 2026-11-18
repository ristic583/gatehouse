import { expect, test } from "@playwright/test";
import { webUrl } from "../fixtures/sign-in";

// The AppHost turns on GATEHOUSE_DEMO_SIGN_IN, as the public demo does.
test.use({ storageState: { cookies: [], origins: [] } });

const signedIn = (url: URL) => !url.pathname.startsWith("/login") && !url.pathname.startsWith("/auth");

test("Demo sign-in: one click signs Anna in through the OIDC upstream", async ({ page }) => {
  await page.goto(`${webUrl()}/login`);
  await page.getByRole("button", { name: /Anna Larsen/ }).click();
  await page.waitForURL(signedIn, { timeout: 60_000 });
  await expect(page.getByRole("complementary").locator("strong", { hasText: "Nordlys A/S" })).toBeVisible();
});

test("Demo sign-in: one click signs Lena in through the SAML upstream", async ({ page }) => {
  await page.goto(`${webUrl()}/login`);
  await page.getByRole("button", { name: /Lena Schmidt/ }).click();
  await page.waitForURL(signedIn, { timeout: 60_000 });
  await expect(page.getByRole("complementary").locator("strong", { hasText: "Bricks GmbH" })).toBeVisible();
});
