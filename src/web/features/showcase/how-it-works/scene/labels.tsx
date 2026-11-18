"use client";

import { useFrame, useThree } from "@react-three/fiber";
import { type RefObject, useMemo } from "react";
import * as THREE from "three";
import { CLOUD_BLOCKS, PLACE_LABELS } from "./cloud";
import { NODES, type NodeId, type Vec3 } from "./story";

interface Anchor {
  id: NodeId;
  title: string;
  detail: string;
  at: Vec3;
  cloud: boolean;
}

const ANCHORS: readonly Anchor[] = [
  ...CLOUD_BLOCKS.map((b) => ({
    id: b.id,
    title: b.title,
    detail: b.detail,
    at: [NODES[b.id][0], NODES[b.id][1] + 0.25, NODES[b.id][2]] as Vec3,
    cloud: true,
  })),
  ...PLACE_LABELS.map((l) => ({
    id: l.id,
    title: l.title,
    detail: l.detail,
    at: [NODES[l.id][0] + l.offset[0], NODES[l.id][1] + l.offset[1], NODES[l.id][2] + l.offset[2]] as Vec3,
    cloud: false,
  })),
];

export type LabelElements = RefObject<Map<NodeId, HTMLDivElement>>;

/** Inside the Canvas: projects every anchor to screen space each frame and moves its DOM label (no React render). */
export function LabelProjector({ elements }: { elements: LabelElements }) {
  const { camera, size } = useThree();
  const points = useMemo(() => ANCHORS.map((a) => ({ id: a.id, v: new THREE.Vector3(...a.at) })), []);
  const tmp = useMemo(() => new THREE.Vector3(), []);
  useFrame(() => {
    for (const p of points) {
      const el = elements.current.get(p.id);
      if (el === undefined) {
        continue;
      }
      tmp.copy(p.v).project(camera);
      const visible = tmp.z < 1 && Math.abs(tmp.x) < 1.1 && Math.abs(tmp.y) < 1.1;
      el.style.visibility = visible ? "" : "hidden";
      el.style.transform = `translate(${((tmp.x + 1) / 2) * size.width}px, ${((1 - tmp.y) / 2) * size.height}px) translate(-50%, -100%)`;
    }
  });
  return null;
}

/** DOM labels over the canvas. Cloud services always show their name; people and devices only while they take part. */
export function LabelLayer({ elements, active, dimmed }: { elements: LabelElements; active: readonly NodeId[]; dimmed: boolean }) {
  return (
    <div className="pointer-events-none absolute inset-0 overflow-hidden" aria-hidden>
      {ANCHORS.map((a) => {
        const on = active.includes(a.id);
        const faded = a.id === "keycloak" || (dimmed && a.cloud);
        const shown = a.cloud || on;
        return (
          <div
            key={a.id}
            ref={(el) => {
              if (el === null) {
                elements.current.delete(a.id);
              } else {
                elements.current.set(a.id, el);
              }
            }}
            className="absolute left-0 top-0 will-change-transform"
            style={{ visibility: "hidden" }}
          >
            <div
              className={
                "mb-1 whitespace-nowrap rounded-md border px-2 py-0.5 text-[11px] leading-tight shadow-sm transition-all duration-300 " +
                `${on ? "border-sky-500 bg-white text-slate-900" : "border-slate-300 bg-white/80 text-slate-600"} ` +
                `${faded ? "opacity-40" : ""} ${shown ? "" : "scale-90 opacity-0"}`
              }
            >
              <div className="font-semibold">{a.title}</div>
              {on || a.id === "keycloak" ? <div className="font-mono text-[10px] text-slate-500">{a.detail}</div> : null}
            </div>
          </div>
        );
      })}
    </div>
  );
}
