"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { ApiError } from "@/lib/api/client";
import type { PlatformTenantDto, TenantStatisticsDto } from "@/lib/api/types";
import { platformApi, platformKeys } from "../api";
import { AlertListView } from "./alert-list";
import { TenantStatusChip } from "./status-chip";

export interface TenantDetailActions {
  onSuspend: (reason: string) => void;
  onReactivate: () => void;
  onRetry: () => void;
  onResendInvite: () => void;
  busy?: boolean;
}

const STAT_LABELS: [keyof TenantStatisticsDto, string][] = [
  ["people", "People"],
  ["employees", "Employees"],
  ["tenantAdmins", "Tenant admins"],
  ["linkedPeople", "Signed in"],
  ["sites", "Sites"],
  ["floors", "Floors"],
  ["areas", "Areas"],
  ["doors", "Doors"],
  ["pacsInstances", "PACS instances"],
  ["activeEntitlements", "Active entitlements"],
  ["pendingRequests", "Pending requests"],
  ["openAlarms", "Open alarms"],
  ["unconfirmedFulfillments", "Unconfirmed fulfillments"],
];

function SuspendDialog({ onConfirm, onCancel, busy }: { onConfirm: (reason: string) => void; onCancel: () => void; busy?: boolean }) {
  const [reason, setReason] = useState("");
  const [touched, setTouched] = useState(false);
  const trimmed = reason.trim();
  const invalid = trimmed.length === 0 || trimmed.length > 500;
  return (
    <div role="dialog" aria-modal="true" aria-labelledby="suspend-title" className="gh-panel flex flex-col gap-3">
      <h3 id="suspend-title" className="font-medium">
        Suspend tenant
      </h3>
      <p className="text-sm text-muted-foreground">
        Users and connectors of this tenant are refused until it is reactivated. Data is kept.
      </p>
      <Label htmlFor="suspend-reason">Reason</Label>
      <Textarea
        id="suspend-reason"
        value={reason}
        maxLength={500}
        onChange={(event) => setReason(event.target.value)}
        onBlur={() => setTouched(true)}
      />
      {touched && invalid && (
        <small role="alert" className="text-xs text-destructive">
          A reason is required (at most 500 characters).
        </small>
      )}
      <div className="flex gap-2">
        <Button
          type="button"
          variant="destructive"
          disabled={invalid || busy}
          onClick={() => {
            setTouched(true);
            if (!invalid) onConfirm(trimmed);
          }}
        >
          Suspend
        </Button>
        <Button type="button" variant="outline" onClick={onCancel}>
          Cancel
        </Button>
      </div>
    </div>
  );
}

export function TenantDetailView({
  tenant,
  onSuspend,
  onReactivate,
  onRetry,
  onResendInvite,
  busy,
}: { tenant: PlatformTenantDto } & TenantDetailActions) {
  const [suspending, setSuspending] = useState(false);
  const failed = tenant.status === "ProvisioningFailed" || tenant.provisioning?.state === "Failed";
  const inviteOpen = tenant.provisioning?.adminInvitedAt != null && tenant.provisioning.adminActivatedAt === null;

  return (
    <div className="flex flex-col gap-4">
      <section className="gh-panel flex flex-col gap-3" aria-label="Tenant">
        <div className="flex flex-wrap items-center gap-3">
          <h2 className="text-xl font-medium">{tenant.displayName}</h2>
          <TenantStatusChip status={tenant.status} />
          <span className="text-sm text-muted-foreground">
            {tenant.tenantId} · {tenant.emailDomain ?? "no domain"} · sign-in {tenant.idpKind}
          </span>
        </div>
        {tenant.status === "Suspended" && tenant.suspendedReason !== null && (
          <p className="text-sm" data-testid="tenant-suspended-reason">
            Suspended{tenant.suspendedAt !== null ? ` ${new Date(tenant.suspendedAt).toLocaleString()}` : ""}: {tenant.suspendedReason}
          </p>
        )}
        {tenant.provisioning !== null && (
          <p className="text-sm text-muted-foreground" data-testid="tenant-provisioning">
            Provisioning: {tenant.provisioning.step} ({tenant.provisioning.state}, attempt {tenant.provisioning.attempts})
            {tenant.provisioning.lastError !== null && ` - ${tenant.provisioning.lastError}`}
          </p>
        )}
        <div className="flex flex-wrap gap-2">
          {tenant.status === "Active" && !suspending && (
            <Button type="button" variant="outline" onClick={() => setSuspending(true)} disabled={busy}>
              Suspend…
            </Button>
          )}
          {tenant.status === "Suspended" && (
            <Button type="button" onClick={onReactivate} disabled={busy}>
              Reactivate
            </Button>
          )}
          {failed && (
            <Button type="button" onClick={onRetry} disabled={busy}>
              Retry provisioning
            </Button>
          )}
          {inviteOpen && (
            <Button type="button" variant="outline" onClick={onResendInvite} disabled={busy}>
              Resend invite
            </Button>
          )}
        </div>
        {suspending && (
          <SuspendDialog
            busy={busy}
            onCancel={() => setSuspending(false)}
            onConfirm={(reason) => {
              onSuspend(reason);
              setSuspending(false);
            }}
          />
        )}
      </section>

      <section className="gh-panel" aria-label="Statistics" data-testid="tenant-statistics">
        <h3 className="gh-panel-title">
          Statistics{tenant.statistics !== null && <span>computed {new Date(tenant.statistics.computedAt).toLocaleString()}</span>}
        </h3>
        {tenant.statistics === null ? (
          <p className="text-sm text-muted-foreground">Not computed yet.</p>
        ) : (
          <dl className="grid grid-cols-2 gap-2 text-sm md:grid-cols-4">
            {STAT_LABELS.map(([key, label]) => (
              <div key={key}>
                <dt className="text-muted-foreground">{label}</dt>
                <dd>{String(tenant.statistics?.[key] ?? 0)}</dd>
              </div>
            ))}
          </dl>
        )}
      </section>

      <section className="gh-panel" aria-label="Connectors" data-testid="tenant-connectors">
        <h3 className="gh-panel-title">Connectors</h3>
        {tenant.connectors.length === 0 ? (
          <p className="text-sm text-muted-foreground">No connectors registered.</p>
        ) : (
          <ul className="flex flex-col gap-1 text-sm">
            {tenant.connectors.map((connector) => (
              <li key={connector.connectorId}>
                {connector.displayName} · {connector.status}
                {connector.lastSeenAt !== null ? ` · last seen ${new Date(connector.lastSeenAt).toLocaleString()}` : " · never seen"}
                {connector.certificateNotAfter !== null && ` · cert until ${new Date(connector.certificateNotAfter).toLocaleDateString()}`}
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="gh-panel" aria-label="Alerts" data-testid="tenant-alerts">
        <h3 className="gh-panel-title">Alerts</h3>
        <AlertListView alerts={tenant.alerts} showTenant={false} />
      </section>
    </div>
  );
}

export function TenantDetail({ tenantId }: { tenantId: string }) {
  const queryClient = useQueryClient();
  const query = useQuery({
    queryKey: platformKeys.tenant(tenantId),
    queryFn: () => platformApi.tenant(tenantId),
    refetchInterval: (q) => (q.state.data?.status === "Provisioning" ? 2000 : 30_000),
  });

  const onDone = (message: string) => (tenant?: PlatformTenantDto) => {
    toast.success(message);
    if (tenant !== undefined) queryClient.setQueryData(platformKeys.tenant(tenantId), tenant);
    void queryClient.invalidateQueries({ queryKey: platformKeys.all });
  };
  const onError = (error: Error) => toast.error(error instanceof ApiError ? error.detail : "The action failed.");

  const suspend = useMutation({
    mutationFn: (reason: string) => platformApi.suspend(tenantId, reason),
    onSuccess: onDone("Tenant suspended"),
    onError,
  });
  const reactivate = useMutation({ mutationFn: () => platformApi.reactivate(tenantId), onSuccess: onDone("Tenant reactivated"), onError });
  const retry = useMutation({ mutationFn: () => platformApi.retry(tenantId), onSuccess: onDone("Provisioning restarted"), onError });
  const resend = useMutation({ mutationFn: () => platformApi.resendInvite(tenantId), onSuccess: () => onDone("Invite sent")(), onError });

  if (query.isPending) return <p className="gh-loading">Loading tenant…</p>;
  if (query.isError)
    return (
      <p role="alert">
        {query.error instanceof ApiError && query.error.status === 404 ? "Tenant not found." : "The tenant could not be loaded."}
      </p>
    );
  return (
    <TenantDetailView
      tenant={query.data}
      busy={suspend.isPending || reactivate.isPending || retry.isPending || resend.isPending}
      onSuspend={(reason) => suspend.mutate(reason)}
      onReactivate={() => reactivate.mutate()}
      onRetry={() => retry.mutate()}
      onResendInvite={() => resend.mutate()}
    />
  );
}
