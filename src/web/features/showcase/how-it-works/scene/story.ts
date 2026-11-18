/**
 * What the 3D "How it works" scene shows at each moment. Pure data + functions (no three.js), so it is unit-tested.
 * Step ids are the ones of flows.ts; every packet runs on a link between components that exist (docs/codemap.md).
 */
export type Track = "governance" | "door";
export type Vec3 = readonly [number, number, number];

export const NODES = {
  annaLaptop: [-5.6, 0.98, 3.05],
  managerLaptop: [-3.6, 0.98, 3.05],
  ownerLaptop: [-1.6, 0.98, 3.05],
  labReader: [0.72, 1.12, -1.1],
  controller: [4.2, 1.5, -4.92],
  agent: [6.55, 1.3, -3.45],
  securityMonitor: [4.2, 1.2, 3.0],
  web: [-4.4, 6.85, -0.4],
  db: [-1.4, 6.85, -2.1],
  rabbit: [1.3, 6.85, -0.4],
  workers: [3.8, 6.85, -2.1],
  redis: [3.8, 6.85, 1.0],
  gateway: [6.2, 6.85, -0.4],
  keycloak: [-6.6, 6.85, -2.7],
} as const satisfies Record<string, Vec3>;

export type NodeId = keyof typeof NODES;

export interface Link {
  from: NodeId;
  to: NodeId;
  /** "cable": the PACS's own reader wiring (polyline); "net": HTTPS/AMQP between processes (arc). */
  kind: "cable" | "net" | "internal";
  path?: readonly Vec3[];
}

/** The reader cable runs up the corridor wall, along the wall top into the comms room and down to the controller. */
const READER_CABLE: readonly Vec3[] = [
  NODES.labReader,
  [0.72, 2.55, -1.1],
  [3.4, 2.55, -1.1],
  [3.4, 2.55, -4.92],
  [4.2, 2.55, -4.92],
  [4.2, 1.86, -4.92],
];

const CONTROLLER_TO_AGENT: readonly Vec3[] = [
  NODES.controller,
  [4.6, 1.5, -4.92],
  [4.6, 2.3, -4.92],
  [6.55, 2.3, -3.6],
  [6.55, 1.32, -3.45],
];

export const LINKS = {
  annaWeb: { from: "annaLaptop", to: "web", kind: "net" },
  managerWeb: { from: "managerLaptop", to: "web", kind: "net" },
  ownerWeb: { from: "ownerLaptop", to: "web", kind: "net" },
  webDb: { from: "web", to: "db", kind: "internal" },
  dbRabbit: { from: "db", to: "rabbit", kind: "internal" },
  rabbitWorkers: { from: "rabbit", to: "workers", kind: "internal" },
  workersDb: { from: "workers", to: "db", kind: "internal" },
  workersRedis: { from: "workers", to: "redis", kind: "internal" },
  redisGateway: { from: "redis", to: "gateway", kind: "internal" },
  gatewayDb: { from: "gateway", to: "db", kind: "internal" },
  gatewayRabbit: { from: "gateway", to: "rabbit", kind: "internal" },
  redisWeb: { from: "redis", to: "web", kind: "internal" },
  agentGateway: { from: "agent", to: "gateway", kind: "net" },
  agentController: {
    from: "agent",
    to: "controller",
    kind: "cable",
    path: [...CONTROLLER_TO_AGENT].reverse(),
  },
  readerController: {
    from: "labReader",
    to: "controller",
    kind: "cable",
    path: READER_CABLE,
  },
  webSecurity: { from: "web", to: "securityMonitor", kind: "net" },
} as const satisfies Record<string, Link>;

export type LinkId = keyof typeof LINKS;

export type ShotId = "office" | "officeCloud" | "cloud" | "commsCloud" | "comms" | "corridor" | "door" | "doorComms" | "security";

/** Camera position and look-at target per shot. */
export const SHOTS: Record<ShotId, { pos: Vec3; target: Vec3 }> = {
  office: { pos: [-1.4, 3.4, 7.0], target: [-3.5, 0.9, 2.9] },
  officeCloud: { pos: [2.0, 7.5, 14.5], target: [-3.0, 3.8, 0.6] },
  cloud: { pos: [0.2, 12.6, 11.8], target: [0.0, 6.4, -0.9] },
  commsCloud: { pos: [-2.5, 7.0, 10.5], target: [5.0, 3.9, -2.4] },
  comms: { pos: [3.2, 4.6, 0.6], target: [5.6, 1.3, -4.2] },
  corridor: { pos: [-4.6, 2.4, 3.4], target: [0.2, 1.1, -0.8] },
  door: { pos: [-1.7, 2.0, 2.3], target: [0.35, 1.15, -1.2] },
  doorComms: { pos: [-2.4, 6.0, 6.8], target: [2.6, 1.5, -2.2] },
  security: { pos: [1.4, 3.1, 6.6], target: [4.2, 0.9, 2.7] },
};

export type ReaderLed = "idle" | "reading" | "granted" | "denied";
export type MapScreen = "NoAccess" | "Pending" | "HasAccess";
export type ApproverScreen = "idle" | "inbox" | "approved";

export interface Packet {
  link: LinkId;
  /** 0..1 along the link. */
  p: number;
  reverse: boolean;
  tone: "request" | "command" | "event";
}

export interface Person {
  pose: "sit" | "walk" | "stand" | "present";
  x: number;
  z: number;
  /** Rotation around y; 0 faces +z. */
  facing: number;
  /** Walk cycle phase in radians. */
  phase: number;
}

export interface SceneState {
  shot: ShotId;
  anna: Person;
  readerLed: ReaderLed;
  /** 0 closed .. 1 open. */
  doorOpen: number;
  controllerBusy: boolean;
  agentBusy: boolean;
  annaScreen: MapScreen;
  managerScreen: ApproverScreen;
  ownerScreen: ApproverScreen;
  securityFlash: boolean;
  active: readonly NodeId[];
  packets: readonly Packet[];
  /** The door decision happens without Gatehouse: the cloud layer is dimmed. */
  cloudDimmed: boolean;
}

export const STEP_SECONDS: Record<Track, readonly number[]> = {
  governance: [4.5, 4, 6, 4.5, 4.5, 5, 4, 5.5],
  door: [5.5, 4.5, 4.5, 3.5, 4.5, 4],
};

const clamp01 = (v: number) => Math.min(1, Math.max(0, v));
/** Progress of a sub-interval [a, b] of the step. */
const span = (t: number, a: number, b: number) => clamp01((t - a) / (b - a));
const ease = (v: number) => v * v * (3 - 2 * v);
const pk = (link: LinkId, t: number, a: number, b: number, tone: Packet["tone"], reverse = false): Packet[] =>
  t >= a && t <= b ? [{ link, p: span(t, a, b), reverse, tone }] : [];

const SEATED: Person = {
  pose: "sit",
  x: -5.6,
  z: 3.62,
  facing: Math.PI,
  phase: 0,
};

function walk(points: readonly (readonly [number, number])[], v: number): Person {
  const lengths = points.slice(1).map((p, i) => Math.hypot(p[0] - points[i]![0], p[1] - points[i]![1]));
  const total = lengths.reduce((a, b) => a + b, 0);
  let d = clamp01(v) * total;
  for (let i = 0; i < lengths.length; i++) {
    const len = lengths[i]!;
    const a = points[i]!;
    const b = points[i + 1]!;
    if (d <= len || i === lengths.length - 1) {
      const k = len === 0 ? 1 : Math.min(1, d / len);
      return {
        pose: v > 0 && v < 1 ? "walk" : "stand",
        x: a[0] + (b[0] - a[0]) * k,
        z: a[1] + (b[1] - a[1]) * k,
        facing: Math.atan2(b[0] - a[0], b[1] - a[1]),
        phase: clamp01(v) * total * 3.4,
      };
    }
    d -= len;
  }
  return { ...SEATED };
}

const TO_READER = [
  [-6.6, 0.25],
  [-0.4, 0.1],
  [0.72, -0.5],
] as const;
const INTO_LAB = [
  [0.72, -0.5],
  [0.05, -0.75],
  [0.05, -2.6],
] as const;
const AT_READER: Person = {
  pose: "present",
  x: 0.72,
  z: -0.5,
  facing: Math.PI,
  phase: 0,
};

const BASE: SceneState = {
  shot: "office",
  anna: SEATED,
  readerLed: "idle",
  doorOpen: 0,
  controllerBusy: false,
  agentBusy: false,
  annaScreen: "Pending",
  managerScreen: "approved",
  ownerScreen: "approved",
  securityFlash: false,
  active: [],
  packets: [],
  cloudDimmed: false,
};

type StepFn = (t: number) => Partial<SceneState>;

const GOVERNANCE: readonly StepFn[] = [
  // request: Anna asks for the lab on the map; the map shows it as Pending.
  (t) => ({
    shot: "officeCloud",
    annaScreen: t < 0.35 ? "NoAccess" : "Pending",
    managerScreen: "idle",
    ownerScreen: "idle",
    active: t > 0.75 ? ["annaLaptop", "web"] : ["annaLaptop"],
    packets: pk("annaWeb", t, 0.35, 0.85, "request"),
  }),
  // route: the API stores the request and resolves the chain (manager first, then area owner).
  (t) => ({
    shot: "cloud",
    managerScreen: t > 0.7 ? "inbox" : "idle",
    ownerScreen: "idle",
    active: ["web", "db"],
    packets: pk("webDb", t, 0.1, 0.55, "request"),
  }),
  // approve: each step in order; Anna cannot approve her own request.
  (t) => ({
    shot: "office",
    managerScreen: t < 0.3 ? "inbox" : "approved",
    ownerScreen: t < 0.55 ? "idle" : t < 0.78 ? "inbox" : "approved",
    active: t < 0.5 ? ["managerLaptop", "web"] : ["ownerLaptop", "web"],
    packets: [
      ...pk("managerWeb", t, 0.3, 0.5, "request"),
      ...pk("ownerWeb", t, 0.5, 0.62, "request", true),
      ...pk("ownerWeb", t, 0.78, 0.98, "request"),
    ],
  }),
  // outbox: the change and its events are committed together, then relayed to RabbitMQ.
  (t) => ({
    shot: "cloud",
    active: t < 0.45 ? ["web", "db"] : ["db", "rabbit"],
    packets: [...pk("webDb", t, 0.05, 0.4, "request"), ...pk("dbRabbit", t, 0.45, 0.9, "command")],
  }),
  // dispatch: Workers create the ConnectorCommand (connector_commands) and signal the gateway.
  (t) => ({
    shot: "cloud",
    active: ["rabbit", "workers", ...(t > 0.45 ? (["db", "redis"] as const) : [])],
    packets: [
      ...pk("rabbitWorkers", t, 0.05, 0.4, "command"),
      ...pk("workersDb", t, 0.45, 0.8, "command"),
      ...pk("workersRedis", t, 0.5, 0.75, "command"),
      ...pk("redisGateway", t, 0.75, 0.95, "command"),
    ],
  }),
  // poll: the on-site agent calls out (long-poll) and gets the command back on the same connection.
  (t) => ({
    shot: "commsCloud",
    agentBusy: true,
    active: ["agent", "gateway"],
    packets: [
      ...pk("agentGateway", t, 0.05, 0.4, "request"),
      ...pk("gatewayDb", t, 0.4, 0.55, "command"),
      ...pk("agentGateway", t, 0.55, 0.95, "command", true),
    ],
  }),
  // adapter: the agent's adapter writes the access level into the PACS.
  (t) => ({
    shot: "comms",
    agentBusy: true,
    controllerBusy: t > 0.6,
    active: ["agent", "controller"],
    packets: pk("agentController", t, 0.1, 0.6, "command"),
  }),
  // ack: only the PACS ack confirms; the map turns green.
  (t) => ({
    shot: "commsCloud",
    annaScreen: t > 0.88 ? "HasAccess" : "Pending",
    active: t < 0.6 ? ["controller", "agent", "gateway"] : ["gateway", "db", "web", "annaLaptop"],
    packets: [
      ...pk("agentController", t, 0.02, 0.22, "event", true),
      ...pk("agentGateway", t, 0.22, 0.55, "event"),
      ...pk("gatewayDb", t, 0.55, 0.7, "event"),
      ...pk("annaWeb", t, 0.7, 0.9, "event", true),
    ],
  }),
];

const DOOR: readonly StepFn[] = [
  // badge: Anna walks to the lab and holds her card at the reader.
  (t) => ({
    shot: "corridor",
    anna: t < 0.82 ? walk(TO_READER, ease(span(t, 0, 0.82))) : AT_READER,
    readerLed: t > 0.86 ? "reading" : "idle",
    active: t > 0.86 ? ["labReader"] : [],
  }),
  // decide: the reader asks the controller over its own cable; the controller grants by itself.
  (t) => ({
    shot: "doorComms",
    anna: AT_READER,
    cloudDimmed: true,
    readerLed: t < 0.75 ? "reading" : "granted",
    controllerBusy: t > 0.35 && t < 0.75,
    doorOpen: ease(span(t, 0.8, 1)),
    active: ["labReader", "controller"],
    packets: [...pk("readerController", t, 0.02, 0.35, "request"), ...pk("readerController", t, 0.42, 0.75, "command", true)],
  }),
  // pump: Anna walks in; the agent reads the event from the PACS.
  (t) => ({
    shot: "comms",
    anna: walk(INTO_LAB, ease(span(t, 0, 0.7))),
    readerLed: t < 0.5 ? "granted" : "idle",
    doorOpen: t < 0.7 ? 1 : 1 - ease(span(t, 0.7, 1)),
    agentBusy: t > 0.3,
    active: ["controller", "agent"],
    packets: pk("agentController", t, 0.3, 0.85, "event", true),
  }),
  // events: the agent posts the batch to the gateway.
  (t) => ({
    shot: "commsCloud",
    anna: walk(INTO_LAB, 1),
    agentBusy: true,
    active: ["agent", "gateway"],
    packets: pk("agentGateway", t, 0.1, 0.85, "event"),
  }),
  // ingest: gateway → RabbitMQ → DoorEventIngestConsumer (door_events + Redis door state).
  (t) => ({
    shot: "cloud",
    anna: walk(INTO_LAB, 1),
    active: ["gateway", "rabbit", "workers", "db", "redis"],
    packets: [
      ...pk("gatewayRabbit", t, 0.05, 0.35, "event"),
      ...pk("rabbitWorkers", t, 0.35, 0.6, "event"),
      ...pk("workersDb", t, 0.62, 0.9, "event"),
      ...pk("workersRedis", t, 0.62, 0.9, "event"),
    ],
  }),
  // signalr: the live update reaches the security desk's floor plan.
  (t) => ({
    shot: "security",
    anna: walk(INTO_LAB, 1),
    securityFlash: t > 0.7,
    active: ["redis", "web", "securityMonitor"],
    packets: [...pk("redisWeb", t, 0.05, 0.35, "event"), ...pk("webSecurity", t, 0.35, 0.7, "event")],
  }),
];

const SCRIPTS: Record<Track, readonly StepFn[]> = {
  governance: GOVERNANCE,
  door: DOOR,
};

/** The whole scene at progress t (0..1) of a step. Door track starts with access already granted (after the governance track). */
export function sceneAt(track: Track, step: number, t: number): SceneState {
  const fn = SCRIPTS[track][step];
  if (fn === undefined) {
    throw new RangeError(`No step ${step} in ${track}`);
  }
  const base: SceneState = track === "door" ? { ...BASE, anna: walk(TO_READER, 0), annaScreen: "HasAccess" } : BASE;
  return { ...base, ...fn(clamp01(t)) };
}
