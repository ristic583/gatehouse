"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import type { PlatformTenantSummaryDto } from "@/lib/api/types";
import { platformApi, platformKeys } from "../api";
import { TenantStatusChip } from "./status-chip";

export function TenantListView({ tenants }: { tenants: readonly PlatformTenantSummaryDto[] }) {
  if (tenants.length === 0) {
    return (
      <div className="gh-empty">
        <h2>No tenants yet</h2>
        <p>Create the first customer tenant to start onboarding.</p>
      </div>
    );
  }
  return (
    <div className="gh-table-wrap">
      <table className="w-full text-left">
        <thead>
          <tr>
            <th>Tenant</th>
            <th>Status</th>
            <th>Email domain</th>
            <th>Sign-in</th>
            <th>People</th>
            <th>Sites</th>
            <th>Connectors</th>
            <th>Alerts</th>
          </tr>
        </thead>
        <tbody>
          {tenants.map((tenant) => (
            <tr key={tenant.tenantId} data-testid="platform-tenant-row" data-tenant-id={tenant.tenantId}>
              <td>
                <Link href={`/platform/tenants/${encodeURIComponent(tenant.tenantId)}`} className="underline">
                  {tenant.displayName}
                </Link>
                <div>{tenant.tenantId}</div>
              </td>
              <td>
                <TenantStatusChip status={tenant.status} />
                {tenant.provisioningStep !== null && tenant.status !== "Active" && (
                  <div className="text-xs text-muted-foreground">Step: {tenant.provisioningStep}</div>
                )}
              </td>
              <td>{tenant.emailDomain ?? "-"}</td>
              <td>{tenant.idpKind}</td>
              <td>{tenant.people}</td>
              <td>{tenant.sites}</td>
              <td>
                {tenant.connectorsOnline} online · {tenant.connectorsOffline} offline
              </td>
              <td data-testid="tenant-alert-count">{tenant.alertCount}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function TenantList() {
  const query = useQuery({ queryKey: platformKeys.tenants(), queryFn: platformApi.tenants, refetchInterval: 30_000 });
  if (query.isPending) return <p className="gh-loading">Loading tenants…</p>;
  if (query.isError) return <p role="alert">Tenants could not be loaded.</p>;
  return <TenantListView tenants={query.data} />;
}
