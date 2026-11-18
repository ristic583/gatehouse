"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { AuditPanel } from "@/features/audit/components/audit-panel";
import { LifecyclePanel } from "@/features/lifecycle/components/lifecycle-panel";
import { StaffTaskList } from "@/features/tasks/components/staff-task-list";
import { ThemePanel } from "@/features/theming/components/theme-panel";
import { FulfillmentDetail } from "@/features/fulfillment/components/fulfillment-detail";
import { FulfillmentQueue } from "@/features/fulfillment/components/fulfillment-queue";
import { ReconciliationPanel } from "@/features/reconciliation/components/reconciliation-panel";
import { ReconciliationReportView } from "@/features/reconciliation/components/reconciliation-report-view";
import { PageState, QueryErrorState } from "@/components/ui/page-state";
import { useMe } from "@/lib/api/me";

type ConsoleTab = "fulfillment" | "tasks" | "reconciliation" | "audit" | "lifecycle" | "theme";

const LABELS: Record<ConsoleTab, string> = {
  fulfillment: "Fulfillment",
  tasks: "Tasks",
  reconciliation: "Reconciliation",
  audit: "Audit",
  lifecycle: "Lifecycle",
  theme: "Theme",
};

function tabsFor(roles: string[]): ConsoleTab[] {
  const staff = roles.includes("SecurityStaff");
  const reconciliation = staff || roles.includes("TenantAdmin");
  return [
    ...(staff ? (["fulfillment"] as const) : []),
    "tasks",
    ...(reconciliation ? (["reconciliation"] as const) : []),
    "audit",
    ...(roles.includes("TenantAdmin") ? (["lifecycle", "theme"] as const) : []),
  ];
}

export function ConsoleView() {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const me = useMe();

  if (me.data === undefined) {
    return me.isError ? (
      <QueryErrorState error={me.error} message="Could not load your profile." onRetry={() => me.refetch()} />
    ) : (
      <PageState kind="loading" message="Loading the console…" />
    );
  }

  if (!me.data.roles.includes("SecurityStaff") && !me.data.roles.includes("TenantAdmin")) {
    return <PageState kind="forbidden" message="You are not permitted to use the console. It is for security staff and tenant admins." />;
  }

  const tabs = tabsFor(me.data.roles);
  const requested = params.get("tab");
  const item = params.get("item");
  const active = tabs.find((t) => t === requested) ?? tabs[0] ?? "tasks";

  return (
    <div className="flex flex-col gap-4">
      <div role="tablist" className="flex gap-2">
        {tabs.map((tab) => (
          <button
            key={tab}
            type="button"
            role="tab"
            aria-selected={tab === active}
            className={tab === active ? "border-b-2 border-primary px-3 py-1 font-medium" : "px-3 py-1"}
            onClick={() => router.replace(`${pathname}?tab=${tab}`)}
          >
            {LABELS[tab]}
          </button>
        ))}
      </div>
      {active === "fulfillment" ? (
        <div className="grid gap-6 lg:grid-cols-2">
          <FulfillmentQueue onSelect={(id) => router.replace(`${pathname}?tab=fulfillment&item=${id}`)} />
          {item !== null ? <FulfillmentDetail id={item} /> : null}
        </div>
      ) : null}
      {active === "tasks" ? <StaffTaskList canHandle={me.data.roles.includes("SecurityStaff")} /> : null}
      {active === "reconciliation" ? (
        <div className="grid gap-6 lg:grid-cols-2">
          <ReconciliationPanel onSelect={(id) => router.replace(`${pathname}?tab=reconciliation&item=${id}`)} />
          {item !== null ? <ReconciliationReportView reportId={item} /> : null}
        </div>
      ) : null}
      {active === "audit" ? <AuditPanel /> : null}
      {active === "lifecycle" ? <LifecyclePanel /> : null}
      {active === "theme" ? <ThemePanel /> : null}
    </div>
  );
}
