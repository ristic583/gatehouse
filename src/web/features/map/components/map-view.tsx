"use client";

import { useQueries, useQuery } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { Crosshair, DoorOpen, Info } from "lucide-react";
import { BuildingSearch } from "./building-search";
import { AreaInspector } from "./area-inspector";
import { AtlasToolbar } from "./atlas-toolbar";
import { MapJourney, MapLegend } from "./map-journey";
import { MapStats } from "./map-stats";
import { RoomBrowser } from "./room-browser";
import { SceneControls } from "./scene-controls";
import { useSceneState } from "../hooks/use-scene-state";
import { useCompactScreen, useMapPreferences, type MapMode } from "../hooks/use-map-preferences";
import { PersonalWorkspace } from "@/features/workspace/personal-workspace";
import { useMe } from "@/lib/api/me";
import { apiClient } from "@/lib/api/client";
import type { AreaAccessDto, FloorAccessMapDto, SiteDto } from "@/lib/api/types";
import { RequestAccessDialog } from "@/features/requests/components/request-access-dialog";
import { requestsApi } from "@/features/requests/api";
import { EmptyState } from "@/components/flow-steps";
import { FloorMap } from "./floor-map";
import { SpatialBuilding } from "./spatial-building";

export function MapView() {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const me = useMe();
  const compact = useCompactScreen();
  const { preferences, save } = useMapPreferences(me.data?.tenantId, me.data?.subject);
  const inspector = useRef<HTMLElement>(null);
  const focusInspector = useRef<string | null>(null);
  const sites = useQuery({ queryKey: ["sites"], queryFn: () => apiClient.get<SiteDto[]>("/v1/sites") });
  const siteList = sites.data ?? [];
  const site = siteList.find((s) => s.siteId === (params.get("site") ?? preferences.siteId)) ?? siteList[0];
  const floor =
    site?.floors.find(
      (f) => f.floorId === (params.get("floor") ?? (site?.siteId === preferences.siteId ? preferences.floorId : undefined)),
    ) ?? [...(site?.floors ?? [])].sort((a, b) => a.level - b.level)[0];
  const floorMaps = useQueries({
    queries: (site?.floors ?? []).map((f) => ({
      queryKey: ["access-map", f.floorId],
      queryFn: () => apiClient.get<FloorAccessMapDto>(`/v1/floors/${f.floorId}/access-map`),
    })),
  });
  const maps = floorMaps.flatMap((q) => (q.data ? [q.data] : []));
  const map = maps.find((m) => m.floorId === floor?.floorId);
  const selectedId = params.get("area");
  const [requesting, setRequesting] = useState<AreaAccessDto | null>(null);
  const scene = useSceneState();
  const selected = map?.areas.find((a) => a.areaId === selectedId) ?? map?.areas[0];
  const details = useQuery({
    queryKey: ["area", selected?.areaId],
    queryFn: () => requestsApi.area(selected?.areaId ?? ""),
    enabled: !!selected,
  });
  const requestedMode = params.get("mode");
  const mode: MapMode =
    requestedMode === "plan" || requestedMode === "spatial" || requestedMode === "list"
      ? requestedMode
      : compact
        ? (preferences.mobileMode ?? "list")
        : (preferences.desktopMode ?? "spatial");
  useEffect(() => {
    if (!focusInspector.current || focusInspector.current !== selected?.areaId) return;
    focusInspector.current = null;
    inspector.current?.focus({ preventScroll: !compact });
    if (compact) inspector.current?.scrollIntoView({ block: "nearest" });
  }, [selected?.areaId, compact, scene.focusToken]);

  function setMode(value: MapMode) {
    const p = new URLSearchParams(params.toString());
    p.set("mode", value);
    save(compact ? { mobileMode: value } : { desktopMode: value });
    router.replace(`${pathname}?${p}`, { scroll: false });
  }
  function navigate(siteId: string, floorId: string, areaId?: string) {
    const p = new URLSearchParams(params.toString());
    p.set("site", siteId);
    p.set("floor", floorId);
    if (areaId) p.set("area", areaId);
    else p.delete("area");
    save({ siteId, floorId });
    router.replace(`${pathname}?${p}`, { scroll: false });
  }
  function selectArea(area: AreaAccessDto, focus = false) {
    const owner = maps.find((m) => m.areas.some((a) => a.areaId === area.areaId));
    if (!owner) return;
    navigate(owner.siteId, owner.floorId, area.areaId);
    focusInspector.current = compact || focus ? area.areaId : null;
    if (focus) scene.focusRoom();
    else scene.bumpFocus();
  }
  if (sites.isError)
    return (
      <p role="alert">
        Could not load sites. <button onClick={() => void sites.refetch()}>Try again</button>
      </p>
    );
  if (!sites.data) return <div className="gh-loading">Initializing spatial workspace…</div>;
  if (!site || !floor)
    return (
      <EmptyState title="No building configured" description="Your administrator can configure sites and floor plans for this workspace." />
    );

  const areas = map?.areas ?? [];
  const unknown = areas.filter((a) => a.state === "Unknown").length;
  const canViewRoster =
    pathname !== "/design-preview" &&
    ((me.data?.roles ?? []).includes("SecurityStaff") ||
      (!!me.data?.person && (details.data?.owners ?? []).some((o) => o.personId === me.data?.person?.personId)));
  const requestsHref = pathname === "/design-preview" ? "/design-preview?view=requests" : "/requests";
  const floorError = floorMaps.some((q) => q.isError);
  const roomBrowser = (
    <RoomBrowser
      floorName={floor.name}
      areas={areas}
      selectedAreaId={selected?.areaId}
      loaded={!!map}
      floorError={floorError}
      onSelect={(a) => selectArea(a)}
    />
  );
  return (
    <div className="gh-command-workspace">
      <PersonalWorkspace preview={pathname === "/design-preview"} />
      <BuildingSearch
        maps={maps}
        loading={floorMaps.some((q) => q.isPending)}
        incomplete={floorMaps.some((q) => q.isError)}
        onSelect={(_, area) => selectArea(area, true)}
      />

      <MapStats site={site} floorName={floor.name} areas={areas} loaded={!!map} />

      {floorError && (
        <p role="alert" className="gh-filter-notice">
          Some floors are unavailable. Search results may be incomplete.{" "}
          <button
            onClick={() =>
              floorMaps.forEach((q) => {
                if (q.isError) void q.refetch();
              })
            }
          >
            Retry floors
          </button>
        </p>
      )}
      <div className={`gh-atlas-layout ${mode === "list" ? "is-list" : ""}`}>
        <section className="gh-atlas-panel">
          <AtlasToolbar site={site} siteList={siteList} floor={floor} mode={mode} onMode={setMode} onNavigate={navigate} />
          {mode === "list" ? (
            roomBrowser
          ) : (
            <div className="gh-atlas-canvas">
              <div className="gh-canvas-meta">
                <span>
                  <span className="gh-cross-mark">+</span> SPATIAL CONTROL / {site.code}
                </span>
                <h3>{floor.name}</h3>
                <p>{mode === "spatial" ? "Drag to orbit · scroll to zoom · select a space" : "Select a room to inspect access"}</p>
              </div>
              <div className="gh-scene-coordinate" aria-hidden="true">
                PLAN {map?.planWidth ?? "—"} × {map?.planHeight ?? "—"}
                <br />
                LEVEL {String(floor.level).padStart(2, "0")}
              </div>

              {mode === "spatial" ? (
                <SpatialBuilding
                  key={site.siteId}
                  floors={site.floors}
                  maps={maps}
                  activeFloorId={floor.floorId}
                  selectedAreaId={selected?.areaId}
                  onFloorSelect={(id) => navigate(site.siteId, id)}
                  onAreaSelect={(a) => selectArea(a)}
                  zoom={scene.zoom}
                  exploded={scene.view === "exploded"}
                  view={scene.view}
                  cutaway={scene.cutaway}
                  interiors={scene.interiors}
                  labels={scene.labels}
                  xray={scene.xray}
                  autoRotate={scene.autoRotate}
                  cameraPreset={scene.cameraPreset}
                  focusToken={scene.focusToken}
                />
              ) : (
                <div className="gh-flat-map">
                  {map ? <FloorMap map={map} onAreaSelect={(a) => selectArea(a)} /> : <p>Loading floor plan…</p>}
                </div>
              )}
              <div className="gh-floor-rail" aria-label="Floors">
                {[...site.floors]
                  .sort((a, b) => b.level - a.level)
                  .map((f) => (
                    <button key={f.floorId} aria-pressed={f.floorId === floor.floorId} onClick={() => navigate(site.siteId, f.floorId)}>
                      <span>{f.level === 0 ? "G" : `L${f.level}`}</span>
                      <span>{f.name}</span>
                      <i />
                    </button>
                  ))}
              </div>
              {mode === "spatial" && <SceneControls scene={scene} />}
            </div>
          )}
          <div className="gh-atlas-foot">
            <span>
              <Crosshair size={12} />{" "}
              {mode === "spatial" ? "ARCHITECTURAL SCHEMATIC" : mode === "list" ? "ROOM DIRECTORY" : "RECORDED FLOOR PLAN"}
            </span>
            <span>
              <DoorOpen size={12} /> {areas.reduce((n, a) => n + a.doors.length, 0)} mapped doors <i />{" "}
              {unknown ? `${unknown} unknown access states` : "No unknown access states"}
            </span>
          </div>
        </section>

        <aside className="gh-atlas-sidebar">
          {mode !== "list" && roomBrowser}
          {selected && (
            <AreaInspector
              key={selected.areaId}
              ref={inspector}
              selected={selected}
              floorName={floor.name}
              details={details}
              requestsHref={requestsHref}
              canViewRoster={canViewRoster}
              showFocus={mode === "spatial"}
              onFocus={scene.focusRoom}
              onRequest={() => setRequesting(selected)}
            />
          )}
          <p className="gh-atlas-tip">
            <Info size={14} /> Room states reflect your personal access. Physical door state is available separately in Live building.
          </p>
        </aside>
      </div>

      <MapLegend spatial={mode === "spatial"} interiors={scene.interiors} />
      <MapJourney requestsHref={requestsHref} />
      <RequestAccessDialog
        area={requesting}
        onClose={() => setRequesting(null)}
        location={{ siteName: site.name, floorName: floor.name }}
      />
    </div>
  );
}
