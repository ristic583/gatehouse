import { expect, test } from "@playwright/test";
import { swipe } from "../fixtures/building";
import { pageFor } from "../fixtures/flows";

test("door lane pulses on a real swipe", async ({ browser }) => {
  const jonas = await pageFor(browser, "jonas.nielsen");
  await jonas.goto("/how-it-works");
  await expect(jonas.getByTestId("sign-in-note")).toContainText("never takes part in a door decision");
  await jonas.getByLabel("Live site").selectOption({ label: "HQ Copenhagen" });
  const pulse = jonas.getByTestId("lane-door-pulse");
  await expect(pulse).toHaveAttribute("data-count", "0");
  await expect(async () => {
    await swipe(jonas.request, "D-CPH-LOBBY", "Visitor");
    await expect(pulse).not.toHaveAttribute("data-count", "0", { timeout: 5_000 });
  }).toPass({ timeout: 45_000 });
  await jonas.context().close();
});
