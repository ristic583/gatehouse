import { apiClient } from "@/lib/api/client";
import type { ReconciliationInstanceDto, ReconciliationReportDto, ReconciliationReportSummaryDto } from "@/lib/api/types";

export const reconciliationKeys = {
  all: ["reconciliation"] as const,
  instances: () => [...reconciliationKeys.all, "instances"] as const,
  reports: (pacsInstanceId?: string) => [...reconciliationKeys.all, "reports", pacsInstanceId ?? "all"] as const,
  report: (reportId: string) => [...reconciliationKeys.all, "report", reportId] as const,
};

export const RUNNING_STATUSES: ReadonlySet<string> = new Set(["Requested", "Exporting", "Comparing"]);

/** Report queries poll every 2 s while any report is still running. */
export const RUNNING_REFETCH_MS = 2_000;

export const reconciliationApi = {
  instances: () => apiClient.get<ReconciliationInstanceDto[]>("/v1/reconciliation/instances"),
  reports: (pacsInstanceId?: string, limit = 20) =>
    apiClient.get<ReconciliationReportSummaryDto[]>(
      `/v1/reconciliation/reports?limit=${limit}${pacsInstanceId === undefined ? "" : `&pacsInstanceId=${pacsInstanceId}`}`,
    ),
  report: (reportId: string) => apiClient.get<ReconciliationReportDto>(`/v1/reconciliation/reports/${reportId}`),
  run: (pacsInstanceId: string) => apiClient.post<ReconciliationReportSummaryDto>("/v1/reconciliation/reports", { pacsInstanceId }),
};
