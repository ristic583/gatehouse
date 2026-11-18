import { expect, test } from "@playwright/test";
import { type StartedAgent, startAgent } from "../fixtures/agent";
import { inDays } from "../fixtures/flows";
import { createUpstreamUser } from "../fixtures/upstream-idp";
import { connectAcmeIdp, drawAreaWithDoor, signInUpstream, siteWithFloor, tenantWithAdmin, TINY_PNG } from "./steps";

let agent: StartedAgent | undefined;
test.afterEach(async () => {
  await agent?.stop();
  agent = undefined;
});

test("a new tenant completes guided onboarding end to end", async ({ browser }) => {
  test.setTimeout(600_000);
  // Invite → admin password → onboarding.
  const { tenant, admin } = await tenantWithAdmin(browser);
  await expect(admin).toHaveURL(/\/onboarding$/);
  const owner = { email: `olivia@${tenant.emailDomain}`, given: "Olivia", family: "Owner" };
  const requester = { email: `rasmus@${tenant.emailDomain}`, given: "Rasmus", family: "Requester" };
  const staff = { email: `sam@${tenant.emailDomain}`, given: "Sam", family: "Staff" };
  for (const u of [owner, requester, staff]) await createUpstreamUser(u.email, u.given, u.family);

  // Identity provider.
  await connectAcmeIdp(admin);

  // Employees via CSV (dry run first).
  await admin.goto("/admin/people");
  const csv = ["email,given_name,family_name,manager_email", `${owner.email},${owner.given},${owner.family},`, `${requester.email},${requester.given},${requester.family},${owner.email}`, `${staff.email},${staff.given},${staff.family},`].join("\n");
  // Re-select until React has hydrated the input (a change event before hydration is lost and Preview stays disabled).
  const preview = admin.getByRole("button", { name: "Preview", exact: true });
  await expect(async () => {
    await admin.getByLabel("CSV file").setInputFiles({ name: "people.csv", mimeType: "text/csv", buffer: Buffer.from(csv) });
    await expect(preview).toBeEnabled({ timeout: 2_000 });
  }).toPass({ timeout: 30_000 });
  await preview.click();
  await expect(admin.getByTestId("import-preview")).toContainText("3 new");
  await expect(admin.getByTestId("import-error")).toHaveCount(0);
  await admin.getByRole("button", { name: "Import" }).click();
  await expect(admin.getByTestId("import-result")).toContainText("3 new");

  // Site, drawing, floor plan with an owned area and a door.
  const { siteName, floorName } = await siteWithFloor(admin, "ONB");
  await admin.getByTestId("floor-background").setInputFiles({ name: "plan.png", mimeType: "image/png", buffer: TINY_PNG });
  await expect(admin.getByTestId("editor-background")).toBeVisible({ timeout: 30_000 });
  await drawAreaWithDoor(admin, { name: "Lab", code: "ONB-LAB", level: "Medium", ownerQuery: owner.given, ownerName: `${owner.given} ${owner.family}`, doorRef: `D-ONB-${Date.now()}` });

  // PACS with every area mapped.
  await admin.goto("/admin/pacs");
  const form = admin.getByRole("form", { name: "Create instance" });
  await form.getByLabel("Instance name").fill("Legacy ONB");
  await form.getByLabel("Vendor").selectOption("LegacyPacs");
  await form.getByLabel(siteName).check();
  await form.getByRole("button", { name: "Create instance" }).click();
  await admin.getByRole("link", { name: "Legacy ONB" }).click();
  await admin.getByLabel("Access level for Lab").fill("AL-ONB-LAB");
  await admin.getByRole("button", { name: "Save mappings" }).click();
  await expect(admin.getByTestId("unmapped-count")).toHaveText("0 unmapped");

  // Connector enrolled and online.
  await admin.getByRole("button", { name: "Register connector" }).click();
  const token = (await admin.getByTestId("enrollment-token").textContent())?.trim() ?? "";
  await admin.getByLabel("I have stored it").check();
  await admin.getByRole("button", { name: "Close" }).click();
  agent = startAgent({ token, vendor: "LegacyPacs" });
  await expect(admin.getByTestId("connector-online")).toHaveText("Online", { timeout: 120_000 });

  // Security staff.
  await admin.goto("/admin/people");
  await admin.getByRole("button", { name: "Assign role" }).click();
  const assign = admin.getByTestId("assign-role-dialog");
  await assign.getByLabel("Find employee").fill(staff.given);
  await assign.getByRole("button", { name: "Search" }).click();
  await assign.getByRole("button", { name: new RegExp(`${staff.given} ${staff.family}`) }).click();
  await assign.getByLabel("Role").selectOption("SecurityStaff");
  await assign.getByRole("button", { name: "Assign" }).click();
  await expect(admin.getByTestId("role-row").filter({ hasText: `${staff.given} ${staff.family}` })).toBeVisible();

  // Every step is complete on the server; finish.
  await admin.goto("/onboarding");
  for (const step of ["IdentityProvider", "Sites", "People", "Pacs", "Connector", "Staff"]) {
    await expect(admin.getByTestId(`onboarding-step-${step}`)).toHaveAttribute("data-state", "Complete", { timeout: 30_000 });
  }
  await admin.getByRole("button", { name: "Finish onboarding" }).click();
  await expect(admin).toHaveURL(/\/dashboard/, { timeout: 30_000 });

  // An imported employee requests access to the new area; the owner approves.
  const rasmus = await (await browser.newContext()).newPage();
  await signInUpstream(rasmus, requester.email);
  await rasmus.goto("/map");
  const site = rasmus.getByRole("combobox", { name: "Site" });
  if (await site.isVisible().catch(() => false)) await site.selectOption({ label: siteName });
  await rasmus.getByRole("combobox", { name: "Floor" }).selectOption({ label: floorName });
  await rasmus.getByRole("button", { name: "Floor plan", exact: true }).click();
  await rasmus.getByTestId("area-ONB-LAB").click();
  await rasmus.locator(".gh-area-detail").getByRole("button", { name: "Request access" }).click();
  const dialog = rasmus.getByRole("dialog");
  await dialog.getByRole("button", { name: "Continue", exact: true }).click();
  await dialog.getByLabel("End date").fill(inDays(14));
  await dialog.getByLabel("Justification").fill("Onboarding e2e: lab work");
  await dialog.getByRole("button", { name: "Review request" }).click();
  await dialog.getByRole("button", { name: "Send request" }).click();
  await expect(dialog).toBeHidden();

  const olivia = await (await browser.newContext()).newPage();
  await signInUpstream(olivia, owner.email);
  await olivia.goto("/approvals");
  const card = olivia.getByRole("article", { name: `${requester.given} ${requester.family} → Lab` });
  await card.getByRole("button", { name: "Approve" }).click();
  await olivia.getByRole("dialog").getByRole("button", { name: "Confirm approval" }).click();
  await expect(card).toHaveCount(0);

  await rasmus.goto("/requests");
  await expect(rasmus.locator("body")).toContainText("Lab");
  await Promise.all([rasmus.context().close(), olivia.context().close(), admin.context().close()]);
});
