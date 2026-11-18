import fs from "node:fs";
import { test as setup, type Browser } from "@playwright/test";
import { authUsers, emailOf, storageStateFor, type AuthUser } from "./fixtures/auth-state";
import { signIn, webUrl } from "./fixtures/sign-in";

// Each user signs in in its own browser context, so the sign-ins run in parallel (project `setup`).
setup.describe.configure({ mode: "parallel" });

for (const user of authUsers) {
  setup(`sign in ${user}`, async ({ browser, page }) => {
    if (await isStillSignedIn(browser, user)) {
      return; // the stored session still works against the running system (repeated local runs)
    }

    await signIn(page, emailOf(user));
    await page.context().storageState({ path: storageStateFor(user) });
  });
}

/** True when `.auth/{user}.json` exists and its BFF session is accepted (`GET /api/proxy/v1/me` → 200). */
async function isStillSignedIn(browser: Browser, user: AuthUser): Promise<boolean> {
  const path = storageStateFor(user);
  if (!fs.existsSync(path)) {
    return false;
  }

  const context = await browser.newContext({ storageState: path });
  try {
    const response = await context.request.get(`${webUrl()}/api/proxy/v1/me`, { maxRedirects: 0 });
    return response.status() === 200;
  } catch {
    return false;
  } finally {
    await context.close();
  }
}
