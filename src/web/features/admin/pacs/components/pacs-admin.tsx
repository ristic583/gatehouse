"use client";

import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { PageState, QueryErrorState } from "@/components/ui/page-state";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import type { PacsInstanceAdminDto, SiteDto } from "@/lib/api/types";
import { sitesAdminApi, sitesAdminKeys } from "../../sites/api";
import { pacsAdminApi, pacsAdminKeys, VENDOR_TEXT } from "../api";
import { ConnectorPanel } from "./connector-panel";
import { type MappableArea, MappingGrid } from "./mapping-grid";
import { errorText, PacsInstanceForm } from "./pacs-instance-form";

export { PacsInstanceForm } from "./pacs-instance-form";

function connectorText(instance: PacsInstanceAdminDto): string {
  if (instance.connector === null) return "None";
  if (instance.connector.status !== "Active") return instance.connector.status === "PendingEnrollment" ? "Pending enrollment" : "Revoked";
  return instance.health?.online ? "Online" : "Offline";
}

export function PacsAdmin() {
  const queryClient = useQueryClient();
  const list = useQuery({ queryKey: pacsAdminKeys.list, queryFn: pacsAdminApi.list, refetchInterval: 15_000 });
  const sites = useQuery({ queryKey: sitesAdminKeys.sites, queryFn: sitesAdminApi.sites });
  const siteName = (id: string) => sites.data?.find((s) => s.siteId === id)?.name ?? id;

  return (
    <div className="flex flex-col gap-4">
      <section className="gh-panel" aria-label="New PACS instance">
        <h3 className="gh-panel-title">New PACS instance</h3>
        <PacsInstanceForm
          sites={sites.data ?? []}
          submitLabel="Create instance"
          onSubmit={async (body) => {
            await pacsAdminApi.create(body);
            toast.success("PACS instance created");
            await queryClient.invalidateQueries({ queryKey: pacsAdminKeys.list });
          }}
        />
      </section>
      {list.isPending ? (
        <PageState kind="loading" message="Loading PACS instances…" />
      ) : list.isError ? (
        <QueryErrorState error={list.error} message="PACS instances could not be loaded." onRetry={() => list.refetch()} />
      ) : list.data.length === 0 ? (
        <PageState
          kind="empty"
          message="No PACS instances yet."
          hint="Add your access control system with the form above, then install its connector and map its access levels."
        />
      ) : (
        <div className="gh-table-wrap">
          <table className="w-full text-left">
            <thead>
              <tr>
                <th>Instance</th>
                <th>Vendor</th>
                <th>Sites</th>
                <th>Mode</th>
                <th>Connector</th>
                <th>Unmapped</th>
              </tr>
            </thead>
            <tbody>
              {list.data.map((p) => (
                <tr key={p.id} data-testid="pacs-row">
                  <td>
                    <Link className="underline" href={`/admin/pacs/${p.id}`}>
                      {p.name}
                    </Link>
                  </td>
                  <td>{VENDOR_TEXT[p.vendor]}</td>
                  <td>{p.siteIds.map(siteName).join(", ")}</td>
                  <td>{p.fulfillmentMode}</td>
                  <td>
                    <Badge variant="outline" data-testid="pacs-connector">
                      {connectorText(p)}
                    </Badge>
                  </td>
                  <td>{p.unmappedAreas}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function useMappableAreas(siteIds: readonly string[], sites: readonly SiteDto[]): { areas: MappableArea[]; loading: boolean } {
  const floors = sites.filter((s) => siteIds.includes(s.siteId)).flatMap((s) => s.floors.map((f) => ({ site: s, floor: f })));
  const plans = useQueries({
    queries: floors.map(({ floor }) => ({
      queryKey: sitesAdminKeys.plan(floor.floorId),
      queryFn: () => sitesAdminApi.plan(floor.floorId),
    })),
  });
  const areas = plans.flatMap((q, i) =>
    (q.data?.areas ?? []).map((a) => ({ areaId: a.id, name: a.name, siteName: floors[i]!.site.name, floorName: floors[i]!.floor.name })),
  );
  return { areas, loading: plans.some((q) => q.isPending) };
}

export function PacsInstanceDetail({ id }: { id: string }) {
  const queryClient = useQueryClient();
  const router = useRouter();
  // Health polling every 5 s (connector status, online/offline, capability gaps).
  const instance = useQuery({ queryKey: pacsAdminKeys.detail(id), queryFn: () => pacsAdminApi.get(id), refetchInterval: 5000 });
  const sites = useQuery({ queryKey: sitesAdminKeys.sites, queryFn: sitesAdminApi.sites });
  const { areas, loading } = useMappableAreas(instance.data?.siteIds ?? [], sites.data ?? []);
  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: pacsAdminKeys.detail(id) });
    await queryClient.invalidateQueries({ queryKey: pacsAdminKeys.list });
  };
  const remove = useMutation({
    mutationFn: () => pacsAdminApi.remove(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: pacsAdminKeys.list });
      router.push("/admin/pacs");
    },
    onError: (e) => toast.error(errorText(e)),
  });

  if (instance.isError) {
    return <QueryErrorState error={instance.error} message="The PACS instance could not be loaded." onRetry={() => instance.refetch()} />;
  }
  if (instance.isPending || sites.isPending) return <PageState kind="loading" message="Loading PACS instance…" />;
  const data = instance.data;

  return (
    <div className="flex flex-col gap-4">
      <Link className="text-sm underline" href="/admin/pacs">
        ← All PACS instances
      </Link>
      <section className="gh-panel flex flex-col gap-2" aria-label="PACS instance">
        <h3 className="gh-panel-title">
          {data.name} <span>{data.fulfillmentMode}</span>
        </h3>
        <PacsInstanceForm
          key={`${data.name}-${data.vendor}-${data.siteIds.join(",")}`}
          initial={data}
          sites={sites.data ?? []}
          submitLabel="Save instance"
          onSubmit={async (body) => {
            await pacsAdminApi.update(id, body);
            toast.success("PACS instance saved");
            await refresh();
          }}
        />
        <Button type="button" variant="outline" size="sm" className="self-start" onClick={() => remove.mutate()}>
          Delete instance
        </Button>
      </section>
      {loading ? (
        <p className="gh-loading">Loading areas…</p>
      ) : (
        <MappingGrid
          key={data.mappings.map((m) => `${m.areaId}:${m.vendorAccessLevelRef}`).join("|")}
          areas={areas}
          mappings={data.mappings}
          onSave={async (body) => {
            try {
              await pacsAdminApi.setMappings(id, body);
            } catch (e) {
              throw new Error(errorText(e));
            }
            toast.success("Mappings saved");
            await refresh();
          }}
        />
      )}
      <ConnectorPanel
        instance={data}
        onRegister={async () => {
          try {
            const enrollment = await pacsAdminApi.registerConnector(id);
            await refresh();
            return enrollment;
          } catch (e) {
            throw new Error(errorText(e));
          }
        }}
        onRevoke={async () => {
          try {
            await pacsAdminApi.revokeConnector(id);
          } catch (e) {
            throw new Error(errorText(e));
          }
          toast.success("Connector revoked");
          await refresh();
        }}
      />
    </div>
  );
}
