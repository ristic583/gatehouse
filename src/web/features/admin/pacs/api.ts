import { apiClient } from "@/lib/api/client";
import type { AccessLevelMappingRequest, ConnectorEnrollmentDto, PacsInstanceAdminDto, SavePacsInstanceRequest } from "@/lib/api/types";

const BASE = "/v1/admin/pacs-instances";

export const pacsAdminKeys = {
  list: ["admin", "pacs-instances"] as const,
  detail: (id: string) => ["admin", "pacs-instances", id] as const,
  areas: (siteIds: readonly string[]) => ["admin", "pacs-areas", [...siteIds].sort().join(",")] as const,
};

export const pacsAdminApi = {
  list: () => apiClient.get<PacsInstanceAdminDto[]>(BASE),
  get: (id: string) => apiClient.get<PacsInstanceAdminDto>(`${BASE}/${id}`),
  create: (body: SavePacsInstanceRequest) => apiClient.post<PacsInstanceAdminDto>(BASE, body),
  update: (id: string, body: SavePacsInstanceRequest) => apiClient.put<PacsInstanceAdminDto>(`${BASE}/${id}`, body),
  remove: (id: string) => apiClient.delete<void>(`${BASE}/${id}`),
  setMappings: (id: string, body: AccessLevelMappingRequest[]) => apiClient.put<PacsInstanceAdminDto>(`${BASE}/${id}/mappings`, body),
  registerConnector: (id: string) => apiClient.post<ConnectorEnrollmentDto>(`${BASE}/${id}/connector`),
  revokeConnector: (id: string) => apiClient.post<void>(`${BASE}/${id}/connector/revoke`),
};

export const VENDOR_TEXT: Record<SavePacsInstanceRequest["vendor"], string> = { LegacyPacs: "LegacyPACS", CloudPacs: "CloudPACS" };

/** Agent environment (`Gatehouse.Agent` reads section `Agent`, see `AgentOptions`); vendor values are placeholders. */
/** Local tag used when no published agent image is configured; the snippet then starts with the build command. */
export const LOCAL_AGENT_IMAGE = "gatehouse-agent:local";

export function installSnippet(
  enrollment: Pick<ConnectorEnrollmentDto, "gatewayUrl" | "enrollmentToken" | "vendor"> & { agentImage?: string | null },
): string {
  const image = enrollment.agentImage ?? null;
  const buildLines =
    image === null
      ? [
          "# No published agent image is configured. Build it from the Gatehouse repository first:",
          `docker build -f deploy/Dockerfile --target agent -t ${LOCAL_AGENT_IMAGE} .`,
          "",
        ]
      : [];
  const vendorLines =
    enrollment.vendor === "CloudPacs"
      ? [
          "  -e Agent__CloudPacsUrl=<cloudpacs-api-url> \\",
          "  -e Agent__CloudPacsApiKey=<cloudpacs-api-key> \\",
          "  -e Agent__WebhookListenUrl=http://0.0.0.0:5186 \\",
        ]
      : ["  -e Agent__LegacyPacsConnectionString=<legacy-pacs-connection-string> \\"];
  return [
    ...buildLines,
    "docker run -d --name gatehouse-agent --restart unless-stopped \\",
    `  -e Agent__GatewayUrl=${enrollment.gatewayUrl} \\`,
    `  -e Agent__EnrollmentToken=${enrollment.enrollmentToken} \\`,
    `  -e Agent__Vendor=${enrollment.vendor} \\`,
    ...vendorLines,
    "  -e Agent__DataDirectory=/data -v gatehouse-agent-data:/data \\",
    `  ${image ?? LOCAL_AGENT_IMAGE}`,
  ].join("\n");
}
