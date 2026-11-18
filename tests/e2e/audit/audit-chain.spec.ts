import { expect, test } from "@playwright/test";
import { pageFor } from "../fixtures/flows";

test("audit chain verifies", async ({ browser }) => {
  const freja = await pageFor(browser, "freja.dahl");
  await freja.goto("/console?tab=audit");
  await freja.getByRole("button", { name: "Verify audit chain" }).click();
  await expect(freja.getByTestId("audit-status")).toContainText("Audit chain verified ✓");
  await freja.context().close();

  const anna = await pageFor(browser, "anna.larsen");
  await anna.goto("/requests");
  const response = await anna.request.get("/api/proxy/v1/audit/verify");
  expect(response.status()).toBe(403);
  await anna.context().close();
});
