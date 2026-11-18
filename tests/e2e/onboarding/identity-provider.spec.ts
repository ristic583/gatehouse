import { expect, test } from "@playwright/test";
import { createUpstreamUser } from "../fixtures/upstream-idp";
import { addEmployee, connectAcmeIdp, signInLocal, signInUpstream, tenantWithAdmin } from "./steps";

test("admin connects the acme OIDC IdP and an acme employee signs in through it", async ({ browser }) => {
  test.setTimeout(240_000);
  const { tenant, admin, password } = await tenantWithAdmin(browser);
  const employee = `emma@${tenant.emailDomain}`;
  await createUpstreamUser(employee, "Emma", "Employee");
  await addEmployee(admin, employee, "Emma", "Employee");

  await connectAcmeIdp(admin);
  await admin.goto("/onboarding");
  await expect(admin.getByTestId("onboarding-step-IdentityProvider")).toHaveAttribute("data-state", "Complete");

  const emma = await (await browser.newContext()).newPage();
  await signInUpstream(emma, employee);
  await expect(emma).toHaveURL(/\/map/);
  expect((await emma.request.get("/api/proxy/v1/me")).status()).toBe(200);
  await emma.context().close();

  // Open question (M10 hand-over): does redirect-on-email-match send the local backup admin to the tenant IdP?
  const backup = await (await browser.newContext()).newPage();
  await backup.goto("/login");
  await backup.getByLabel("Work email").fill(tenant.adminEmail);
  await backup.getByRole("button", { name: "Continue with email" }).click();
  await backup.waitForURL(/\/realms\//);
  const landed = new URL(backup.url());
  test.info().annotations.push({ type: "backup-admin-sign-in", description: `${landed.host}${landed.pathname}` });
  if (landed.host.endsWith(":8080")) {
    // Still on Gatehouse's realm: the local password keeps working.
    await signInLocal(backup, tenant.adminEmail, password);
  }
  await backup.context().close();
});
