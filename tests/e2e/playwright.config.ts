import { defineConfig, devices, type Project } from "@playwright/test";

// Records the 3-minute demo (spec §17) as video: `GATEHOUSE_RECORD_DEMO=1 npx playwright test --project=demo`.
// Opt-in only: it resets the shared nordlys demo data, so it must never run next to the chromium specs.
const demoProject: Project = {
  name: "demo",
  testMatch: "demo/**/*.spec.ts",
  dependencies: ["setup"],
  workers: 1,
  use: { ...devices["Desktop Chrome"], viewport: { width: 1280, height: 800 } },
};

// README screenshots into docs/images: `GATEHOUSE_SCREENSHOTS=1 npx playwright test --project=screenshots`. Signs in with the
// one-click demo personas, so the web app must run with GATEHOUSE_DEMO_SIGN_IN (the AppHost does).
const screenshotsProject: Project = {
  name: "screenshots",
  testMatch: "screenshots/**/*.spec.ts",
  workers: 1,
  use: { ...devices["Desktop Chrome"], viewport: { width: 1440, height: 900 }, deviceScaleFactor: 2 },
};

export default defineConfig({
  testDir: ".",
  timeout: 60_000,
  retries: 0,
  // Only the sign-in setup runs in parallel. The specs share one running demo system and reset tenant-wide demo data
  // (resetDemoAccess, simulator scenarios such as fire-alarm), so they stay on one worker.
  workers: 4,
  reporter: [["list"], ["html", { open: "never" }]],
  use: {
    baseURL: process.env.GATEHOUSE_WEB_URL ?? "http://localhost:3000",
    trace: "on-first-retry",
  },
  projects: [
    { name: "warmup", testMatch: "warmup.setup.ts" },
    { name: "setup", testMatch: "auth.setup.ts", dependencies: ["warmup"] },
    { name: "chromium", testMatch: "**/*.spec.ts", testIgnore: ["demo/**", "screenshots/**"], dependencies: ["setup"], workers: 1, use: { ...devices["Desktop Chrome"] } },
    ...(process.env.GATEHOUSE_RECORD_DEMO === "1" ? [demoProject] : []),
    ...(process.env.GATEHOUSE_SCREENSHOTS === "1" ? [screenshotsProject] : []),
  ],
});
