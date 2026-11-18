"use client";

import { RoundedBox } from "@react-three/drei";
import { useFrame } from "@react-three/fiber";
import { type RefObject, useEffect, useMemo, useRef } from "react";
import * as THREE from "three";
import type { ApproverScreen, MapScreen, Person as PersonState, ReaderLed } from "./story";
import { approverScreenTexture, mapScreenTexture, readerFaceTexture, securityScreenTexture, signTexture, woodTexture } from "./textures";

const METAL = { color: "#9aa3ad", metalness: 0.8, roughness: 0.32 } as const;
const DARK_METAL = {
  color: "#2e343b",
  metalness: 0.6,
  roughness: 0.45,
} as const;

export interface Outfit {
  skin: string;
  hair: string;
  top: string;
  bottom: string;
  shoes: string;
  longHair?: boolean;
}

function Limb({ length, radius, color }: { length: number; radius: number; color: string }) {
  return (
    <mesh position={[0, -length / 2, 0]} castShadow>
      <capsuleGeometry args={[radius, Math.max(0.01, length - radius * 2), 6, 12]} />
      <meshStandardMaterial color={color} roughness={0.75} />
    </mesh>
  );
}

type PoseSource = { current: PersonState } | PersonState;
const read = (s: PoseSource): PersonState => ("current" in s ? s.current : s);

/** A simple articulated figure (≈1.72 m). Pose and position are read every frame from `pose` (a ref or a fixed value). */
export function Figure({ pose, outfit, card = false }: { pose: PoseSource; outfit: Outfit; card?: boolean }) {
  const root = useRef<THREE.Group>(null);
  const pelvis = useRef<THREE.Group>(null);
  const legs = [useRef<THREE.Group>(null), useRef<THREE.Group>(null)];
  const knees = [useRef<THREE.Group>(null), useRef<THREE.Group>(null)];
  const arms = [useRef<THREE.Group>(null), useRef<THREE.Group>(null)];
  const elbows = [useRef<THREE.Group>(null), useRef<THREE.Group>(null)];
  const cardRef = useRef<THREE.Mesh>(null);

  useFrame(() => {
    const p = read(pose);
    if (root.current === null || pelvis.current === null) {
      return;
    }
    root.current.position.set(p.x, 0, p.z);
    root.current.rotation.y = p.facing;
    const sit = p.pose === "sit";
    pelvis.current.position.y = sit ? 0.5 : 0.93 + (p.pose === "walk" ? Math.abs(Math.sin(p.phase)) * 0.025 : 0);
    const swing = p.pose === "walk" ? Math.sin(p.phase) * 0.5 : 0;
    [0, 1].forEach((i) => {
      const side = i === 0 ? 1 : -1;
      const leg = legs[i]!.current;
      const knee = knees[i]!.current;
      const arm = arms[i]!.current;
      const elbow = elbows[i]!.current;
      if (leg === null || knee === null || arm === null || elbow === null) {
        return;
      }
      leg.rotation.x = sit ? -Math.PI / 2 : swing * side * -1;
      knee.rotation.x = sit ? Math.PI / 2 : p.pose === "walk" ? Math.max(0, Math.sin(p.phase + (side > 0 ? 0 : Math.PI))) * 0.6 : 0;
      const presenting = p.pose === "present" && i === 1;
      arm.rotation.x = sit ? -0.75 : presenting ? -1.25 : swing * side * 0.7;
      arm.rotation.z = presenting ? 0.12 : side * 0.06;
      elbow.rotation.x = sit ? -0.85 : presenting ? -0.25 : -0.15;
    });
    if (cardRef.current !== null) {
      cardRef.current.visible = card && (p.pose === "present" || p.pose === "walk" || p.pose === "stand");
    }
  });

  return (
    <group ref={root}>
      <group ref={pelvis}>
        {/* torso */}
        <mesh position={[0, 0.3, 0]} scale={[1.12, 1, 0.72]} castShadow>
          <capsuleGeometry args={[0.15, 0.3, 8, 16]} />
          <meshStandardMaterial color={outfit.top} roughness={0.85} />
        </mesh>
        <mesh position={[0, 0.02, 0]} scale={[1.1, 0.6, 0.75]} castShadow>
          <sphereGeometry args={[0.16, 16, 12]} />
          <meshStandardMaterial color={outfit.bottom} roughness={0.85} />
        </mesh>
        {/* neck + head */}
        <mesh position={[0, 0.6, 0]} castShadow>
          <cylinderGeometry args={[0.045, 0.05, 0.1, 12]} />
          <meshStandardMaterial color={outfit.skin} roughness={0.6} />
        </mesh>
        <mesh position={[0, 0.74, 0.01]} scale={[0.92, 1.08, 1]} castShadow>
          <sphereGeometry args={[0.1, 24, 18]} />
          <meshStandardMaterial color={outfit.skin} roughness={0.6} />
        </mesh>
        {/* face: nose and eyes on +z so the facing reads at a glance */}
        <mesh position={[0, 0.735, 0.1]} rotation={[Math.PI / 2, 0, 0]}>
          <coneGeometry args={[0.014, 0.035, 8]} />
          <meshStandardMaterial color={outfit.skin} roughness={0.6} />
        </mesh>
        {[-0.033, 0.033].map((x) => (
          <mesh key={x} position={[x, 0.76, 0.088]}>
            <sphereGeometry args={[0.009, 8, 6]} />
            <meshStandardMaterial color="#1f2328" roughness={0.3} />
          </mesh>
        ))}
        {/* hair covers the top and the back of the head, not the face */}
        <mesh position={[0, 0.775, -0.02]} scale={outfit.longHair === true ? [1.04, 1.3, 1.08] : [1.0, 0.92, 1.0]} castShadow>
          <sphereGeometry args={[0.106, 24, 18, Math.PI * 0.85, Math.PI * 1.3, 0, Math.PI * 0.66]} />
          <meshStandardMaterial color={outfit.hair} roughness={0.9} side={THREE.DoubleSide} />
        </mesh>
        {[0, 1].map((i) => (
          <group key={`leg-${i}`} ref={legs[i]} position={[i === 0 ? 0.09 : -0.09, -0.02, 0]}>
            <Limb length={0.44} radius={0.068} color={outfit.bottom} />
            <group ref={knees[i]} position={[0, -0.44, 0]}>
              <Limb length={0.42} radius={0.055} color={outfit.bottom} />
              <mesh position={[0, -0.45, 0.04]} castShadow>
                <boxGeometry args={[0.09, 0.07, 0.24]} />
                <meshStandardMaterial color={outfit.shoes} roughness={0.5} />
              </mesh>
            </group>
          </group>
        ))}
        {[0, 1].map((i) => (
          <group key={`arm-${i}`} ref={arms[i]} position={[i === 0 ? 0.2 : -0.2, 0.52, 0]}>
            <Limb length={0.29} radius={0.046} color={outfit.top} />
            <group ref={elbows[i]} position={[0, -0.29, 0]}>
              <Limb length={0.25} radius={0.038} color={outfit.skin} />
              <mesh position={[0, -0.27, 0]} castShadow>
                <sphereGeometry args={[0.042, 12, 10]} />
                <meshStandardMaterial color={outfit.skin} roughness={0.6} />
              </mesh>
              {i === 1 ? (
                <mesh ref={cardRef} position={[0, -0.31, 0.03]} rotation={[0.2, 0, 0]} castShadow>
                  <boxGeometry args={[0.054, 0.086, 0.004]} />
                  <meshStandardMaterial color="#f4f6f8" roughness={0.3} />
                </mesh>
              ) : null}
            </group>
          </group>
        ))}
      </group>
    </group>
  );
}

const LED_COLOR: Record<ReaderLed, string> = {
  idle: "#d97706",
  reading: "#3b82f6",
  granted: "#22c55e",
  denied: "#ef4444",
};

/** Wall-mounted contactless reader (≈ 85 × 150 × 30 mm) with LED bar and beeper grille. */
export function Reader({ position, led }: { position: readonly [number, number, number]; led: ReaderLed }) {
  const face = useMemo(() => readerFaceTexture(), []);
  const ledMat = useRef<THREE.MeshStandardMaterial>(null);
  useEffect(() => {
    ledMat.current?.color.set(LED_COLOR[led]);
    ledMat.current?.emissive.set(LED_COLOR[led]);
  }, [led]);
  return (
    <group position={position}>
      <RoundedBox args={[0.1, 0.17, 0.012]} radius={0.004} position={[0, 0, -0.012]} castShadow>
        <meshStandardMaterial color="#d8dce0" roughness={0.6} />
      </RoundedBox>
      <RoundedBox args={[0.085, 0.15, 0.028]} radius={0.01} smoothness={4} castShadow>
        <meshStandardMaterial color="#1b1f24" roughness={0.25} metalness={0.2} />
      </RoundedBox>
      <mesh position={[0, -0.005, 0.0145]}>
        <planeGeometry args={[0.072, 0.126]} />
        <meshStandardMaterial map={face} roughness={0.15} metalness={0.1} />
      </mesh>
      <mesh position={[0, 0.062, 0.0152]}>
        <planeGeometry args={[0.05, 0.007]} />
        <meshStandardMaterial ref={ledMat} color={LED_COLOR[led]} emissive={LED_COLOR[led]} emissiveIntensity={2.4} toneMapped={false} />
      </mesh>
    </group>
  );
}

/** A door in the corridor wall: steel frame, veneered leaf with vision panel, lever, closer, kick plate, strike and room sign. */
export function Door({
  x,
  wallZ,
  number,
  name,
  restricted,
  open,
}: {
  x: number;
  wallZ: number;
  number: string;
  name: string;
  restricted: boolean;
  open?: RefObject<number>;
}) {
  const wood = useMemo(() => woodTexture(), []);
  const sign = useMemo(() => signTexture(number, name, restricted), [number, name, restricted]);
  const leaf = useRef<THREE.Group>(null);
  useFrame(() => {
    if (leaf.current !== null) {
      leaf.current.rotation.y = (open?.current ?? 0) * 1.45;
    }
  });
  const W = 1.0;
  const H = 2.1;
  const frame = { color: "#4b5560", metalness: 0.55, roughness: 0.4 } as const;
  return (
    <group position={[x, 0, wallZ]}>
      {/* frame */}
      {[-1, 1].map((s) => (
        <mesh key={s} position={[s * (W / 2 + 0.03), H / 2 + 0.03, 0]} castShadow receiveShadow>
          <boxGeometry args={[0.06, H + 0.06, 0.16]} />
          <meshStandardMaterial {...frame} />
        </mesh>
      ))}
      <mesh position={[0, H + 0.03, 0]} castShadow>
        <boxGeometry args={[W + 0.12, 0.06, 0.16]} />
        <meshStandardMaterial {...frame} />
      </mesh>
      {/* electric strike in the latch-side jamb */}
      <mesh position={[W / 2 + 0.002, 1.02, 0.06]}>
        <boxGeometry args={[0.012, 0.16, 0.03]} />
        <meshStandardMaterial {...METAL} />
      </mesh>
      {/* leaf, hinged on the left (-x), swinging into the room (-z) */}
      <group ref={leaf} position={[-W / 2, 0, -0.02]}>
        <group position={[W / 2, 0, 0]}>
          <mesh position={[0, H / 2, 0]} castShadow receiveShadow>
            <boxGeometry args={[W - 0.01, H - 0.01, 0.045]} />
            <meshStandardMaterial map={wood} roughness={0.55} />
          </mesh>
          <mesh position={[-0.18, 1.45, 0]}>
            <boxGeometry args={[0.16, 0.7, 0.047]} />
            <meshPhysicalMaterial color="#cfe3ea" transmission={0.6} roughness={0.08} thickness={0.01} transparent opacity={0.55} />
          </mesh>
          <mesh position={[0, 0.12, 0.024]}>
            <boxGeometry args={[W - 0.06, 0.22, 0.003]} />
            <meshStandardMaterial {...METAL} />
          </mesh>
          {[1, -1].map((side) => (
            <group key={side} position={[W / 2 - 0.08, 1.02, side * 0.035]}>
              <mesh rotation={[Math.PI / 2, 0, 0]}>
                <cylinderGeometry args={[0.025, 0.025, 0.012, 20]} />
                <meshStandardMaterial {...METAL} />
              </mesh>
              <mesh position={[-0.06, 0, side * 0.03]} rotation={[0, 0, Math.PI / 2]} castShadow>
                <capsuleGeometry args={[0.011, 0.11, 4, 10]} />
                <meshStandardMaterial {...METAL} />
              </mesh>
            </group>
          ))}
          {/* overhead closer on the room side */}
          <mesh position={[-0.22, H - 0.1, -0.06]} castShadow>
            <boxGeometry args={[0.3, 0.06, 0.06]} />
            <meshStandardMaterial color="#b9bec4" metalness={0.5} roughness={0.4} />
          </mesh>
        </group>
      </group>
      {/* room sign beside the door */}
      <mesh position={[W / 2 + 0.33, 1.6, 0.07]}>
        <planeGeometry args={[0.26, 0.13]} />
        <meshStandardMaterial map={sign} roughness={0.4} />
      </mesh>
    </group>
  );
}

function ScreenMesh({ texture, w, h }: { texture: THREE.Texture; w: number; h: number }) {
  useEffect(() => () => texture.dispose(), [texture]);
  return (
    <mesh position={[0, 0, 0.004]}>
      <planeGeometry args={[w, h]} />
      <meshBasicMaterial map={texture} toneMapped={false} />
    </mesh>
  );
}

/** Laptop open on a desk, screen towards +z. */
export function Laptop({
  position,
  screen,
}: {
  position: readonly [number, number, number];
  screen: { kind: "map"; state: MapScreen } | { kind: "approver"; state: ApproverScreen; role: string };
}) {
  const texture = useMemo(
    () => (screen.kind === "map" ? mapScreenTexture(screen.state) : approverScreenTexture(screen.state, screen.role)),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [screen.kind, screen.state, screen.kind === "approver" ? screen.role : ""],
  );
  return (
    <group position={position}>
      <RoundedBox args={[0.34, 0.016, 0.23]} radius={0.006} position={[0, -0.212, 0.12]} castShadow>
        <meshStandardMaterial color="#b4bac1" metalness={0.75} roughness={0.3} />
      </RoundedBox>
      <group position={[0, -0.205, 0.005]} rotation={[-0.22, 0, 0]}>
        <group position={[0, 0.115, 0]}>
          <RoundedBox args={[0.34, 0.225, 0.008]} radius={0.004} castShadow>
            <meshStandardMaterial color="#b4bac1" metalness={0.75} roughness={0.3} />
          </RoundedBox>
          <ScreenMesh texture={texture} w={0.32} h={0.2} />
        </group>
      </group>
    </group>
  );
}

/** Security desk monitor on a stand. */
export function Monitor({ position, flash }: { position: readonly [number, number, number]; flash: boolean }) {
  const texture = useMemo(() => securityScreenTexture(flash), [flash]);
  return (
    <group position={position}>
      <RoundedBox args={[0.62, 0.38, 0.03]} radius={0.008} castShadow>
        <meshStandardMaterial color="#1f2328" roughness={0.4} />
      </RoundedBox>
      <group position={[0, 0, 0.012]}>
        <ScreenMesh texture={texture} w={0.59} h={0.35} />
      </group>
      <mesh position={[0, -0.3, -0.04]} castShadow>
        <boxGeometry args={[0.05, 0.25, 0.03]} />
        <meshStandardMaterial {...DARK_METAL} />
      </mesh>
      <mesh position={[0, -0.42, 0]} castShadow>
        <boxGeometry args={[0.24, 0.015, 0.18]} />
        <meshStandardMaterial {...DARK_METAL} />
      </mesh>
    </group>
  );
}

export function Desk({ x, z, w = 1.4 }: { x: number; z: number; w?: number }) {
  return (
    <group position={[x, 0, z]}>
      <RoundedBox args={[w, 0.03, 0.72]} radius={0.008} position={[0, 0.735, 0]} castShadow receiveShadow>
        <meshStandardMaterial color="#d9c3a2" roughness={0.6} />
      </RoundedBox>
      {[-1, 1].map((s) => (
        <mesh key={s} position={[s * (w / 2 - 0.06), 0.36, 0]} castShadow>
          <boxGeometry args={[0.05, 0.72, 0.62]} />
          <meshStandardMaterial {...DARK_METAL} />
        </mesh>
      ))}
    </group>
  );
}

/** Office chair; `facing` 0 means the occupant faces +z. */
export function Chair({ x, z, facing }: { x: number; z: number; facing: number }) {
  return (
    <group position={[x, 0, z]} rotation={[0, facing, 0]}>
      <RoundedBox args={[0.48, 0.07, 0.46]} radius={0.025} position={[0, 0.46, 0]} castShadow>
        <meshStandardMaterial color="#30363d" roughness={0.9} />
      </RoundedBox>
      <RoundedBox args={[0.46, 0.55, 0.06]} radius={0.03} position={[0, 0.8, -0.24]} rotation={[-0.08, 0, 0]} castShadow>
        <meshStandardMaterial color="#30363d" roughness={0.9} />
      </RoundedBox>
      <mesh position={[0, 0.24, 0]}>
        <cylinderGeometry args={[0.025, 0.025, 0.4, 12]} />
        <meshStandardMaterial {...METAL} />
      </mesh>
      {[0, 1, 2, 3, 4].map((i) => (
        <mesh
          key={i}
          position={[Math.sin((i * 2 * Math.PI) / 5) * 0.16, 0.04, Math.cos((i * 2 * Math.PI) / 5) * 0.16]}
          rotation={[0, (i * 2 * Math.PI) / 5, 0]}
          castShadow
        >
          <boxGeometry args={[0.035, 0.03, 0.32]} />
          <meshStandardMaterial {...DARK_METAL} />
        </mesh>
      ))}
    </group>
  );
}

function blinkFrame(mat: THREE.MeshStandardMaterial | null, busy: RefObject<boolean>, t: number, rate: number) {
  if (mat !== null) {
    mat.emissiveIntensity = busy.current ? (Math.sin(t * rate) > 0 ? 3 : 0.3) : 1.2;
  }
}

/** Vendor door controller in a steel enclosure with the door open: board, relays, terminal blocks, status LEDs. */
export function Controller({ position, busy }: { position: readonly [number, number, number]; busy: RefObject<boolean> }) {
  const led = useRef<THREE.MeshStandardMaterial>(null);
  useFrame(({ clock }) => blinkFrame(led.current, busy, clock.elapsedTime, 28));
  return (
    <group position={position}>
      <mesh castShadow receiveShadow position={[0, 0, -0.07]}>
        <boxGeometry args={[0.55, 0.65, 0.04]} />
        <meshStandardMaterial color="#c9ccd0" metalness={0.4} roughness={0.5} />
      </mesh>
      {[-1, 1].map((s) => (
        <mesh key={`s${s}`} position={[s * 0.27, 0, 0.02]} castShadow>
          <boxGeometry args={[0.012, 0.65, 0.18]} />
          <meshStandardMaterial color="#c9ccd0" metalness={0.4} roughness={0.5} />
        </mesh>
      ))}
      {[-1, 1].map((s) => (
        <mesh key={`t${s}`} position={[0, s * 0.32, 0.02]} castShadow>
          <boxGeometry args={[0.55, 0.012, 0.18]} />
          <meshStandardMaterial color="#c9ccd0" metalness={0.4} roughness={0.5} />
        </mesh>
      ))}
      {/* open door hinged on the left */}
      <mesh position={[-0.27, 0, 0.3]} rotation={[0, Math.PI / 2.3, 0]} castShadow>
        <boxGeometry args={[0.55, 0.65, 0.012]} />
        <meshStandardMaterial color="#c9ccd0" metalness={0.4} roughness={0.5} />
      </mesh>
      <mesh position={[0, 0.04, -0.045]}>
        <boxGeometry args={[0.42, 0.4, 0.01]} />
        <meshStandardMaterial color="#1f6f43" roughness={0.6} />
      </mesh>
      {[
        [-0.12, 0.12, 0.08, 0.08],
        [0.06, 0.12, 0.12, 0.05],
        [0.08, -0.02, 0.06, 0.06],
        [-0.12, -0.04, 0.1, 0.04],
      ].map(([x, y, w, h], i) => (
        <mesh key={`c${i}`} position={[x!, y!, -0.032]}>
          <boxGeometry args={[w!, h!, 0.018]} />
          <meshStandardMaterial color="#15181b" roughness={0.4} />
        </mesh>
      ))}
      {[-0.15, -0.05, 0.05, 0.15].map((x) => (
        <mesh key={`r${x}`} position={[x, -0.12, -0.028]}>
          <boxGeometry args={[0.06, 0.04, 0.03]} />
          <meshStandardMaterial color="#e5e7eb" roughness={0.5} />
        </mesh>
      ))}
      <mesh position={[0, 0.2, -0.032]}>
        <boxGeometry args={[0.36, 0.025, 0.02]} />
        <meshStandardMaterial color="#16a34a" roughness={0.6} />
      </mesh>
      <mesh position={[0.15, 0.06, -0.03]}>
        <sphereGeometry args={[0.01, 10, 8]} />
        <meshStandardMaterial ref={led} color="#22c55e" emissive="#22c55e" emissiveIntensity={1.2} toneMapped={false} />
      </mesh>
      {/* backup battery */}
      <mesh position={[0, -0.24, -0.01]} castShadow>
        <boxGeometry args={[0.18, 0.1, 0.07]} />
        <meshStandardMaterial color="#111" roughness={0.6} />
      </mesh>
    </group>
  );
}

/** 42U rack facing -x with a few 1U/2U servers; the Gatehouse Agent box is the highlighted one. */
export function Rack({ position, busy }: { position: readonly [number, number, number]; busy: RefObject<boolean> }) {
  const led = useRef<THREE.MeshStandardMaterial>(null);
  useFrame(({ clock }) => blinkFrame(led.current, busy, clock.elapsedTime, 22));
  const units: [number, number, string][] = [
    [0.55, 0.09, "#2a2f35"],
    [0.75, 0.045, "#2a2f35"],
    [0.95, 0.09, "#3b4148"],
    [1.45, 0.045, "#2a2f35"],
    [1.6, 0.045, "#2a2f35"],
  ];
  return (
    <group position={position}>
      {[
        [-0.48, -0.28],
        [-0.48, 0.28],
        [0.48, -0.28],
        [0.48, 0.28],
      ].map(([x, z]) => (
        <mesh key={`${x}${z}`} position={[x!, 1.0, z!]} castShadow>
          <boxGeometry args={[0.04, 2.0, 0.04]} />
          <meshStandardMaterial color="#16191d" metalness={0.5} roughness={0.5} />
        </mesh>
      ))}
      {[0.02, 1.98].map((y) => (
        <mesh key={y} position={[0, y, 0]} castShadow>
          <boxGeometry args={[1.0, 0.04, 0.6]} />
          <meshStandardMaterial color="#16191d" metalness={0.5} roughness={0.5} />
        </mesh>
      ))}
      <mesh position={[0.48, 1.0, 0]}>
        <boxGeometry args={[0.02, 1.96, 0.56]} />
        <meshStandardMaterial color="#1c2025" metalness={0.4} roughness={0.6} />
      </mesh>
      {units.map(([y, h, color]) => (
        <group key={y} position={[0, y, 0]}>
          <mesh castShadow>
            <boxGeometry args={[0.9, h, 0.48]} />
            <meshStandardMaterial color={color} metalness={0.5} roughness={0.4} />
          </mesh>
          <mesh position={[-0.451, 0, 0.16]}>
            <boxGeometry args={[0.002, h * 0.3, 0.1]} />
            <meshStandardMaterial color="#38bdf8" emissive="#38bdf8" emissiveIntensity={0.8} toneMapped={false} />
          </mesh>
        </group>
      ))}
      {/* Gatehouse Agent: small 1U appliance */}
      <group position={[0, 1.3 - position[1], 0]}>
        <mesh castShadow>
          <boxGeometry args={[0.9, 0.045, 0.48]} />
          <meshStandardMaterial color="#e2e8f0" metalness={0.4} roughness={0.35} />
        </mesh>
        <mesh position={[-0.452, 0, -0.12]}>
          <boxGeometry args={[0.002, 0.014, 0.14]} />
          <meshStandardMaterial ref={led} color="#22c55e" emissive="#22c55e" emissiveIntensity={1.2} toneMapped={false} />
        </mesh>
      </group>
      {/* patch panel + cables */}
      <mesh position={[-0.46, 1.75, 0]}>
        <boxGeometry args={[0.03, 0.045, 0.48]} />
        <meshStandardMaterial color="#111" />
      </mesh>
    </group>
  );
}
