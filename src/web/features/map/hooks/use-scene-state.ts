import { useState } from "react";
import type { BuildingView } from "../scene/geometry";

export type CameraPreset = "overview" | "top" | "room";

/** View state of the 3D building scene: layers, view mode, orbit, camera preset, zoom and a focus token. */
export function useSceneState() {
  const [zoom, setZoom] = useState(1);
  const [view, setView] = useState<BuildingView>("isolated");
  const [cutaway, setCutaway] = useState(true);
  const [interiors, setInteriors] = useState(true);
  const [labels, setLabels] = useState(true);
  const [xray, setXray] = useState(false);
  const [autoRotate, setAutoRotate] = useState(false);
  const [showSettings, setShowSettings] = useState(false);
  const [cameraPreset, setCameraPreset] = useState<CameraPreset>("overview");
  const [focusToken, setFocusToken] = useState(0);

  const bumpFocus = () => setFocusToken((n) => n + 1);

  function focusRoom() {
    setView("isolated");
    setCameraPreset("room");
    bumpFocus();
    setAutoRotate(false);
  }

  function resetView() {
    setView("isolated");
    setCameraPreset("overview");
    setZoom(1);
    bumpFocus();
    setAutoRotate(false);
  }

  function topView() {
    setCameraPreset("top");
    bumpFocus();
    setAutoRotate(false);
  }

  function pickView(next: BuildingView) {
    setView(next);
    setCameraPreset("overview");
  }

  return {
    zoom,
    setZoom,
    view,
    cutaway,
    setCutaway,
    interiors,
    setInteriors,
    labels,
    setLabels,
    xray,
    setXray,
    autoRotate,
    setAutoRotate,
    showSettings,
    setShowSettings,
    cameraPreset,
    focusToken,
    bumpFocus,
    focusRoom,
    resetView,
    topView,
    pickView,
  };
}

export type SceneState = ReturnType<typeof useSceneState>;
