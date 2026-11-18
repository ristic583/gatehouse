"use client";

import { Environment, Lightformer, OrbitControls } from "@react-three/drei";
import { Canvas, useFrame, useThree } from "@react-three/fiber";
import { type RefObject, useEffect, useRef, useState } from "react";
import * as THREE from "three";
import { Building } from "./building";
import { CloudLayer, Links, Packets } from "./cloud";
import { type LabelElements, LabelLayer, LabelProjector } from "./labels";
import { Laptop, Monitor } from "./props";
import { NODES, type Packet, type Person, type SceneState, SHOTS, STEP_SECONDS, sceneAt, type ShotId, type Track } from "./story";

export interface BuildingSceneProps {
  track: Track;
  step: number;
  playing: boolean;
  /** Changing it restarts the current step from t = 0. */
  restartKey: number;
  onStepEnd: () => void;
}

/** The discrete part of the scene state: changes a few times per step, so it is React state. */
type Discrete = Pick<
  SceneState,
  "shot" | "readerLed" | "annaScreen" | "managerScreen" | "ownerScreen" | "securityFlash" | "active" | "cloudDimmed"
>;
const discreteOf = (s: SceneState): Discrete => ({
  shot: s.shot,
  readerLed: s.readerLed,
  annaScreen: s.annaScreen,
  managerScreen: s.managerScreen,
  ownerScreen: s.ownerScreen,
  securityFlash: s.securityFlash,
  active: s.active,
  cloudDimmed: s.cloudDimmed,
});

interface DriverProps {
  props: BuildingSceneProps;
  annaRef: RefObject<Person>;
  doorOpenRef: RefObject<number>;
  controllerBusyRef: RefObject<boolean>;
  agentBusyRef: RefObject<boolean>;
  packetsRef: RefObject<readonly Packet[]>;
  setDiscrete: (d: Discrete) => void;
}

/** Advances the step clock and writes the per-frame state into refs that meshes read in their own useFrame. */
function Driver({ props, annaRef, doorOpenRef, controllerBusyRef, agentBusyRef, packetsRef, setDiscrete }: DriverProps) {
  const t = useRef(0);
  const key = useRef("");
  const ended = useRef(false);
  const { track, step, playing, restartKey, onStepEnd } = props;
  useEffect(() => {
    t.current = 0;
    ended.current = false;
  }, [track, step, restartKey]);
  useFrame((_, dt) => {
    if (playing) {
      t.current = Math.min(1, t.current + Math.min(dt, 0.1) / (STEP_SECONDS[track][step] ?? 4));
    }
    const s = sceneAt(track, step, t.current);
    annaRef.current = s.anna;
    doorOpenRef.current = s.doorOpen;
    controllerBusyRef.current = s.controllerBusy;
    agentBusyRef.current = s.agentBusy;
    packetsRef.current = s.packets;
    const d = discreteOf(s);
    const k = JSON.stringify(d);
    if (k !== key.current) {
      key.current = k;
      setDiscrete(d);
    }
    if (t.current >= 1 && playing && !ended.current) {
      ended.current = true;
      onStepEnd();
    }
  });
  return null;
}

/** Flies the camera to each step's shot; the user can orbit in between. */
function Director({ shot }: { shot: ShotId }) {
  const { camera, controls } = useThree() as unknown as {
    camera: THREE.PerspectiveCamera;
    controls: { target: THREE.Vector3; update: () => void } | null;
  };
  const fly = useRef<{
    fromPos: THREE.Vector3;
    fromTarget: THREE.Vector3;
    toPos: THREE.Vector3;
    toTarget: THREE.Vector3;
    t: number;
  } | null>(null);
  useEffect(() => {
    if (controls === null) {
      return;
    }
    const s = SHOTS[shot];
    fly.current = {
      fromPos: camera.position.clone(),
      fromTarget: controls.target.clone(),
      toPos: new THREE.Vector3(...s.pos),
      toTarget: new THREE.Vector3(...s.target),
      t: 0,
    };
  }, [shot, camera, controls]);
  useFrame((_, dt) => {
    const f = fly.current;
    if (f === null || controls === null) {
      return;
    }
    f.t = Math.min(1, f.t + Math.min(dt, 0.1) / 1.3);
    const k = f.t * f.t * (3 - 2 * f.t);
    camera.position.lerpVectors(f.fromPos, f.toPos, k);
    controls.target.lerpVectors(f.fromTarget, f.toTarget, k);
    controls.update();
    if (f.t >= 1) {
      fly.current = null;
    }
  });
  return null;
}

export default function BuildingScene(props: BuildingSceneProps) {
  const first = sceneAt(props.track, props.step, 0);
  const [discrete, setDiscrete] = useState<Discrete>(() => discreteOf(first));
  const annaRef = useRef<Person>(first.anna);
  const doorOpenRef = useRef(0);
  const controllerBusyRef = useRef(false);
  const agentBusyRef = useRef(false);
  const packetsRef = useRef<readonly Packet[]>([]);
  const start = SHOTS[first.shot];
  const labelElements: LabelElements = useRef(new Map());

  return (
    <div className="relative h-full w-full">
      <Canvas
        shadows
        dpr={[1, 2]}
        camera={{ fov: 40, near: 0.1, far: 120, position: [...start.pos] }}
        gl={{
          antialias: true,
          toneMapping: THREE.ACESFilmicToneMapping,
          toneMappingExposure: 1.05,
        }}
        aria-label={
          "3D view of an office floor: a corridor with card readers on the doors, " +
          "the PACS controller and Gatehouse Agent in the comms room, and the Gatehouse services above"
        }
      >
        <color attach="background" args={["#e9eef3"]} />
        <fog attach="fog" args={["#e9eef3", 28, 60]} />
        <hemisphereLight args={["#f4f7fb", "#5b544c", 0.55]} />
        <directionalLight
          position={[6, 14, 8]}
          intensity={2.1}
          castShadow
          shadow-mapSize={[2048, 2048]}
          shadow-camera-left={-11}
          shadow-camera-right={11}
          shadow-camera-top={9}
          shadow-camera-bottom={-9}
          shadow-bias={-0.0004}
          shadow-normalBias={0.02}
        />
        <directionalLight position={[-8, 6, -4]} intensity={0.45} color="#ffd9a8" />
        {/* local studio environment for reflections (no network fetch) */}
        <Environment resolution={256} frames={1}>
          <Lightformer form="rect" intensity={2.4} position={[0, 6, 0]} rotation-x={Math.PI / 2} scale={[14, 6, 1]} />
          <Lightformer form="rect" intensity={1.2} position={[-8, 2, 4]} rotation-y={Math.PI / 2} scale={[8, 3, 1]} color="#ffe9cf" />
          <Lightformer form="rect" intensity={1} position={[8, 2, -4]} rotation-y={-Math.PI / 2} scale={[8, 3, 1]} color="#dbeafe" />
        </Environment>

        <Building
          anna={annaRef}
          doorOpen={doorOpenRef}
          controllerBusy={controllerBusyRef}
          agentBusy={agentBusyRef}
          readerLed={discrete.readerLed}
        />
        <Laptop position={NODES.annaLaptop} screen={{ kind: "map", state: discrete.annaScreen }} />
        <Laptop
          position={NODES.managerLaptop}
          screen={{
            kind: "approver",
            state: discrete.managerScreen,
            role: "Manager",
          }}
        />
        <Laptop
          position={NODES.ownerLaptop}
          screen={{
            kind: "approver",
            state: discrete.ownerScreen,
            role: "Area owner",
          }}
        />
        <Monitor position={[NODES.securityMonitor[0], NODES.securityMonitor[1], 2.95]} flash={discrete.securityFlash} />
        <CloudLayer active={discrete.active} dimmed={discrete.cloudDimmed} />
        <Links active={discrete.active} />
        <Packets packets={packetsRef} />
        <LabelProjector elements={labelElements} />

        <Driver
          props={props}
          annaRef={annaRef}
          doorOpenRef={doorOpenRef}
          controllerBusyRef={controllerBusyRef}
          agentBusyRef={agentBusyRef}
          packetsRef={packetsRef}
          setDiscrete={setDiscrete}
        />
        <Director shot={discrete.shot} />
        <OrbitControls
          makeDefault
          enableDamping
          enablePan={false}
          minDistance={2}
          maxDistance={30}
          maxPolarAngle={Math.PI * 0.49}
          target={[...start.target]}
        />
      </Canvas>
      <LabelLayer elements={labelElements} active={discrete.active} dimmed={discrete.cloudDimmed} />
    </div>
  );
}
