"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import type { PlatformTenantDto, ProvisioningStep } from "@/lib/api/types";
import { platformApi, platformKeys } from "../api";
import { TenantStatusChip } from "./status-chip";

export const PROVISIONING_STEPS: readonly ProvisioningStep[] = [
  "Database",
  "Migrate",
  "Baseline",
  "FirstAdmin",
  "Organization",
  "AdminInvite",
  "Completed",
];
const STEP_TEXT: Record<ProvisioningStep, string> = {
  Database: "Create tenant database",
  Migrate: "Apply schema",
  Baseline: "Write baseline (security levels)",
  FirstAdmin: "Create first admin",
  Organization: "Create Keycloak organization",
  AdminInvite: "Invite first admin",
  Completed: "Ready",
};

export function ProvisioningProgress({ initial, pollMs }: { initial: PlatformTenantDto; pollMs: number }) {
  const query = useQuery({
    queryKey: platformKeys.tenant(initial.tenantId),
    queryFn: () => platformApi.tenant(initial.tenantId),
    initialData: initial,
    initialDataUpdatedAt: 0,
    refetchInterval: (q) => {
      const status = q.state.data?.status;
      return status === "Active" || status === "ProvisioningFailed" ? false : pollMs;
    },
  });
  const tenant = query.data;
  const current = tenant.status === "Active" ? "Completed" : (tenant.provisioning?.step ?? tenant.provisioningStep ?? "Database");
  const currentIndex = PROVISIONING_STEPS.indexOf(current);
  const failed = tenant.status === "ProvisioningFailed";

  return (
    <section className="gh-panel flex flex-col gap-3" aria-label="Provisioning progress" data-testid="provisioning-progress">
      <div className="flex items-center gap-3">
        <h2 className="text-lg font-medium">{tenant.displayName}</h2>
        <TenantStatusChip status={tenant.status} />
      </div>
      <ol className="flex flex-col gap-1 text-sm">
        {PROVISIONING_STEPS.map((step, i) => {
          const state =
            tenant.status === "Active" || i < currentIndex ? "done" : i === currentIndex ? (failed ? "failed" : "current") : "pending";
          return (
            <li
              key={step}
              data-testid={`provisioning-step-${step}`}
              data-state={state}
              aria-current={state === "current" ? "step" : undefined}
            >
              {state === "done" ? "✓" : state === "failed" ? "✕" : state === "current" ? "…" : "○"} {STEP_TEXT[step]}
            </li>
          );
        })}
      </ol>
      {failed && tenant.provisioning?.lastError && (
        <p role="alert" className="text-sm text-destructive">
          {tenant.provisioning.lastError}
        </p>
      )}
      {tenant.status === "Active" ? (
        <p data-testid="provisioning-done">The tenant is active and the first admin has been invited.</p>
      ) : null}
      <Link className="gh-primary-link" href={`/platform/tenants/${encodeURIComponent(tenant.tenantId)}`}>
        Open tenant
      </Link>
    </section>
  );
}
