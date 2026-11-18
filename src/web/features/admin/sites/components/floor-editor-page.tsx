"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { floorBackgroundUrl } from "@/features/map/components/plan-underlay";
import { SpatialBuilding } from "@/features/map/components/spatial-building";
import type { FloorAccessMapDto, FloorPlanDto, SecurityLevelAdminDto } from "@/lib/api/types";
import { sitesAdminApi, sitesAdminKeys } from "../api";
import { runSave } from "../save";
import { FloorBackground, readOpacity } from "./floor-background";
import { PlanEditor } from "./plan-editor";
import { FloorForm } from "./sites-admin";

/** Saved plan as the access-map shape the 3D scene renders; neutral "Unknown" state colors from access-state.ts. */
export function planToAccessMap(plan: FloorPlanDto, levels: readonly SecurityLevelAdminDto[]): FloorAccessMapDto {
  return {
    floorId: plan.floorId,
    siteId: plan.siteId,
    floorName: plan.name,
    planWidth: plan.planWidth,
    planHeight: plan.planHeight,
    hasBackground: plan.hasBackground,
    backgroundUpdatedAt: plan.backgroundUpdatedAt,
    areas: plan.areas.map((a) => ({
      areaId: a.id,
      name: a.name,
      code: a.code,
      polygon: a.polygon,
      securityLevelName: levels.find((l) => l.id === a.securityLevelId)?.name ?? "",
      state: "Unknown",
      reason: "NoEntitlement",
      cardInactive: false,
      since: null,
      doors: a.doors.map((d) => ({ doorId: d.id, name: d.name, x: d.position.x, y: d.position.y })),
    })),
  };
}

export function FloorEditorPage({ floorId }: { floorId: string }) {
  const queryClient = useQueryClient();
  const plan = useQuery({ queryKey: sitesAdminKeys.plan(floorId), queryFn: () => sitesAdminApi.plan(floorId) });
  const levels = useQuery({ queryKey: sitesAdminKeys.securityLevels, queryFn: sitesAdminApi.securityLevels });
  const [preview, setPreview] = useState(false);
  const [opacity, setOpacity] = useState(() => (typeof window === "undefined" ? 0.5 : readOpacity(floorId)));
  const refresh = () => queryClient.invalidateQueries({ queryKey: sitesAdminKeys.plan(floorId) });

  if (plan.isPending || levels.isPending) return <p className="gh-loading">Loading floor plan…</p>;
  if (plan.isError || levels.isError) return <p role="alert">The floor plan could not be loaded.</p>;
  const data = plan.data;
  const map = planToAccessMap(data, levels.data);

  return (
    <div className="flex flex-col gap-4">
      <Link className="text-sm underline" href={`/admin/sites/${data.siteId}`}>
        ← Back to site
      </Link>
      <section className="gh-panel" aria-label="Floor">
        <h3 className="gh-panel-title">{data.name}</h3>
        <FloorForm
          key={`${data.name}-${data.level}-${data.planWidth}-${data.planHeight}`}
          initial={data}
          submitLabel="Save floor"
          onSubmit={async (body) => {
            await sitesAdminApi.updateFloor(floorId, body);
            toast.success("Floor saved");
            await refresh();
            await queryClient.invalidateQueries({ queryKey: sitesAdminKeys.sites });
          }}
        />
      </section>
      <FloorBackground
        floorId={floorId}
        hasBackground={data.hasBackground}
        opacity={opacity}
        onOpacityChange={setOpacity}
        onChanged={() => void refresh()}
      />
      <div className="flex gap-2">
        <Button type="button" variant={preview ? "outline" : "default"} aria-pressed={!preview} onClick={() => setPreview(false)}>
          Edit plan
        </Button>
        <Button type="button" variant={preview ? "default" : "outline"} aria-pressed={preview} onClick={() => setPreview(true)}>
          Preview 3D
        </Button>
      </div>
      {preview ? (
        <div data-testid="plan-preview">
          <p className="text-xs text-muted-foreground">Shows the saved plan. Unsaved edits are not included.</p>
          <SpatialBuilding
            floors={[{ floorId: data.floorId, name: data.name, level: data.level }]}
            maps={[map]}
            activeFloorId={data.floorId}
            onFloorSelect={() => undefined}
            onAreaSelect={() => undefined}
            view="isolated"
            labels
          />
        </div>
      ) : (
        <PlanEditor
          key={plan.dataUpdatedAt}
          plan={data}
          securityLevels={levels.data}
          searchEmployees={sitesAdminApi.searchEmployees}
          backgroundUrl={data.hasBackground ? floorBackgroundUrl(floorId, data.backgroundUpdatedAt) : null}
          backgroundOpacity={opacity}
          onSave={async (original, current) => {
            const outcome = await runSave(floorId, original, current);
            if (Object.keys(outcome.errors).length === 0) {
              toast.success("Plan saved");
              await refresh();
            }
            return outcome;
          }}
        />
      )}
    </div>
  );
}
