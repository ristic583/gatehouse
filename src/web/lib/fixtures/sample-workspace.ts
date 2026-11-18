/** Fictional, in-memory data for the development-only visual review route. Never used by production routes. */
import type { AccessRequestDto, ApprovalInboxItemDto, AreaDetailsDto, FloorAccessMapDto, MeDto, SiteDto } from "@/lib/api/types";

export const sampleMe: MeDto = {
  subject: "preview",
  email: "sofie@example.test",
  displayName: "Sofie Berg",
  tenantId: "sample",
  tenantName: "Nordlys Workspace",
  person: { personId: "sofie", displayName: "Sofie Berg", type: "Employee" },
  roles: ["Employee", "AreaOwner", "Manager", "SecurityStaff"],
  onboardingPending: false,
};
export const sampleSites: SiteDto[] = [
  {
    siteId: "copenhagen",
    name: "Copenhagen headquarters",
    code: "CPH-HQ",
    timeZoneId: "Europe/Copenhagen",
    floors: [
      { floorId: "ground", name: "Ground floor", level: 0 },
      { floorId: "first", name: "First floor", level: 1 },
      { floorId: "second", name: "Second floor", level: 2 },
    ],
  },
  {
    siteId: "aarhus",
    name: "Aarhus studio",
    code: "AAR-ST",
    timeZoneId: "Europe/Copenhagen",
    floors: [{ floorId: "studio", name: "Studio floor", level: 0 }],
  },
];
const rooms = [
  ["Welcome lounge", "Meeting suite", "Collaboration hub", "Café & kitchen", "Atrium", "Delivery room"],
  ["Design studio", "Focus rooms", "Research lab", "Server room", "Project space", "Team lounge"],
  ["Executive suite", "Boardroom", "Roof terrace", "Archive", "Workshop", "Quiet lounge"],
  ["Main studio", "Materials library", "Workshop", "Meeting room", "Reception", "Storage"],
];
const layouts = [
  [
    [35, 35],
    [330, 35],
    [330, 150],
    [265, 150],
    [265, 255],
    [35, 255],
  ],
  [
    [355, 35],
    [610, 35],
    [610, 255],
    [355, 255],
  ],
  [
    [635, 35],
    [965, 35],
    [965, 255],
    [635, 255],
  ],
  [
    [35, 340],
    [305, 340],
    [305, 565],
    [35, 565],
  ],
  [
    [330, 340],
    [665, 340],
    [665, 475],
    [590, 475],
    [590, 565],
    [330, 565],
  ],
  [
    [690, 340],
    [965, 340],
    [965, 565],
    [690, 565],
  ],
];
export function createSampleMaps(): FloorAccessMapDto[] {
  return sampleSites
    .flatMap((s) => s.floors)
    .map((floor, index) => ({
      floorId: floor.floorId,
      siteId: index === 3 ? "aarhus" : "copenhagen",
      floorName: floor.name,
      planWidth: 1000,
      planHeight: 600,
      areas: (rooms[index] ?? []).map((name, j) => {
        const polygon = (layouts[j] ?? []).map(([x, y]) => ({ x: x!, y: y! }));
        const first = polygon[j < 3 ? polygon.length - 2 : 0]!;
        const next = polygon[j < 3 ? polygon.length - 1 : 1]!;
        return {
          areaId: `${floor.floorId}-${j}`,
          name,
          code: `${floor.floorId.toUpperCase()}-${j + 1}`,
          securityLevelName: j === 3 ? "Restricted" : j === 2 ? "High" : "Low",
          polygon,
          state: j === 0 || j === 4 ? "HasAccess" : j === 2 ? "Pending" : j === 5 ? "Unknown" : "NoAccess",
          reason: j === 0 || j === 4 ? "Confirmed" : j === 2 ? "AwaitingApproval" : j === 5 ? "ConnectorOffline" : "NoEntitlement",
          cardInactive: false,
          since: null,
          doors: [{ doorId: `door-${floor.floorId}-${j}`, name: `${name} door`, x: (first.x + next.x) / 2, y: (first.y + next.y) / 2 }],
        };
      }),
    }));
}
const start = new Date();
const end = new Date(start);
end.setDate(end.getDate() + 7);
export function createSampleRequests(): AccessRequestDto[] {
  return [
    {
      entitlementId: "request-1",
      subject: sampleMe.person!,
      requestedBy: sampleMe.person!,
      area: { areaId: "ground-2", name: "Collaboration hub", siteName: "Copenhagen headquarters", floorName: "Ground floor" },
      requestedStart: start.toISOString(),
      requestedEnd: end.toISOString(),
      effectiveEnd: end.toISOString(),
      cappedToEligibility: false,
      justification: "Collaboration sessions with the product team for the new workspace project.",
      status: "PendingApproval",
      steps: [
        {
          stepId: "step-1",
          order: 1,
          approverKind: "Manager",
          status: "Approved",
          assignedApprover: { personId: "mikkel", displayName: "Mikkel Holm" },
          decidedBy: { personId: "mikkel", displayName: "Mikkel Holm" },
          decidedAt: start.toISOString(),
          comment: null,
        },
        {
          stepId: "step-2",
          order: 2,
          approverKind: "AreaOwner",
          status: "Pending",
          assignedApprover: null,
          decidedBy: null,
          decidedAt: null,
          comment: null,
        },
      ],
      fulfillmentStatus: null,
      createdAt: start.toISOString(),
      revocation: null,
      revocationFulfillmentStatus: null,
    },
    {
      entitlementId: "request-2",
      subject: sampleMe.person!,
      requestedBy: sampleMe.person!,
      area: { areaId: "ground-0", name: "Welcome lounge", siteName: "Copenhagen headquarters", floorName: "Ground floor" },
      requestedStart: start.toISOString(),
      requestedEnd: end.toISOString(),
      effectiveEnd: end.toISOString(),
      cappedToEligibility: false,
      justification: "Daily access to the headquarters reception and welcome area.",
      status: "Approved",
      steps: [
        {
          stepId: "step-3",
          order: 1,
          approverKind: "Manager",
          status: "Approved",
          assignedApprover: null,
          decidedBy: { personId: "mikkel", displayName: "Mikkel Holm" },
          decidedAt: start.toISOString(),
          comment: null,
        },
      ],
      fulfillmentStatus: "Confirmed",
      createdAt: start.toISOString(),
      revocation: null,
      revocationFulfillmentStatus: null,
    },
  ];
}
export function createSampleInbox(): ApprovalInboxItemDto[] {
  return [
    {
      entitlementId: "inbox-1",
      stepId: "review-1",
      stepOrder: 2,
      approverKind: "AreaOwner",
      subject: { personId: "anna", displayName: "Anna Larsen" },
      requestedBy: { personId: "anna", displayName: "Anna Larsen" },
      area: { areaId: "first-2", name: "Research lab", siteName: "Copenhagen headquarters", floorName: "First floor" },
      securityLevelName: "High",
      requestedStart: start.toISOString(),
      requestedEnd: end.toISOString(),
      effectiveEnd: end.toISOString(),
      cappedToEligibility: false,
      justification:
        "I’m joining the materials research team for the next sprint. I’ll need access to prepare and run the scheduled experiments.",
      createdAt: start.toISOString(),
    },
    {
      entitlementId: "inbox-2",
      stepId: "review-2",
      stepOrder: 1,
      approverKind: "Manager",
      subject: { personId: "oscar", displayName: "Oscar Nielsen" },
      requestedBy: { personId: "oscar", displayName: "Oscar Nielsen" },
      area: { areaId: "first-0", name: "Design studio", siteName: "Copenhagen headquarters", floorName: "First floor" },
      securityLevelName: "Low",
      requestedStart: start.toISOString(),
      requestedEnd: end.toISOString(),
      effectiveEnd: end.toISOString(),
      cappedToEligibility: false,
      justification: "Working on the new wayfinding concept with the design team. Requesting access for our upcoming workshop sessions.",
      createdAt: start.toISOString(),
    },
  ];
}
export function sampleDetails(maps: FloorAccessMapDto[], id: string): AreaDetailsDto | null {
  const m = maps.find((m) => m.areas.some((a) => a.areaId === id));
  const a = m?.areas.find((a) => a.areaId === id);
  return a && m
    ? {
        areaId: id,
        name: a.name,
        code: a.code,
        floorId: m.floorId,
        siteId: m.siteId,
        securityLevel: {
          securityLevelId: "level",
          name: a.securityLevelName,
          approverChain: a.securityLevelName === "High" || a.securityLevelName === "Restricted" ? "Both" : "Manager",
          maxDurationDays: 30,
        },
        owners: [{ personId: "owner", displayName: "Mikkel Holm" }],
      }
    : null;
}
