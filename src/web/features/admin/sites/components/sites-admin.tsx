"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { PageState, QueryErrorState } from "@/components/ui/page-state";
import type { SiteDto } from "@/lib/api/types";
import { sitesAdminApi, sitesAdminKeys } from "../api";
import { errorOf, FloorForm, SiteForm } from "./site-forms";

export { FloorForm, SiteForm } from "./site-forms";

export function useSites() {
  return useQuery({ queryKey: sitesAdminKeys.sites, queryFn: sitesAdminApi.sites });
}

export function SitesAdmin() {
  const queryClient = useQueryClient();
  const sites = useSites();
  const refresh = () => queryClient.invalidateQueries({ queryKey: sitesAdminKeys.sites });
  const remove = useMutation({
    mutationFn: sitesAdminApi.deleteSite,
    onSuccess: refresh,
    onError: (e) => toast.error(errorOf(e) ?? "Delete failed"),
  });

  return (
    <div className="flex flex-col gap-4">
      <section className="gh-panel" aria-label="New site">
        <h3 className="gh-panel-title">New site</h3>
        <SiteForm
          submitLabel="Create site"
          onSubmit={async (body) => {
            await sitesAdminApi.createSite(body);
            toast.success("Site created");
            await refresh();
          }}
        />
      </section>
      {sites.isPending ? (
        <PageState kind="loading" message="Loading sites…" />
      ) : sites.isError ? (
        <QueryErrorState error={sites.error} message="Sites could not be loaded." onRetry={() => sites.refetch()} />
      ) : sites.data.length === 0 ? (
        <PageState kind="empty" message="No sites yet." hint="Create the first site with the form above, then add its floors." />
      ) : (
        <div className="gh-table-wrap">
          <table className="w-full text-left">
            <thead>
              <tr>
                <th>Site</th>
                <th>Code</th>
                <th>Time zone</th>
                <th>Floors</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {sites.data.map((s: SiteDto) => (
                <tr key={s.siteId} data-testid="admin-site-row">
                  <td>
                    <Link className="underline" href={`/admin/sites/${s.siteId}`}>
                      {s.name}
                    </Link>
                  </td>
                  <td>{s.code}</td>
                  <td>{s.timeZoneId}</td>
                  <td>{s.floors.length}</td>
                  <td>
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      onClick={() => remove.mutate(s.siteId)}
                      disabled={s.floors.length > 0}
                    >
                      Delete
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

export function SiteDetail({ siteId }: { siteId: string }) {
  const queryClient = useQueryClient();
  const sites = useSites();
  const refresh = () => queryClient.invalidateQueries({ queryKey: sitesAdminKeys.sites });
  const removeFloor = useMutation({
    mutationFn: sitesAdminApi.deleteFloor,
    onSuccess: refresh,
    onError: (e) => toast.error(errorOf(e) ?? "Delete failed"),
  });
  if (sites.isPending) return <PageState kind="loading" message="Loading site…" />;
  if (sites.isError) return <QueryErrorState error={sites.error} message="The site could not be loaded." onRetry={() => sites.refetch()} />;
  const site = sites.data.find((s) => s.siteId === siteId);
  if (site === undefined) return <PageState kind="empty" message="Site not found." hint={<Link href="/admin/sites">Back to sites</Link>} />;

  return (
    <div className="flex flex-col gap-4">
      <section className="gh-panel" aria-label="Site">
        <h3 className="gh-panel-title">{site.name}</h3>
        <SiteForm
          initial={site}
          submitLabel="Save site"
          onSubmit={async (body) => {
            await sitesAdminApi.updateSite(siteId, body);
            toast.success("Site saved");
            await refresh();
          }}
        />
      </section>
      <section className="gh-panel flex flex-col gap-3" aria-label="Floors">
        <h3 className="gh-panel-title">Floors</h3>
        <ul className="flex flex-col gap-1 text-sm">
          {site.floors.map((f) => (
            <li key={f.floorId} className="flex items-center gap-3" data-testid="admin-floor-row">
              <Link className="underline" href={`/admin/sites/floors/${f.floorId}`}>
                {f.name}
              </Link>
              <span className="text-muted-foreground">level {f.level}</span>
              <Button type="button" size="sm" variant="outline" onClick={() => removeFloor.mutate(f.floorId)}>
                Delete
              </Button>
            </li>
          ))}
          {site.floors.length === 0 && <li className="text-muted-foreground">No floors yet.</li>}
        </ul>
        <FloorForm
          submitLabel="Add floor"
          onSubmit={async (body) => {
            await sitesAdminApi.createFloor(siteId, body);
            toast.success("Floor added");
            await refresh();
          }}
        />
      </section>
    </div>
  );
}
