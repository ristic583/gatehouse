import { Badge } from "@/components/ui/badge";
import type { AlertSeverity, TenantStatus } from "@/lib/api/types";

const STATUS_TEXT: Record<TenantStatus, string> = {
  Active: "Active",
  Suspended: "Suspended",
  Deprovisioned: "Deprovisioned",
  Provisioning: "Provisioning",
  ProvisioningFailed: "Provisioning failed",
};

const STATUS_VARIANT: Record<TenantStatus, "default" | "secondary" | "destructive" | "outline"> = {
  Active: "default",
  Suspended: "secondary",
  Deprovisioned: "outline",
  Provisioning: "outline",
  ProvisioningFailed: "destructive",
};

export function TenantStatusChip({ status }: { status: TenantStatus }) {
  return (
    <Badge variant={STATUS_VARIANT[status]} data-testid="tenant-status">
      {STATUS_TEXT[status]}
    </Badge>
  );
}

export function SeverityChip({ severity }: { severity: AlertSeverity }) {
  return <Badge variant={severity === "Error" ? "destructive" : severity === "Warning" ? "secondary" : "outline"}>{severity}</Badge>;
}
