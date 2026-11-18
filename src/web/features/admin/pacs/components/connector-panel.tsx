"use client";

import { useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { CapabilityGapList } from "@/features/connectors/components/capability-gap-list";
import type { ConnectorEnrollmentDto, PacsInstanceAdminDto } from "@/lib/api/types";
import { EnrollmentDialog } from "./connector-enrollment";

const STATUS_TEXT = { PendingEnrollment: "Pending enrollment", Active: "Active", Revoked: "Revoked" } as const;

export interface ConnectorPanelProps {
  instance: Pick<PacsInstanceAdminDto, "connector" | "health" | "fulfillmentMode">;
  onRegister: () => Promise<ConnectorEnrollmentDto>;
  onRevoke: () => Promise<unknown>;
}

export function ConnectorPanel({ instance, onRegister, onRevoke }: ConnectorPanelProps) {
  const [enrollment, setEnrollment] = useState<ConnectorEnrollmentDto | null>(null);
  const [confirmRevoke, setConfirmRevoke] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const connector = instance.connector;
  const health = instance.health;
  const canRegister = connector === null || connector.status === "Revoked";

  async function run(action: () => Promise<void>) {
    setBusy(true);
    setError(null);
    try {
      await action();
    } catch (e) {
      setError(e instanceof Error ? e.message : "The action failed.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="gh-panel flex flex-col gap-3" aria-label="Connector" data-testid="connector-panel">
      <h3 className="gh-panel-title">Connector</h3>
      {connector === null ? (
        <p className="text-sm text-muted-foreground">No connector registered. Without one, changes are fulfilled manually.</p>
      ) : (
        <div className="flex flex-col gap-1 text-sm">
          <div className="flex items-center gap-2">
            <Badge
              variant={connector.status === "Active" ? "default" : connector.status === "Revoked" ? "destructive" : "secondary"}
              data-testid="connector-status"
            >
              {STATUS_TEXT[connector.status]}
            </Badge>
            {health !== null && connector.status === "Active" && (
              <Badge variant={health.online ? "default" : "destructive"} data-testid="connector-online">
                {health.online ? "Online" : "Offline"}
              </Badge>
            )}
          </div>
          {connector.status === "PendingEnrollment" && connector.enrollmentTokenExpiresAt !== null && (
            <span>Token expires {new Date(connector.enrollmentTokenExpiresAt).toLocaleString()}</span>
          )}
          {health?.agentVersion != null && <span>Agent {health.agentVersion}</span>}
          {connector.lastSeenAt !== null && <span>Last seen {new Date(connector.lastSeenAt).toLocaleString()}</span>}
          {connector.certificateNotAfter !== null && (
            <span>Certificate valid until {new Date(connector.certificateNotAfter).toLocaleDateString()}</span>
          )}
          <CapabilityGapList gaps={health?.capabilityGaps} />
        </div>
      )}
      {enrollment !== null && <EnrollmentDialog enrollment={enrollment} onClose={() => setEnrollment(null)} />}
      {error !== null && (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        {canRegister && enrollment === null && (
          <Button type="button" disabled={busy} onClick={() => void run(async () => setEnrollment(await onRegister()))}>
            Register connector
          </Button>
        )}
        {connector !== null && connector.status !== "Revoked" && !confirmRevoke && (
          <Button type="button" variant="outline" disabled={busy} onClick={() => setConfirmRevoke(true)}>
            Revoke
          </Button>
        )}
      </div>
      {confirmRevoke && (
        <div role="alertdialog" aria-label="Revoke connector" className="flex items-center gap-2 text-sm">
          <span>Revoke the connector? The agent is disconnected and must be enrolled again.</span>
          <Button
            type="button"
            variant="destructive"
            disabled={busy}
            onClick={() =>
              void run(async () => {
                await onRevoke();
                setConfirmRevoke(false);
              })
            }
          >
            Revoke connector
          </Button>
          <Button type="button" variant="outline" onClick={() => setConfirmRevoke(false)}>
            Cancel
          </Button>
        </div>
      )}
    </section>
  );
}
