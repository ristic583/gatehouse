"use client";

import { Line, RoundedBox } from "@react-three/drei";
import { useFrame } from "@react-three/fiber";
import { type RefObject, useMemo, useRef } from "react";
import * as THREE from "three";
import { LINKS, type LinkId, NODES, type NodeId, type Packet, type Vec3 } from "./story";

/** The Gatehouse services (one Aspire deployment), drawn as a platform above the building. Names are the real projects/resources. */
export const CLOUD_BLOCKS: {
  id: NodeId;
  title: string;
  detail: string;
  color: string;
}[] = [
  {
    id: "web",
    title: "Web + Api",
    detail: "Next.js BFF · Gatehouse.Api · SignalR hub",
    color: "#38bdf8",
  },
  {
    id: "db",
    title: "PostgreSQL",
    detail: "tenant DB · outbox · connector_commands",
    color: "#818cf8",
  },
  { id: "rabbit", title: "RabbitMQ", detail: "MassTransit", color: "#fb923c" },
  {
    id: "workers",
    title: "Workers",
    detail: "dispatch · DoorEventIngestConsumer",
    color: "#a78bfa",
  },
  {
    id: "redis",
    title: "Redis",
    detail: "poll signal · door state · live updates",
    color: "#f87171",
  },
  {
    id: "gateway",
    title: "ConnectorGateway",
    detail: "/connector/v1 · mTLS",
    color: "#4ade80",
  },
  {
    id: "keycloak",
    title: "Keycloak",
    detail: "sign-in only",
    color: "#94a3b8",
  },
];

/** People and devices in the building. */
export const PLACE_LABELS: {
  id: NodeId;
  title: string;
  detail: string;
  offset: Vec3;
}[] = [
  {
    id: "annaLaptop",
    title: "Anna",
    detail: "employee · map",
    offset: [0, 0.45, 0.2],
  },
  {
    id: "managerLaptop",
    title: "Manager",
    detail: "approver 1",
    offset: [0, 0.45, 0.2],
  },
  {
    id: "ownerLaptop",
    title: "Area owner",
    detail: "approver 2",
    offset: [0, 0.45, 0.2],
  },
  {
    id: "labReader",
    title: "Reader",
    detail: "Lab 2.04 · In",
    offset: [0.35, 0.25, 0.1],
  },
  {
    id: "controller",
    title: "PACS controller",
    detail: "vendor · decides open/deny",
    offset: [0, 0.6, 0.2],
  },
  {
    id: "agent",
    title: "Gatehouse Agent",
    detail: "on-site · outbound only",
    offset: [-0.2, 0.95, 0],
  },
  {
    id: "securityMonitor",
    title: "Security desk",
    detail: "/live floor plan",
    offset: [0, 0.5, 0.1],
  },
];

const TONE: Record<Packet["tone"], string> = {
  request: "#60a5fa",
  command: "#f59e0b",
  event: "#34d399",
};

export function linkCurve(id: LinkId): THREE.Curve<THREE.Vector3> {
  const link = LINKS[id];
  const v = (p: Vec3) => new THREE.Vector3(...p);
  if ("path" in link) {
    const curve = new THREE.CurvePath<THREE.Vector3>();
    link.path.slice(1).forEach((p, i) => curve.add(new THREE.LineCurve3(v(link.path[i]!), v(p))));
    return curve;
  }
  const a = v(NODES[link.from]);
  const b = v(NODES[link.to]);
  if (link.kind === "net") {
    // up from the device, then across to the service
    const low = a.y < b.y ? a : b;
    const high = a.y < b.y ? b : a;
    return new THREE.QuadraticBezierCurve3(a, new THREE.Vector3(low.x, high.y + 0.2, low.z), b);
  }
  const mid = a.clone().add(b).multiplyScalar(0.5);
  mid.y += 0.7;
  return new THREE.QuadraticBezierCurve3(a, mid, b);
}

/** A service drawn as a 2U appliance: graphite chassis, vented bezel and a status strip in the service's accent colour. */
function Block({ id, color, active, dimmed }: { id: NodeId; color: string; active: boolean; dimmed: boolean }) {
  const strip = useRef<THREE.MeshStandardMaterial>(null);
  const body = useRef<THREE.MeshStandardMaterial>(null);
  const faded = dimmed || id === "keycloak";
  useFrame(({ clock }, dt) => {
    if (strip.current !== null) {
      const goal = active ? 2.2 + Math.sin(clock.elapsedTime * 6) * 0.6 : faded ? 0.15 : 0.6;
      strip.current.emissiveIntensity += (goal - strip.current.emissiveIntensity) * Math.min(1, dt * 6);
    }
    if (body.current !== null) {
      body.current.opacity += ((faded ? 0.4 : 1) - body.current.opacity) * Math.min(1, dt * 4);
    }
  });
  const p = NODES[id];
  return (
    <group position={[p[0], p[1] - 0.14, p[2]]}>
      <RoundedBox args={[1.5, 0.3, 0.85]} radius={0.025} smoothness={3} castShadow>
        <meshStandardMaterial ref={body} color="#2a3038" roughness={0.38} metalness={0.65} transparent />
      </RoundedBox>
      {[-0.55, -0.35, -0.15, 0.05, 0.25].map((x) => (
        <mesh key={x} position={[x, 0, 0.427]}>
          <boxGeometry args={[0.14, 0.17, 0.004]} />
          <meshStandardMaterial color="#151a20" roughness={0.8} />
        </mesh>
      ))}
      <mesh position={[0.5, 0, 0.428]}>
        <boxGeometry args={[0.3, 0.035, 0.004]} />
        <meshStandardMaterial ref={strip} color={color} emissive={color} emissiveIntensity={0.6} toneMapped={false} />
      </mesh>
      <mesh position={[0, 0.151, 0]}>
        <boxGeometry args={[1.42, 0.003, 0.78]} />
        <meshStandardMaterial color={color} roughness={0.5} transparent opacity={active ? 0.55 : 0.18} />
      </mesh>
    </group>
  );
}

export function CloudLayer({ active, dimmed }: { active: readonly NodeId[]; dimmed: boolean }) {
  return (
    <group>
      <mesh position={[0, 6.48, -0.85]} receiveShadow>
        <boxGeometry args={[15.6, 0.05, 5.4]} />
        <meshPhysicalMaterial
          color="#dbeafe"
          roughness={0.15}
          metalness={0}
          transmission={0.4}
          transparent
          opacity={dimmed ? 0.12 : 0.28}
        />
      </mesh>
      {CLOUD_BLOCKS.map((b) => (
        <Block key={b.id} id={b.id} color={b.color} active={active.includes(b.id)} dimmed={dimmed} />
      ))}
    </group>
  );
}

/** Every link drawn faintly; links between two active nodes brighter. Building cables are real tubes (Building), so only net links here. */
export function Links({ active }: { active: readonly NodeId[] }) {
  const lines = useMemo(
    () =>
      (Object.keys(LINKS) as LinkId[])
        .filter((id) => !("path" in LINKS[id]))
        .map((id) => ({
          id,
          points: linkCurve(id).getPoints(48),
          net: LINKS[id].kind === "net",
        })),
    [],
  );
  return (
    <>
      {lines.map((l) => {
        const on = active.includes(LINKS[l.id].from) && active.includes(LINKS[l.id].to);
        return (
          <Line
            key={l.id}
            points={l.points}
            color={on ? "#0ea5e9" : "#94a3b8"}
            lineWidth={on ? 2.2 : 1}
            dashed={l.net}
            dashSize={0.12}
            gapSize={0.08}
            transparent
            opacity={on ? 0.95 : 0.35}
          />
        );
      })}
    </>
  );
}

const MAX_PACKETS = 4;

/** Glowing packets moving along links; positions are read every frame from `packets`. */
export function Packets({ packets }: { packets: RefObject<readonly Packet[]> }) {
  const curves = useMemo(
    () =>
      Object.fromEntries((Object.keys(LINKS) as LinkId[]).map((id) => [id, linkCurve(id)])) as Record<LinkId, THREE.Curve<THREE.Vector3>>,
    [],
  );
  const meshes = useRef<(THREE.Group | null)[]>([]);
  const cores = useRef<(THREE.MeshStandardMaterial | null)[]>([]);
  const halos = useRef<(THREE.MeshBasicMaterial | null)[]>([]);
  useFrame(() => {
    const list = packets.current;
    for (let i = 0; i < MAX_PACKETS; i++) {
      const group = meshes.current[i];
      const packet = list[i];
      if (group === null || group === undefined) {
        continue;
      }
      group.visible = packet !== undefined;
      if (packet === undefined) {
        continue;
      }
      curves[packet.link].getPointAt(packet.reverse ? 1 - packet.p : packet.p, group.position);
      cores.current[i]?.color.set(TONE[packet.tone]);
      cores.current[i]?.emissive.set(TONE[packet.tone]);
      halos.current[i]?.color.set(TONE[packet.tone]);
    }
  });
  return (
    <>
      {Array.from({ length: MAX_PACKETS }, (_, i) => (
        <group
          key={i}
          ref={(g) => {
            meshes.current[i] = g;
          }}
          visible={false}
        >
          <mesh>
            <sphereGeometry args={[0.07, 20, 16]} />
            <meshStandardMaterial
              ref={(m) => {
                cores.current[i] = m;
              }}
              emissiveIntensity={3}
              toneMapped={false}
            />
          </mesh>
          <mesh>
            <sphereGeometry args={[0.16, 20, 16]} />
            <meshBasicMaterial
              ref={(m) => {
                halos.current[i] = m;
              }}
              transparent
              opacity={0.25}
              depthWrite={false}
              blending={THREE.AdditiveBlending}
            />
          </mesh>
        </group>
      ))}
    </>
  );
}
