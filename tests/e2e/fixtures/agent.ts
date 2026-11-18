import { type ChildProcess, spawn } from "node:child_process";
import { mkdtempSync } from "node:fs";
import os from "node:os";
import path from "node:path";

export interface StartedAgent {
  process: ChildProcess;
  dataDirectory: string;
  stop: () => Promise<void>;
}

const repoRoot = path.resolve(__dirname, "..", "..", "..");

/** LegacyPACS simulator database of the running AppHost (Aspire Postgres on 55432). */
export function legacyPacsConnectionString(): string {
  const explicit = process.env.GATEHOUSE_LEGACYPACS_CONNECTION_STRING;
  if (explicit !== undefined && explicit !== "") return explicit;
  const password = process.env.GATEHOUSE_POSTGRES_PASSWORD;
  if (password === undefined || password === "") throw new Error("Set GATEHOUSE_LEGACYPACS_CONNECTION_STRING or GATEHOUSE_POSTGRES_PASSWORD (Parameters:postgres-password).");
  return `Host=localhost;Port=55432;Database=legacypacs;Username=postgres;Password=${password}`;
}

/** Spawns `Gatehouse.Agent` (already built) with its own data directory; call `stop()` in teardown. */
export function startAgent(options: { token: string; vendor: "LegacyPacs" }): StartedAgent {
  const dataDirectory = mkdtempSync(path.join(os.tmpdir(), "gatehouse-e2e-agent-"));
  // The built apphost binary, not `dotnet run`: killing `dotnet run` leaves its child agent running (orphans kept reading
  // the shared LegacyPACS simulator database and disturbed later live/reconciliation specs).
  const binary = path.join(repoRoot, "src", "Gatehouse.Agent", "bin", "Debug", "net10.0", process.platform === "win32" ? "Gatehouse.Agent.exe" : "Gatehouse.Agent");
  const child = spawn(binary, [], {
    cwd: repoRoot,
    env: {
      ...process.env,
      DOTNET_ENVIRONMENT: "Development",
      Agent__GatewayUrl: process.env.GATEHOUSE_GATEWAY_URL ?? "https://localhost:5443",
      Agent__EnrollmentToken: options.token,
      Agent__Vendor: options.vendor,
      Agent__DataDirectory: dataDirectory,
      Agent__TrustGatewayCertificate: "dev",
      Agent__LegacyPacsConnectionString: legacyPacsConnectionString(),
    },
    stdio: "ignore",
  });
  return {
    process: child,
    dataDirectory,
    stop: () =>
      new Promise((resolve) => {
        if (child.exitCode !== null) return resolve();
        child.once("exit", () => resolve());
        child.kill("SIGTERM");
        setTimeout(() => child.kill("SIGKILL"), 5000).unref();
      }),
  };
}
