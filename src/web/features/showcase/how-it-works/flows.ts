/**
 * The two real flows of Gatehouse, named after the components that implement them (docs/codemap.md, as-built M1-M9).
 * Nothing here is invented: each step is a class, route or process that exists in the repository.
 */
export interface FlowStep {
  id: string;
  title: string;
  component: string;
  detail: string;
}

export const GOVERNANCE_LANE: readonly FlowStep[] = [
  {
    id: "request",
    title: "Request on the map",
    component: "Web /map → POST /api/v1/access-requests",
    detail: "An employee (or manager/sponsor) asks for an area for a time window.",
  },
  {
    id: "route",
    title: "Approval routing",
    component: "Api RequestAccessHandler + ApproverResolver",
    detail: "The security level decides the chain (manager, area owner or both). Nobody approves their own request.",
  },
  {
    id: "approve",
    title: "Approvals",
    component: "Api ApproveStepHandler",
    detail: "Each step is decided in order; the last approval makes the entitlement Approved (not yet applied).",
  },
  {
    id: "outbox",
    title: "Outbox → RabbitMQ",
    component: "Outbox (same DB transaction) → OutboxRelay → MassTransit",
    detail: "Domain events are stored with the change and published afterwards, so nothing is lost.",
  },
  {
    id: "dispatch",
    title: "Fulfillment command",
    component: "Workers DispatchFulfillmentHandler → ConnectorCommand",
    detail: "A command per PACS instance, ordered per person, idempotent.",
  },
  {
    id: "poll",
    title: "Agent long-poll",
    component: "ConnectorGateway /connector/v1/poll (outbound HTTPS, mTLS)",
    detail: "The on-site agent pulls work; nothing connects into the customer network.",
  },
  {
    id: "adapter",
    title: "Adapter writes the PACS",
    component: "Agent CommandExecutor → LegacyPACS integration table / CloudPACS REST",
    detail: "The vendor-specific adapter writes the access level for the card.",
  },
  {
    id: "ack",
    title: "Ack → confirmed",
    component: "ConnectorGateway /connector/v1/acks → fulfillment Confirmed",
    detail: "Only the PACS ack turns the map green: approved is not the same as applied.",
  },
];

export const DOOR_LANE: readonly FlowStep[] = [
  { id: "badge", title: "Badge at the reader", component: "Reader (In/Out) on a door", detail: "A person presents a card at a door." },
  {
    id: "decide",
    title: "Controller decides locally",
    component: "PACS controller (its own access-level database)",
    detail: "The PACS grants or denies by itself. Gatehouse is not in the decision path.",
  },
  {
    id: "pump",
    title: "Agent reads the event",
    component: "Agent event pump (adapter ReadEvents / CloudPACS webhook)",
    detail: "The agent normalizes the vendor event into a DoorEvent.",
  },
  {
    id: "events",
    title: "Gateway receives events",
    component: "ConnectorGateway /connector/v1/events → RabbitMQ",
    detail: "Batches are deduplicated by event id.",
  },
  {
    id: "ingest",
    title: "Ingest",
    component: "DoorEventIngestConsumer → door_events + Redis door state",
    detail: "Door state machine, alarms, live drift check and occupancy counters.",
  },
  {
    id: "signalr",
    title: "Live floor plan",
    component: "SignalR group tenant:{t}:site:{s} → /live",
    detail: "Security staff see the swipe, the door state and alarms in real time.",
  },
];

export const SIGN_IN_NOTE: FlowStep = {
  id: "sign-in",
  title: "Sign-in only",
  component: "Keycloak (OIDC / SAML broker) → BFF session",
  detail: "Keycloak is used when people sign in to the web app. It never takes part in a door decision.",
};
