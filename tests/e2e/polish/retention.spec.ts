import { expect, test } from "@playwright/test";
import { pageFor } from "../fixtures/flows";
import { personIdOf } from "../fixtures/reconciliation";

test("admin exports a person", async ({ browser }) => {
  const annaId = await personIdOf(browser, "anna.larsen");
  const freja = await pageFor(browser, "freja.dahl");
  await freja.goto(`/people?person=${annaId}`);
  const download = freja.waitForEvent("download");
  await freja.getByTestId("person-export").click();
  const file = await download;
  expect(file.suggestedFilename()).toBe(`person-${annaId}.json`);
  const body = await (await file.createReadStream()).toArray();
  const json = Buffer.concat(body.map((b) => Buffer.from(b as Uint8Array))).toString("utf8");
  expect(json).toContain("anna.larsen@nordlys.test");
  expect(json).toContain("\"auditEntries\"");

  await freja.goto("/console?tab=lifecycle");
  await expect(freja.getByTestId("retention-setting")).toHaveCount(3);
  await freja.context().close();
});
