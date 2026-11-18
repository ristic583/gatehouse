"use client";
import type { AreaAccessDto, FloorAccessMapDto, FloorSummaryDto, PointDto } from "@/lib/api/types";
import type { BuildingView } from "../scene/geometry";
import { ACCESS_STATE_META } from "../access-state";

interface Props {
  floors: FloorSummaryDto[];
  maps: FloorAccessMapDto[];
  activeFloorId: string;
  selectedAreaId?: string;
  onFloorSelect: (id: string) => void;
  onAreaSelect: (area: AreaAccessDto) => void;
  zoom?: number;
  exploded?: boolean;
  view?: BuildingView;
  labels?: boolean;
}
/** Isometric SVG projection of the stored floor polygons. It is a spatial schematic, not a measured BIM model. */
export function SpatialBuildingFallback({
  floors,
  maps,
  activeFloorId,
  selectedAreaId,
  onFloorSelect,
  onAreaSelect,
  zoom = 1,
  exploded = true,
  view,
  labels = true,
}: Props) {
  const sorted = floors.filter((f) => view !== "isolated" || f.floorId === activeFloorId).sort((a, b) => a.level - b.level);
  const count = sorted.length;
  const gap = exploded ? Math.min(120, 280 / Math.max(1, count - 1)) : 30;
  const baseY = 230 + ((count - 1) * gap) / 2;
  const project = (x: number, y: number, z: number) => ({ x: 440 + (x - y) * 0.55, y: baseY + (x + y) * 0.2 - z });
  const point = (x: number, y: number, z: number) => {
    const p = project(x, y, z);
    return `${p.x},${p.y}`;
  };
  return (
    <svg
      viewBox="0 0 900 610"
      className="gh-spatial-svg"
      role="group"
      aria-label="Interactive spatial building"
      data-testid="spatial-building"
    >
      <defs>
        <pattern id="spatial-pending" width="8" height="8" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
          <line x1="0" y1="0" x2="0" y2="8" stroke="var(--gh-state-pending)" strokeWidth="2" strokeOpacity=".3" />
        </pattern>
        <filter id="slab-shadow" x="-50%" y="-100%" width="200%" height="300%">
          <feGaussianBlur stdDeviation="12" />
        </filter>
      </defs>
      <g transform={`translate(450 300) scale(${zoom}) translate(-450 -300)`}>
        {Array.from({ length: 15 }, (_, i) => (
          <g key={i} stroke="#284149" strokeWidth=".6" opacity=".7">
            <path d={`M ${point(-450 + i * 65, -340, -45)} L ${point(-450 + i * 65, 340, -45)}`} />
            <path d={`M ${point(-450, -340 + i * 50, -45)} L ${point(450, -340 + i * 50, -45)}`} />
          </g>
        ))}
        <ellipse cx="440" cy={baseY + 135} rx="265" ry="65" fill="#29424a" opacity=".12" filter="url(#slab-shadow)" />
        {sorted.map((floor, index) => {
          const map = maps.find((m) => m.floorId === floor.floorId);
          const active = floor.floorId === activeFloorId;
          const z = index * gap;
          const corners = [
            [-300, -190],
            [300, -190],
            [300, 190],
            [-300, 190],
          ];
          const top = corners.map(([x, y]) => point(x ?? 0, y ?? 0, z)).join(" ");
          const edge = ([x1, y1]: number[], [x2, y2]: number[]) =>
            [point(x1 ?? 0, y1 ?? 0, z), point(x2 ?? 0, y2 ?? 0, z), point(x2 ?? 0, y2 ?? 0, z - 13), point(x1 ?? 0, y1 ?? 0, z - 13)].join(
              " ",
            );
          const normalize = (p: PointDto) => ({
            x: (p.x / (map?.planWidth ?? 1000)) * 580 - 290,
            y: (p.y / (map?.planHeight ?? 600)) * 360 - 180,
          });
          return (
            <g key={floor.floorId} className="gh-floor-slab" opacity={active ? 1 : 0.65}>
              <polygon
                points={edge(corners[2] ?? [], corners[3] ?? [])}
                fill={active ? "#2b3b47" : "#24313d"}
                stroke="#587280"
                strokeWidth=".7"
              />
              <polygon
                points={edge(corners[1] ?? [], corners[2] ?? [])}
                fill={active ? "#354b59" : "#1d303b"}
                stroke="#587280"
                strokeWidth=".7"
              />
              <polygon
                points={top}
                fill={active ? "#25343e" : "#182630"}
                stroke={active ? "#b3d185" : "#455b68"}
                strokeWidth="1.2"
                role="button"
                tabIndex={0}
                aria-label={`Select floor ${floor.name}`}
                onClick={() => onFloorSelect(floor.floorId)}
                onKeyDown={(e) => {
                  if (e.key === "Enter" || e.key === " ") {
                    e.preventDefault();
                    onFloorSelect(floor.floorId);
                  }
                }}
              />
              {map?.areas.map((area) => {
                const poly = area.polygon.map(normalize);
                const centroid = {
                  x: poly.reduce((n, p) => n + p.x, 0) / (poly.length || 1),
                  y: poly.reduce((n, p) => n + p.y, 0) / (poly.length || 1),
                };
                const center = project(centroid.x, centroid.y, z + 9);
                const meta = ACCESS_STATE_META[area.state];
                const Icon = meta.icon;
                const selected = selectedAreaId === area.areaId;
                return (
                  <g
                    key={area.areaId}
                    role="button"
                    tabIndex={active ? 0 : -1}
                    aria-label={`${area.name}: ${meta.label}`}
                    data-testid={`spatial-area-${area.code}`}
                    className={`gh-room ${selected ? "is-selected" : ""}`}
                    onClick={() => {
                      onFloorSelect(floor.floorId);
                      onAreaSelect(area);
                    }}
                    onKeyDown={(e) => {
                      if (e.key === "Enter" || e.key === " ") {
                        e.preventDefault();
                        onFloorSelect(floor.floorId);
                        onAreaSelect(area);
                      }
                    }}
                  >
                    <polygon points={poly.map((p) => point(p.x, p.y, z + 4)).join(" ")} fill="#344853" stroke="#52717c" strokeWidth="1" />
                    <polygon
                      className="gh-room-top"
                      points={poly.map((p) => point(p.x, p.y, z + 12)).join(" ")}
                      fill={active ? `var(${meta.colorVar})` : "#334b55"}
                      fillOpacity={active ? (selected ? 0.25 : 0.12) : 0.6}
                      stroke={selected ? "#d4ea8b" : "#6f8c8c"}
                      strokeWidth="1"
                    />
                    {active && meta.hatched && (
                      <polygon points={poly.map((p) => point(p.x, p.y, z + 12)).join(" ")} fill="url(#spatial-pending)" />
                    )}
                    {poly.map((p, i) => {
                      const next = poly[(i + 1) % poly.length];
                      if (!next) return null;
                      return (
                        <path
                          key={i}
                          d={[
                            `M ${point(p.x, p.y, z + 12)} L ${point(p.x, p.y, z + 35)}`,
                            `L ${point(next.x, next.y, z + 35)} L ${point(next.x, next.y, z + 12)}`,
                          ].join(" ")}
                          fill="none"
                          stroke={active ? "#819d8c" : "#49616c"}
                          strokeWidth="1.1"
                          opacity=".8"
                        />
                      );
                    })}
                    {active && labels && (
                      <>
                        <rect
                          x={center.x - 62}
                          y={center.y - 15}
                          width="124"
                          height="32"
                          rx="5"
                          fill="#111b24"
                          fillOpacity=".91"
                          stroke="#48616d"
                          strokeWidth=".6"
                        />
                        <Icon x={center.x - 53} y={center.y - 7} width="13" height="13" color={`var(${meta.colorVar})`} />
                        <text className="gh-room-label" x={center.x + 7} y={center.y + 2} textAnchor="middle">
                          {area.name.length > 18 ? area.name.slice(0, 16) + "…" : area.name}
                        </text>
                      </>
                    )}
                  </g>
                );
              })}
              {map?.areas.flatMap((area) =>
                area.doors.map((door) => {
                  const p = normalize(door);
                  const c = project(p.x, p.y, z + 14);
                  return (
                    <g key={door.doorId} aria-hidden="true">
                      <circle cx={c.x} cy={c.y} r="4" fill="#9bb7b2" stroke="#d4ea8b" strokeWidth="1.5" />
                      <title>{door.name}</title>
                    </g>
                  );
                }),
              )}
              <g
                role="button"
                tabIndex={0}
                aria-label={`View ${floor.name}`}
                onClick={() => onFloorSelect(floor.floorId)}
                onKeyDown={(e) => {
                  if (e.key === "Enter" || e.key === " ") {
                    e.preventDefault();
                    onFloorSelect(floor.floorId);
                  }
                }}
                style={{ cursor: "pointer" }}
              >
                <path d={`M ${point(300, -190, z)} h 37`} stroke="#52717c" strokeDasharray="3 3" />
                <text x="750" y={baseY + 27 - z} className="gh-floor-label" fontWeight={active ? 600 : 400}>
                  {floor.name}
                </text>
              </g>
            </g>
          );
        })}
        <text x="130" y="570" fontSize="8" letterSpacing="2" fill="#96acb2">
          SPATIAL SCHEMATIC / NOT TO SCALE
        </text>
        <g transform="translate(770 530)" stroke="#9ab7b4" fill="none">
          <path d="M0 22V-8 M-5 0L0-8 5 0" />
          <text x="-3" y="-17" stroke="none" fill="#9ab7b4" fontSize="9">
            N
          </text>
        </g>
      </g>
    </svg>
  );
}
