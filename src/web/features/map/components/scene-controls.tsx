"use client";

import { Building2, Layers3, LayoutGrid, Minus, Pause, Play, Plus, RotateCcw, SlidersHorizontal, SquareStack } from "lucide-react";

import type { SceneState } from "../hooks/use-scene-state";

/** 3D scene overlay: layer settings, view tabs, orbit, top view, reset and zoom. */
export function SceneControls({ scene }: { scene: SceneState }) {
  return (
    <>
      <div className="gh-scene-settings-toggle">
        <button
          className="gh-scene-icon"
          aria-label="Scene settings"
          aria-expanded={scene.showSettings}
          onClick={() => scene.setShowSettings(!scene.showSettings)}
        >
          <SlidersHorizontal size={16} />
        </button>
      </div>
      {scene.showSettings && (
        <div className="gh-scene-settings">
          <header>
            MODEL LAYERS{" "}
            <button aria-label="Close scene settings" onClick={() => scene.setShowSettings(false)}>
              ×
            </button>
          </header>
          {[
            { label: "Cutaway walls", value: scene.cutaway, set: scene.setCutaway },
            { label: "Room labels", value: scene.labels, set: scene.setLabels },
            { label: "Illustrative interiors", value: scene.interiors, set: scene.setInteriors },
            { label: "X-ray surfaces", value: scene.xray, set: scene.setXray },
          ].map((s) => (
            <button key={s.label} role="switch" aria-checked={s.value} onClick={() => s.set(!s.value)}>
              <span>{s.label}</span>
              <i />
            </button>
          ))}
          <p>Furniture and wall heights are illustrative. Room boundaries and door locations come from your floor plan.</p>
        </div>
      )}
      <div className="gh-scene-dock">
        <div className="gh-scene-view-tabs">
          {(
            [
              { id: "assembled", label: "Building", icon: Building2 },
              { id: "exploded", label: "Exploded", icon: Layers3 },
              { id: "isolated", label: "Isolate floor", icon: SquareStack },
            ] as const
          ).map((v) => (
            <button key={v.id} aria-pressed={scene.view === v.id} onClick={() => scene.pickView(v.id)}>
              <v.icon size={14} />
              <span>{v.label}</span>
            </button>
          ))}
        </div>
        <span className="gh-dock-divider" />
        <button
          className="gh-scene-icon"
          aria-label={scene.autoRotate ? "Pause orbit" : "Auto orbit"}
          aria-pressed={scene.autoRotate}
          onClick={() => scene.setAutoRotate(!scene.autoRotate)}
        >
          {scene.autoRotate ? <Pause size={14} /> : <Play size={14} />}
        </button>
        <button className="gh-scene-icon" aria-label="Top view" onClick={scene.topView}>
          <LayoutGrid size={14} />
        </button>
        <button className="gh-scene-icon" aria-label="Reset view" onClick={scene.resetView}>
          <RotateCcw size={14} />
        </button>
      </div>
      <div className="gh-scene.zoom-tools">
        <button aria-label="Zoom in" disabled={scene.zoom >= 1.6} onClick={() => scene.setZoom((z) => Math.min(1.6, z + 0.15))}>
          <Plus size={15} />
        </button>
        <span>{Math.round(scene.zoom * 100)}%</span>
        <button aria-label="Zoom out" disabled={scene.zoom <= 0.65} onClick={() => scene.setZoom((z) => Math.max(0.65, z - 0.15))}>
          <Minus size={15} />
        </button>
      </div>
    </>
  );
}
