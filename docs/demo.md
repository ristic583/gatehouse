# Recording the demo

The 3-minute demo of spec §17 is a Playwright spec that drives the real UI: `tests/e2e/demo/demo.spec.ts`.

1. Start the system: `dotnet run --project src/Gatehouse.AppHost` (web on http://localhost:3000).
2. Export the demo password: `export GATEHOUSE_DEMO_PASSWORD=$(dotnet user-secrets list --project src/Gatehouse.AppHost | sed -n 's/^Parameters:demo-user-password = //p')`.
3. Record: `cd tests/e2e && GATEHOUSE_RECORD_DEMO=1 npx playwright test --project=demo --reporter=line`.

Playwright writes one `.webm` video per persona under `tests/e2e/test-results/demo-*/video-{user}/` (Anna, Mikkel, Sofie, Jonas, Lena; the spec records each browser context with `recordVideo`). The default `chromium` project ignores `demo/`, so `npx playwright test` does not record.

What the recording shows, in order: Anna requests the Server Room; Mikkel and Sofie approve; the HQ agent applies the change to LegacyPACS and the map turns green; a swipe at the Server Room reaches the live floor plan; the "How it works" view; a reconciliation of the CloudPACS warehouse; "Audit chain verified"; the dashboard; Bricks GmbH with its own theme.

Not automated: stopping the connector container (script steps 6-7). Do that by hand in the Aspire dashboard if you want it in a recording.
