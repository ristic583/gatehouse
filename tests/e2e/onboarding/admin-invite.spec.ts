import { expect, test } from "@playwright/test";
import { createTenant, signInPlatform } from "../fixtures/platform";
import { acceptInvite, adminPassword, signInLocal } from "./steps";

test("invited admin sets a password and lands on onboarding", async ({ page, browser }) => {
  test.setTimeout(180_000);
  await signInPlatform(page);
  const tenant = await createTenant(page.request);

  const admin = await (await browser.newContext()).newPage();
  const password = adminPassword();
  await acceptInvite(admin, tenant.adminEmail, password);
  await signInLocal(admin, tenant.adminEmail, password);

  await expect(admin).toHaveURL(/\/onboarding$/);
  await expect(admin.getByTestId("onboarding-welcome")).toContainText(tenant.displayName);
  await expect(admin.getByTestId("onboarding-step-IdentityProvider")).toHaveAttribute("data-state", "NotStarted");
  await expect(admin.getByRole("link", { name: "Onboarding" })).toContainText("left");
  await admin.context().close();
});
