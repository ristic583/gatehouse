"use client";

import { Canvas, useFrame, useThree, type ThreeEvent } from "@react-three/fiber";
import { Edges, Environment, Html, Lightformer, OrbitControls } from "@react-three/drei";
import { useEffect, useMemo, useRef, useState } from "react";
import { Group, MathUtils, Shape, Vector2, Vector3, type PerspectiveCamera } from "three";
import type { OrbitControls as OrbitControlsImpl } from "three-stdlib";
import type { AreaAccessDto, FloorAccessMapDto } from "@/lib/api/types";
import { ACCESS_STATE_META } from "../access-state";
import { floorElevation, roomGeometry, siteScale, type BuildingView } from "./geometry";
import { RoomInterior } from "./room-interior";
import type { SpatialBuildingProps } from "../components/spatial-building";

const ACCENT = "#d4ea8b";
const EXTRUDE = { depth: 0.08, bevelEnabled: false, steps: 1 };

function Room({
  area,
  map,
  scale,
  selected,
  active,
  cutaway,
  interiors,
  labels,
  xray,
  onSelect,
}: {
  area: AreaAccessDto;
  map: FloorAccessMapDto;
  scale: number;
  selected: boolean;
  active: boolean;
  cutaway: boolean;
  interiors: boolean;
  labels: boolean;
  xray: boolean;
  onSelect: () => void;
}) {
  const geo = useMemo(() => roomGeometry(area, map, scale), [area, map, scale]);
  const shape = useMemo(() => new Shape(geo.points.map((p) => new Vector2(p.x, -p.z))), [geo]);
  const height = cutaway ? 0.47 : 1.25;
  const color = ACCESS_STATE_META[area.state].colorHex;
  const StateIcon = ACCESS_STATE_META[area.state].icon;
  const act = (e: ThreeEvent<MouseEvent>) => {
    e.stopPropagation();
    onSelect();
  };
  if (geo.points.length < 3) return null;
  return (
    <group onClick={act}>
      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0.11, 0]} receiveShadow>
        <extrudeGeometry args={[shape, EXTRUDE]} />
        <meshStandardMaterial
          color={selected ? "#34463d" : active ? "#293641" : "#19242e"}
          metalness={0.35}
          roughness={0.55}
          emissive={color}
          emissiveIntensity={selected ? 0.2 : 0.055}
          transparent={xray || !active}
          opacity={xray ? 0.24 : active ? 1 : 0.14}
          depthWrite={active && !xray}
        />
      </mesh>
      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0.195, 0]}>
        <shapeGeometry args={[shape]} />
        <meshBasicMaterial color={color} transparent opacity={active ? 0.11 : 0.025} depthWrite={false} />
      </mesh>
      {geo.walls.map((wall, i) => (
        <group key={i} position={[wall.x, 0, wall.z]} rotation={[0, wall.rotation, 0]}>
          <mesh position={[0, height / 2 + 0.2, 0]} castShadow={active} receiveShadow={active}>
            <boxGeometry args={[wall.length, height, 0.075]} />
            <meshStandardMaterial
              color={active ? "#60717b" : "#34434e"}
              metalness={0.5}
              roughness={0.42}
              transparent
              opacity={xray ? 0.22 : active ? 0.78 : 0.16}
              depthWrite={active && !xray}
            />
            <Edges color={selected ? "#a8c28a" : "#8297a0"} transparent opacity={active ? 0.5 : 0.2} />
          </mesh>
          <mesh position={[0, height + 0.21, 0]}>
            <boxGeometry args={[wall.length, 0.025, 0.085]} />
            <meshStandardMaterial
              color={selected ? ACCENT : "#6a8a92"}
              emissive={selected ? ACCENT : "#436a78"}
              emissiveIntensity={selected ? 1.3 : 0.2}
              transparent
              opacity={xray ? 0.35 : active ? 1 : 0.25}
            />
          </mesh>
          {active && (
            <mesh position={[0, 0.23, 0]}>
              <boxGeometry args={[wall.length, 0.035, 0.09]} />
              <meshStandardMaterial color="#202c34" metalness={0.6} roughness={0.4} />
            </mesh>
          )}
        </group>
      ))}
      {geo.doors.map(({ door, x, z, rotation, width }) => (
        <group key={door.doorId} position={[x, 0.2, z]} rotation={[0, rotation, 0]}>
          <mesh position={[0, 0.02, 0]}>
            <boxGeometry args={[width, 0.04, 0.24]} />
            <meshStandardMaterial color={color} emissive={color} emissiveIntensity={0.8} />
          </mesh>
          {[-1, 1].map((side) => (
            <mesh key={side} position={[(side * width) / 2, 0.4, 0]}>
              <boxGeometry args={[0.045, 0.8, 0.09]} />
              <meshStandardMaterial color="#8fa4a8" metalness={0.7} roughness={0.3} />
            </mesh>
          ))}
          <mesh position={[0, 0.8, 0]}>
            <boxGeometry args={[width + 0.045, 0.045, 0.09]} />
            <meshStandardMaterial color="#8fa4a8" metalness={0.7} />
          </mesh>
          <mesh position={[0, 0.37, 0.015]}>
            <boxGeometry args={[width - 0.06, 0.7, 0.028]} />
            <meshPhysicalMaterial color="#84bfc8" metalness={0.15} roughness={0.15} transparent opacity={0.16} depthWrite={false} />
          </mesh>
          {/* A static door symbol locates the recorded opening; it does not claim the physical door is open/locked. */}
        </group>
      ))}
      {active && interiors && !xray && <RoomInterior geometry={geo} name={area.name} />}
      {active && labels && (
        <Html
          position={[geo.center.x, 0.95, geo.center.z]}
          center
          distanceFactor={13}
          zIndexRange={[20, 5]}
          style={{ pointerEvents: "none" }}
        >
          <button
            className={`gh-3d-room-label ${selected ? "selected" : ""}`}
            onClick={onSelect}
            style={{ pointerEvents: "auto" }}
            aria-label={`${area.name}: ${ACCESS_STATE_META[area.state].label}`}
          >
            <StateIcon size={12} style={{ color }} />
            <span>
              {area.name}
              <small>{ACCESS_STATE_META[area.state].label}</small>
            </span>
          </button>
        </Html>
      )}
    </group>
  );
}

function FloorAssembly({
  map,
  index,
  selectedIndex,
  scale,
  props,
}: {
  map: FloorAccessMapDto;
  index: number;
  selectedIndex: number;
  scale: number;
  props: SpatialBuildingProps;
}) {
  const ref = useRef<Group>(null);
  const active = map.floorId === props.activeFloorId;
  const view = props.view ?? (props.exploded ? "exploded" : "assembled");
  const y = floorElevation(index, selectedIndex, view);
  const [initialY] = useState(y);
  const width = map.planWidth * scale + 0.3;
  const depth = map.planHeight * scale + 0.3;
  useFrame((_, dt) => {
    if (ref.current) {
      ref.current.position.y = props.reducedMotion ? y : MathUtils.damp(ref.current.position.y, y, 5, Math.min(dt, 0.1));
    }
  });
  return (
    <group ref={ref} position={[0, initialY, 0]} visible={view !== "isolated" || active}>
      <mesh
        position={[0, -0.02, 0]}
        receiveShadow={active}
        castShadow={active}
        onClick={(e) => {
          e.stopPropagation();
          props.onFloorSelect(map.floorId);
        }}
      >
        <boxGeometry args={[width, 0.18, depth]} />
        <meshStandardMaterial
          color={active ? "#25313c" : "#18232d"}
          metalness={0.65}
          roughness={0.48}
          transparent={props.xray || !active}
          opacity={props.xray ? 0.2 : active ? 1 : 0.18}
          depthWrite={active && !props.xray}
        />
        <Edges color={active ? "#a9c78c" : "#3c545f"} />
      </mesh>
      {[-1, 1].map((side) => (
        <group key={side}>
          <mesh position={[0, -0.07, (side * depth) / 2]}>
            <boxGeometry args={[width, 0.025, 0.025]} />
            <meshStandardMaterial
              color={active ? ACCENT : "#4b7580"}
              emissive={active ? ACCENT : "#325b69"}
              emissiveIntensity={active ? 1.4 : 0.35}
            />
          </mesh>
          <mesh position={[(side * width) / 2, -0.07, 0]}>
            <boxGeometry args={[0.025, 0.025, depth]} />
            <meshStandardMaterial
              color={active ? ACCENT : "#4b7580"}
              emissive={active ? ACCENT : "#325b69"}
              emissiveIntensity={active ? 1.4 : 0.35}
            />
          </mesh>
        </group>
      ))}
      {map.areas.map((area) => (
        <Room
          key={area.areaId}
          area={area}
          map={map}
          scale={scale}
          selected={active && props.selectedAreaId === area.areaId}
          active={active}
          cutaway={props.cutaway ?? true}
          interiors={props.interiors ?? false}
          labels={props.labels ?? true}
          xray={props.xray ?? false}
          onSelect={() => {
            props.onFloorSelect(map.floorId);
            props.onAreaSelect(area);
          }}
        />
      ))}
    </group>
  );
}

function applyCameraZoom(camera: PerspectiveCamera, zoom: number) {
  camera.zoom = zoom;
  camera.updateProjectionMatrix();
}

function CameraDirector({
  props,
  scale,
  selectedIndex,
  controls,
}: {
  props: SpatialBuildingProps;
  scale: number;
  selectedIndex: number;
  controls: React.RefObject<OrbitControlsImpl | null>;
}) {
  const { camera, invalidate } = useThree();
  const animation = useRef<{ from: Vector3; to: Vector3; fromTarget: Vector3; target: Vector3; t: number } | null>(null);
  const map = props.maps.find((m) => m.floorId === props.activeFloorId);
  const room = map?.areas.find((a) => a.areaId === props.selectedAreaId);
  const center = useMemo(() => (map && room ? roomGeometry(room, map, scale).center : { x: 0, z: 0 }), [map, room, scale]);
  const focusX = props.cameraPreset === "room" ? center.x : 0;
  const focusZ = props.cameraPreset === "room" ? center.z : 0;
  const view: BuildingView = props.view ?? "exploded";
  const height = floorElevation(selectedIndex, selectedIndex, view);
  useEffect(() => {
    const control = controls.current;
    if (!control) return;
    const focusing = props.cameraPreset === "room";
    const target = new Vector3(
      focusX,
      view === "isolated" ? 0.45 : focusing ? height + 0.5 : ((props.floors.length - 1) * (view === "exploded" ? 3.25 : 1.7)) / 2,
      focusZ,
    );
    const distance = focusing ? 6 : Math.max(15, props.floors.length * 2.7);
    const offset =
      props.cameraPreset === "top"
        ? new Vector3(0.01, distance * 1.4, 0.01)
        : new Vector3(distance * 0.85, distance * 0.68, distance * 0.95);
    animation.current = { from: camera.position.clone(), to: target.clone().add(offset), fromTarget: control.target.clone(), target, t: 0 };
    invalidate();
  }, [
    camera,
    controls,
    focusX,
    focusZ,
    height,
    invalidate,
    props.activeFloorId,
    props.cameraPreset,
    props.focusToken,
    props.floors.length,
    view,
  ]);
  useEffect(() => {
    const c = controls.current;
    if (!c) return;
    const cancel = () => {
      animation.current = null;
    };
    c.addEventListener("start", cancel);
    return () => c.removeEventListener("start", cancel);
  }, [controls]);
  useEffect(() => {
    applyCameraZoom(camera as PerspectiveCamera, props.zoom ?? 1);
    invalidate();
  }, [camera, invalidate, props.zoom]);
  useFrame((_, dt) => {
    const a = animation.current;
    const c = controls.current;
    if (!a || !c) return;
    a.t = props.reducedMotion ? 1 : Math.min(1, a.t + Math.min(dt, 0.1) / 1.1);
    const ease = 1 - Math.pow(1 - a.t, 3);
    camera.position.lerpVectors(a.from, a.to, ease);
    c.target.lerpVectors(a.fromTarget, a.target, ease);
    c.update();
    if (a.t === 1) animation.current = null;
  });
  return null;
}
function Scene({ props }: { props: SpatialBuildingProps }) {
  const scale = siteScale(props.maps);
  const floors = [...props.floors].sort((a, b) => a.level - b.level);
  const selectedIndex = Math.max(
    0,
    floors.findIndex((f) => f.floorId === props.activeFloorId),
  );
  const controls = useRef<OrbitControlsImpl>(null);
  const width = Math.max(1, ...props.maps.map((m) => m.planWidth * scale)) + 0.9;
  const depth = Math.max(1, ...props.maps.map((m) => m.planHeight * scale)) + 0.9;
  return (
    <>
      <color attach="background" args={["#0b1119"]} />
      <fog attach="fog" args={["#0b1119", 32, 65]} />
      <ambientLight intensity={0.55} />
      <hemisphereLight args={["#c6d9eb", "#182524", 1.3]} />
      <directionalLight
        position={[6, 17, 9]}
        intensity={3}
        color="#e4e9d3"
        castShadow
        shadow-mapSize={[1024, 1024]}
        shadow-camera-left={-12}
        shadow-camera-right={12}
        shadow-camera-top={12}
        shadow-camera-bottom={-12}
        shadow-bias={-0.001}
      />
      <directionalLight position={[-10, 7, -8]} intensity={2.5} color="#779fd5" />
      <Environment resolution={128} frames={1}>
        <Lightformer form="rect" intensity={3} position={[0, 12, 0]} rotation-x={Math.PI / 2} scale={[15, 10, 1]} />
        <Lightformer form="rect" intensity={2} position={[-10, 3, 0]} rotation-y={Math.PI / 2} scale={[8, 8, 1]} color="#9bb5e1" />
      </Environment>
      <gridHelper args={[50, 50, "#284149", "#17242d"]} position={[0, -0.34, 0]} />
      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, -0.35, 0]} receiveShadow>
        <planeGeometry args={[100, 100]} />
        <meshStandardMaterial color="#0b1119" metalness={0.35} roughness={0.65} />
      </mesh>
      <mesh position={[0, -0.26, 0]}>
        <boxGeometry args={[width, 0.25, depth]} />
        <meshStandardMaterial color="#15202a" metalness={0.6} roughness={0.45} />
        <Edges color="#385362" />
      </mesh>
      {floors.map((f, i) => {
        const map = props.maps.find((m) => m.floorId === f.floorId);
        return map ? <FloorAssembly key={f.floorId} map={map} index={i} selectedIndex={selectedIndex} scale={scale} props={props} /> : null;
      })}
      <OrbitControls
        ref={controls}
        makeDefault
        enableDamping
        dampingFactor={0.07}
        autoRotate={props.autoRotate && !props.reducedMotion}
        autoRotateSpeed={0.3}
        minDistance={4}
        maxDistance={45}
        maxPolarAngle={Math.PI * 0.47}
        minPolarAngle={0.08}
        enablePan
        screenSpacePanning
      />
      <CameraDirector props={props} scale={scale} selectedIndex={selectedIndex} controls={controls} />
    </>
  );
}
export function BuildingScene(props: SpatialBuildingProps) {
  return (
    <Canvas
      shadows
      dpr={[1, 1.5]}
      camera={{ position: [17, 15, 19], fov: 38, near: 0.1, far: 100 }}
      gl={{ antialias: true, alpha: false, powerPreference: "high-performance" }}
      frameloop={props.visible === false ? "never" : "always"}
      onCreated={({ gl }) => {
        gl.setClearColor("#0b1119");
        gl.domElement.addEventListener("webglcontextlost", props.onContextLost ?? (() => {}));
      }}
      aria-label="Interactive 3D building. Drag to orbit and scroll to zoom; use the adjacent floor and room lists for keyboard navigation."
    >
      <Scene props={props} />
    </Canvas>
  );
}
