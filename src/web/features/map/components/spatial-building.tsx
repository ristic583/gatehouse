"use client";
import { Component, lazy, Suspense, useEffect, useRef, useState, type ReactNode } from "react";
import type { AreaAccessDto, FloorAccessMapDto, FloorSummaryDto } from "@/lib/api/types";
import type { BuildingView } from "../scene/geometry";
import { SpatialBuildingFallback } from "./spatial-building-fallback";

const BuildingScene = lazy(() => import("../scene/building-scene").then((module) => ({ default: module.BuildingScene })));
export interface SpatialBuildingProps {
  floors: FloorSummaryDto[];
  maps: FloorAccessMapDto[];
  activeFloorId: string;
  selectedAreaId?: string;
  onFloorSelect: (id: string) => void;
  onAreaSelect: (area: AreaAccessDto) => void;
  zoom?: number;
  exploded?: boolean;
  view?: BuildingView;
  cutaway?: boolean;
  interiors?: boolean;
  labels?: boolean;
  xray?: boolean;
  autoRotate?: boolean;
  reducedMotion?: boolean;
  cameraPreset?: "overview" | "top" | "room";
  focusToken?: number;
  visible?: boolean;
  onContextLost?: () => void;
}
class SceneBoundary extends Component<{ children: ReactNode; fallback: ReactNode }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() {
    return { failed: true };
  }
  render() {
    return this.state.failed ? this.props.fallback : this.props.children;
  }
}
/** WebGL is progressive enhancement: keyboard/2D operation and non-GPU fallback never depend on it. */
export function SpatialBuilding(props: SpatialBuildingProps) {
  const [gpu, setGpu] = useState<"checking" | "ready" | "unavailable">("checking");
  const [reduced, setReduced] = useState(false);
  const [visible, setVisible] = useState(true);
  const host = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const frame = requestAnimationFrame(() => {
      const preference = window.matchMedia?.("(prefers-reduced-motion: reduce)");
      setReduced(preference?.matches ?? false);
      if (typeof window.WebGL2RenderingContext === "undefined") {
        setGpu("unavailable");
        return;
      }
      try {
        const canvas = document.createElement("canvas");
        const gl = canvas.getContext("webgl2");
        if (gl) {
          gl.getExtension("WEBGL_lose_context")?.loseContext();
          setGpu("ready");
        } else setGpu("unavailable");
      } catch {
        setGpu("unavailable");
      }
    });
    const motion = window.matchMedia?.("(prefers-reduced-motion: reduce)");
    const change = (e: MediaQueryListEvent) => setReduced(e.matches);
    motion?.addEventListener("change", change);
    let intersecting = true;
    const visibility = () => setVisible(intersecting && !document.hidden);
    document.addEventListener("visibilitychange", visibility);
    const observer =
      typeof IntersectionObserver !== "undefined"
        ? new IntersectionObserver(([entry]) => {
            intersecting = !!entry?.isIntersecting;
            visibility();
          })
        : null;
    if (host.current) observer?.observe(host.current);
    return () => {
      cancelAnimationFrame(frame);
      motion?.removeEventListener("change", change);
      document.removeEventListener("visibilitychange", visibility);
      observer?.disconnect();
    };
  }, []);
  const fallback = (
    <div className="gh-gpu-fallback">
      <SpatialBuildingFallback {...props} />
      <span className="gh-render-notice">
        {gpu === "checking" ? "Preparing 3D renderer…" : "Schematic mode · 3D rendering unavailable"}
      </span>
    </div>
  );
  return (
    <div ref={host} className="gh-webgl-stage" data-renderer={gpu}>
      {gpu === "ready" ? (
        <SceneBoundary fallback={fallback}>
          <Suspense fallback={fallback}>
            <BuildingScene {...props} reducedMotion={reduced} visible={visible} onContextLost={() => setGpu("unavailable")} />
          </Suspense>
        </SceneBoundary>
      ) : (
        fallback
      )}
    </div>
  );
}
