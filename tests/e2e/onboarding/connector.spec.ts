import { expect, test } from "@playwright/test";
import { type StartedAgent, startAgent } from "../fixtures/agent";
import { siteWithFloor, tenantWithAdmin } from "./steps";

let agent: StartedAgent | undefined;
test.afterEach(async () => {
  await agent?.stop();
  agent = undefined;
});

test("admin registers a connector, the agent enrolls and shows online", async ({ browser }) => {
  test.setTimeout(240_000);
  const { admin } = await tenantWithAdmin(browser);
  const { siteName } = await siteWithFloor(admin, "PX");

  await admin.goto("/admin/pacs");
  const form = admin.getByRole("form", { name: "Create instance" });
  await form.getByLabel("Instance name").fill("Legacy PX");
  await form.getByLabel("Vendor").selectOption("LegacyPacs");
  await form.getByLabel(siteName).check();
  await form.getByRole("button", { name: "Create instance" }).click();
  await admin.getByRole("link", { name: "Legacy PX" }).click();

  await admin.getByRole("button", { name: "Register connector" }).click();
  const token = (await admin.getByTestId("enrollment-token").textContent())?.trim() ?? "";
  expect(token.length).toBeGreaterThan(10);
  await expect(admin.getByTestId("install-snippet")).toContainText("Agent__GatewayUrl=");
  await admin.getByLabel("I have stored it").check();
  await admin.getByRole("button", { name: "Close" }).click();
  await expect(admin.getByTestId("enrollment-token")).toHaveCount(0);
  await expect(admin.getByTestId("connector-status")).toHaveText("Pending enrollment");

  agent = startAgent({ token, vendor: "LegacyPacs" });
  await expect(admin.getByTestId("connector-status")).toHaveText("Active", { timeout: 120_000 });
  await expect(admin.getByTestId("connector-online")).toHaveText("Online", { timeout: 60_000 });
  await admin.context().close();
});
