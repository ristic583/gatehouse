"use client";

import { MeshReflectorMaterial, RoundedBox } from "@react-three/drei";
import { type ReactNode, type RefObject, useMemo } from "react";
import * as THREE from "three";
import { Chair, Controller, Desk, Door, Figure, type Outfit, Rack, Reader } from "./props";
import { LINKS, NODES, type Person, type ReaderLed, type Vec3 } from "./story";
import { tileTexture } from "./textures";

/** Storey height; walls of the near rooms are cut at SECTION so the camera sees in (architectural section view). */
const H = 2.7;
const SECTION = 1.05;
const T = 0.12;
const FAR_Z = -1.26;
const NEAR_Z = 1.26;
const BACK_Z = -5.2;
const FRONT_Z = 5.2;
const X0 = -8;
const X1 = 8;
const FAR_DOORS = [-4.5, 0, 4.5];
const NEAR_OPENINGS = [-2.6, 2.6];

const WALL = { color: "#ece8e1", roughness: 0.92 } as const;

function Wall({
  from,
  to,
  z,
  x,
  height = H,
  cut = false,
}: {
  from: number;
  to: number;
  z?: number;
  x?: number;
  height?: number;
  cut?: boolean;
}) {
  const len = to - from;
  const along = z !== undefined;
  const pos: Vec3 = along ? [(from + to) / 2, height / 2, z] : [x!, height / 2, (from + to) / 2];
  const size: Vec3 = along ? [len, height, T] : [T, height, len];
  return (
    <group>
      <mesh position={pos} castShadow receiveShadow>
        <boxGeometry args={size} />
        <meshStandardMaterial {...WALL} />
      </mesh>
      {cut ? (
        <mesh position={[pos[0], height + 0.002, pos[2]]}>
          <boxGeometry args={[size[0] + 0.001, 0.004, size[2] + 0.001]} />
          <meshStandardMaterial color="#2b3138" roughness={1} />
        </mesh>
      ) : null}
      {/* skirting */}
      <mesh position={[pos[0], 0.05, pos[2]]}>
        <boxGeometry args={[size[0] + (along ? 0 : 0.02), 0.1, size[2] + (along ? 0.02 : 0)]} />
        <meshStandardMaterial color="#5b6168" roughness={0.6} />
      </mesh>
    </group>
  );
}

/** Wall along x with door-sized openings (centres `holes`, width `w`), plus the lintel above each. */
function WallWithOpenings({
  z,
  holes,
  w,
  height,
  cut,
  lintel,
}: {
  z: number;
  holes: number[];
  w: number;
  height: number;
  cut: boolean;
  lintel: boolean;
}) {
  const edges = [X0, ...holes.flatMap((h) => [h - w / 2, h + w / 2]), X1];
  const segments: [number, number][] = [];
  for (let i = 0; i < edges.length; i += 2) {
    segments.push([edges[i]!, edges[i + 1]!]);
  }
  return (
    <group>
      {segments.map(([a, b]) => (
        <Wall key={a} from={a} to={b} z={z} height={height} cut={cut} />
      ))}
      {lintel
        ? holes.map((h) => (
            <mesh key={h} position={[h, (2.16 + height) / 2, z]} castShadow receiveShadow>
              <boxGeometry args={[w, height - 2.16, T]} />
              <meshStandardMaterial {...WALL} />
            </mesh>
          ))
        : null}
    </group>
  );
}

function Floor({ x, z, w, d, material }: { x: number; z: number; w: number; d: number; material: ReactNode }) {
  return (
    <mesh position={[x, 0, z]} rotation={[-Math.PI / 2, 0, 0]} receiveShadow>
      <planeGeometry args={[w, d]} />
      {material}
    </mesh>
  );
}

function Tube({ path, color, radius }: { path: readonly Vec3[]; color: string; radius: number }) {
  const geometry = useMemo(() => {
    const curve = new THREE.CurvePath<THREE.Vector3>();
    path.slice(1).forEach((p, i) => curve.add(new THREE.LineCurve3(new THREE.Vector3(...path[i]!), new THREE.Vector3(...p))));
    return new THREE.TubeGeometry(curve, 128, radius, 6, false);
  }, [path, radius]);
  return (
    <mesh geometry={geometry} castShadow>
      <meshStandardMaterial color={color} roughness={0.6} />
    </mesh>
  );
}

const OUTFITS: Record<string, Outfit> = {
  anna: {
    skin: "#e8bfa0",
    hair: "#6b4226",
    top: "#2f6db3",
    bottom: "#2b2f36",
    shoes: "#1a1a1a",
    longHair: true,
  },
  manager: {
    skin: "#d6a684",
    hair: "#2a2522",
    top: "#e5e7eb",
    bottom: "#33404f",
    shoes: "#3a2a1f",
  },
  owner: {
    skin: "#f0caa8",
    hair: "#c9a26a",
    top: "#7c4d8f",
    bottom: "#24282e",
    shoes: "#111",
    longHair: true,
  },
  staff: {
    skin: "#b7835f",
    hair: "#1c1a19",
    top: "#1f2937",
    bottom: "#1f2937",
    shoes: "#111",
  },
};

export interface BuildingProps {
  anna: RefObject<Person>;
  doorOpen: RefObject<number>;
  controllerBusy: RefObject<boolean>;
  agentBusy: RefObject<boolean>;
  readerLed: ReaderLed;
}

/** One floor of an office building: corridor with three PACS doors, an open office, a security desk, a lab and a comms room. */
export function Building({ anna, doorOpen, controllerBusy, agentBusy, readerLed }: BuildingProps) {
  const corridorTiles = useMemo(() => {
    const t = tileTexture("#c9ccc8", "#b3b7b3", 1);
    t.repeat.set(26, 4);
    return t;
  }, []);
  const raised = useMemo(() => {
    const t = tileTexture("#d3d6da", "#9aa0a6", 1);
    t.repeat.set(9, 6);
    return t;
  }, []);
  const seated = (x: number) => ({
    pose: "sit" as const,
    x,
    z: 3.62,
    facing: Math.PI,
    phase: 0,
  });

  return (
    <group>
      {/* ground slab */}
      <mesh position={[0, -0.06, 0]} receiveShadow>
        <boxGeometry args={[X1 - X0 + 0.6, 0.12, FRONT_Z - BACK_Z + 0.6]} />
        <meshStandardMaterial color="#8d939a" roughness={0.9} />
      </mesh>

      {/* floors: polished vinyl corridor, carpet in offices, epoxy in the lab, raised floor in the comms room */}
      <Floor
        x={0}
        z={0}
        w={X1 - X0}
        d={NEAR_Z - FAR_Z}
        material={
          <MeshReflectorMaterial
            map={corridorTiles}
            mirror={0.35}
            blur={[300, 80]}
            resolution={512}
            mixBlur={1}
            mixStrength={2.2}
            roughness={0.75}
            metalness={0.05}
            color="#cfd2ce"
          />
        }
      />
      <Floor x={-4} z={3.23} w={8} d={3.94} material={<meshStandardMaterial color="#5d6670" roughness={1} />} />
      <Floor x={4} z={3.23} w={8} d={3.94} material={<meshStandardMaterial color="#4f5a63" roughness={1} />} />
      <Floor x={-5.12} z={-3.23} w={5.75} d={3.94} material={<meshStandardMaterial color="#6a6f76" roughness={1} />} />
      <Floor x={0} z={-3.23} w={4.5} d={3.94} material={<meshStandardMaterial color="#dfe4e2" roughness={0.35} metalness={0.05} />} />
      <Floor x={5.12} z={-3.23} w={5.75} d={3.94} material={<meshStandardMaterial map={raised} roughness={0.5} />} />

      {/* walls */}
      <WallWithOpenings z={FAR_Z} holes={FAR_DOORS} w={1.12} height={H} cut={false} lintel />
      <WallWithOpenings z={NEAR_Z} holes={NEAR_OPENINGS} w={1.0} height={SECTION} cut lintel={false} />
      <Wall from={X0} to={X1} z={BACK_Z} />
      <Wall from={X0} to={X1} z={FRONT_Z} height={SECTION} cut />
      <Wall from={BACK_Z} to={FAR_Z} x={X0} />
      <Wall from={BACK_Z} to={FAR_Z} x={X1} />
      <Wall from={FAR_Z} to={FRONT_Z} x={X0} height={SECTION} cut />
      <Wall from={FAR_Z} to={FRONT_Z} x={X1} height={SECTION} cut />
      <Wall from={BACK_Z} to={FAR_Z} x={-2.25} />
      <Wall from={BACK_Z} to={FAR_Z} x={2.25} />
      <Wall from={NEAR_Z} to={FRONT_Z} x={0} height={SECTION} cut />

      {/* doors and readers on the corridor side (latch side, 1.1 m) */}
      <Door x={-4.5} wallZ={FAR_Z} number="2.01" name="Meeting" restricted={false} />
      <Door x={0} wallZ={FAR_Z} number="2.04" name="Lab" restricted open={doorOpen} />
      <Door x={4.5} wallZ={FAR_Z} number="2.06" name="Comms" restricted />
      <Reader position={[-3.78, 1.12, -1.185]} led="idle" />
      <Reader position={[NODES.labReader[0], NODES.labReader[1], -1.185]} led={readerLed} />
      <Reader position={[5.22, 1.12, -1.185]} led="idle" />

      {/* cable tray along the corridor wall, reader cables and the controller's network cable to the rack */}
      <mesh position={[-0.3, 2.58, -1.12]} castShadow>
        <boxGeometry args={[7.4, 0.04, 0.12]} />
        <meshStandardMaterial color="#9ca3ab" metalness={0.6} roughness={0.4} />
      </mesh>
      <Tube path={LINKS.readerController.path} color="#3b3f45" radius={0.01} />
      <Tube
        path={[
          [-3.78, 1.12, -1.1],
          [-3.78, 2.55, -1.1],
          [0.72, 2.55, -1.08],
        ]}
        color="#3b3f45"
        radius={0.01}
      />
      <Tube
        path={[
          [5.22, 1.12, -1.1],
          [5.22, 2.55, -1.1],
          [3.4, 2.55, -1.08],
        ]}
        color="#3b3f45"
        radius={0.01}
      />
      <Tube path={LINKS.agentController.path} color="#2563eb" radius={0.008} />

      {/* open office: Anna, her manager and the lab's area owner */}
      {[-5.6, -3.6, -1.6].map((x) => (
        <group key={x}>
          <Desk x={x} z={3.05} />
          <Chair x={x} z={3.7} facing={Math.PI} />
        </group>
      ))}
      <Figure pose={anna} outfit={OUTFITS.anna!} card />
      <Figure pose={seated(-3.6)} outfit={OUTFITS.manager!} />
      <Figure pose={seated(-1.6)} outfit={OUTFITS.owner!} />
      <RoundedBox args={[0.5, 1.1, 0.5]} radius={0.05} position={[-7.5, 0.55, 4.7]} castShadow>
        <meshStandardMaterial color="#3f6b3a" roughness={0.9} />
      </RoundedBox>

      {/* security desk */}
      <Desk x={4.2} z={3.05} w={1.8} />
      <Chair x={4.2} z={3.7} facing={Math.PI} />
      <Figure pose={{ pose: "sit", x: 4.2, z: 3.62, facing: Math.PI, phase: 0 }} outfit={OUTFITS.staff!} />

      {/* meeting room */}
      <RoundedBox args={[2.4, 0.04, 1.1]} radius={0.02} position={[-5.1, 0.74, -3.3]} castShadow receiveShadow>
        <meshStandardMaterial color="#e9e4dc" roughness={0.5} />
      </RoundedBox>
      <mesh position={[-5.1, 0.37, -3.3]} castShadow>
        <boxGeometry args={[0.3, 0.72, 0.3]} />
        <meshStandardMaterial color="#3a3f45" />
      </mesh>
      {[-5.9, -4.3].flatMap((x) => [
        <Chair key={`${x}a`} x={x} z={-2.45} facing={Math.PI} />,
        <Chair key={`${x}b`} x={x} z={-4.15} facing={0} />,
      ])}

      {/* lab benches */}
      {[-2.4, -4.1].map((z) => (
        <group key={z}>
          <RoundedBox args={[3.4, 0.05, 0.75]} radius={0.01} position={[0, 0.92, z]} castShadow receiveShadow>
            <meshStandardMaterial color="#1f2937" roughness={0.3} />
          </RoundedBox>
          <mesh position={[0, 0.45, z]} castShadow>
            <boxGeometry args={[3.3, 0.88, 0.68]} />
            <meshStandardMaterial color="#e5e7eb" roughness={0.6} />
          </mesh>
          <RoundedBox args={[0.5, 0.35, 0.4]} radius={0.03} position={[-1, 1.12, z]} castShadow>
            <meshStandardMaterial color="#cbd5e1" roughness={0.4} />
          </RoundedBox>
          <RoundedBox args={[0.32, 0.5, 0.32]} radius={0.03} position={[0.9, 1.2, z]} castShadow>
            <meshStandardMaterial color="#94a3b8" roughness={0.4} />
          </RoundedBox>
        </group>
      ))}

      {/* comms room: PACS controller on the back wall, rack with the Gatehouse Agent */}
      <Controller position={[NODES.controller[0], NODES.controller[1], -5.04]} busy={controllerBusy} />
      <Rack position={[7.35, 0, NODES.agent[2]]} busy={agentBusy} />
      <mesh position={[3.0, 1.9, -5.12]}>
        <boxGeometry args={[0.35, 0.25, 0.04]} />
        <meshStandardMaterial color="#e2e8f0" roughness={0.6} />
      </mesh>
    </group>
  );
}
