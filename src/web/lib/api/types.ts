/** Hand-written mirrors of the C# DTO records (camelCase, field-for-field). */

/** Mirrors Gatehouse.Application.Identity.MePersonDto. */
export interface MePersonDto {
  personId: string;
  displayName: string;
  type: string;
}

/** Mirrors Gatehouse.Application.Identity.MeDto. */
export interface MeDto {
  subject: string;
  email: string;
  displayName: string;
  tenantId: string;
  tenantName: string;
  person: MePersonDto | null;
  roles: string[];
  onboardingPending: boolean;
}

/** Mirrors Gatehouse.Application.Sites.Dtos.FloorSummaryDto. */
export interface FloorSummaryDto {
  floorId: string;
  name: string;
  level: number;
}

/** Mirrors Gatehouse.Application.Sites.Dtos.SiteDto. */
export interface SiteDto {
  siteId: string;
  name: string;
  code: string;
  timeZoneId: string;
  floors: FloorSummaryDto[];
}

export type AreaAccessState = "HasAccess" | "NoAccess" | "Pending" | "Unknown";

export type AreaAccessReason =
  | "AreaNotMapped"
  | "ConnectorOffline"
  | "Confirmed"
  | "RevocationPending"
  | "AwaitingApproval"
  | "AwaitingFulfillment"
  | "ScheduledStart"
  | "OutsideWindow"
  | "NoEntitlement";

/** Mirrors Gatehouse.Application.Access.Dtos.PointDto. */
export interface PointDto {
  x: number;
  y: number;
}

/** Mirrors Gatehouse.Application.Access.Dtos.DoorMarkerDto. */
export interface DoorMarkerDto {
  doorId: string;
  name: string;
  x: number;
  y: number;
}

/** Mirrors Gatehouse.Application.Access.Dtos.AreaAccessDto. */
export interface AreaAccessDto {
  areaId: string;
  name: string;
  code: string;
  polygon: PointDto[];
  securityLevelName: string;
  state: AreaAccessState;
  reason: AreaAccessReason;
  cardInactive: boolean;
  since: string | null;
  doors: DoorMarkerDto[];
}

/** Mirrors Gatehouse.Application.Access.Dtos.FloorAccessMapDto. */
export interface FloorAccessMapDto {
  floorId: string;
  siteId: string;
  floorName: string;
  planWidth: number;
  planHeight: number;
  areas: AreaAccessDto[];
  /** C# default parameter (M10-09), optional like `LiveFloorDto.occupancy`. */
  hasBackground?: boolean;
  backgroundUpdatedAt?: string | null;
}

/** Mirrors Gatehouse.Application.People.Dtos.PersonRefDto. */
export interface PersonRefDto {
  personId: string;
  displayName: string;
}

/** Mirrors Gatehouse.Application.Sites.Dtos.SecurityLevelDto. */
export interface SecurityLevelDto {
  securityLevelId: string;
  name: string;
  approverChain: "Manager" | "AreaOwner" | "Both";
  maxDurationDays: number;
}

/** Mirrors Gatehouse.Application.Sites.Dtos.AreaDetailsDto. */
export interface AreaDetailsDto {
  areaId: string;
  name: string;
  code: string;
  floorId: string;
  siteId: string;
  securityLevel: SecurityLevelDto;
  owners: PersonRefDto[];
}

/** Mirrors Gatehouse.Application.Access.Dtos.CreateAccessRequestDto. */
export interface CreateAccessRequestDto {
  personId: string | null;
  areaId: string;
  requestedStart: string;
  requestedEnd: string;
  justification: string;
}

/** Mirrors Gatehouse.Application.Access.Dtos.AreaRefDto. */
export interface AreaRefDto {
  areaId: string;
  name: string;
  siteName: string;
  floorName: string;
}

/** Mirrors Gatehouse.Application.Access.Dtos.ApprovalStepDto. */
export interface ApprovalStepDto {
  stepId: string;
  order: number;
  approverKind: "Manager" | "AreaOwner";
  status: "Waiting" | "Pending" | "Approved" | "Rejected" | "Closed";
  assignedApprover: PersonRefDto | null;
  decidedBy: PersonRefDto | null;
  decidedAt: string | null;
  comment: string | null;
}

/** Mirrors Gatehouse.Application.Access.Dtos.AccessRequestDto. */
export interface AccessRequestDto {
  entitlementId: string;
  subject: PersonRefDto;
  requestedBy: PersonRefDto;
  area: AreaRefDto;
  requestedStart: string;
  requestedEnd: string;
  effectiveEnd: string;
  cappedToEligibility: boolean;
  justification: string;
  status: "PendingApproval" | "Approved" | "Rejected" | "Cancelled" | "Revoked" | "Expired";
  steps: ApprovalStepDto[];
  fulfillmentStatus: string | null;
  createdAt: string;
  revocation: RevocationDto | null;
  revocationFulfillmentStatus: string | null;
}

/** Mirrors Gatehouse.Application.Access.Dtos.ApprovalInboxItemDto. */
export interface ApprovalInboxItemDto {
  entitlementId: string;
  stepId: string;
  stepOrder: number;
  approverKind: "Manager" | "AreaOwner";
  subject: PersonRefDto;
  requestedBy: PersonRefDto;
  area: AreaRefDto;
  securityLevelName: string;
  requestedStart: string;
  requestedEnd: string;
  effectiveEnd: string;
  cappedToEligibility: boolean;
  justification: string;
  createdAt: string;
}

/** Mirrors Gatehouse.Application.Access.Dtos.DecideStepDto. */
export interface DecideStepDto {
  comment: string | null;
}

export type FulfillmentStatus = "Desired" | "Pending" | "Confirmed" | "Failed" | "Drift" | "Superseded";
export type FulfillmentKind = "Grant" | "Revoke";
export type FulfillmentPriority = "Normal" | "High";

/** Mirrors Gatehouse.Application.Fulfillment.Dtos.FulfillmentQueueItemDto. */
export interface FulfillmentQueueItemDto {
  fulfillmentId: string;
  entitlementId: string;
  kind: FulfillmentKind;
  status: FulfillmentStatus;
  priority: FulfillmentPriority;
  revision: number;
  subject: PersonRefDto;
  area: AreaRefDto;
  pacsInstanceName: string;
  vendorAccessLevelRef: string;
  entitlementStatus: string;
  lastError: string | null;
  createdAt: string;
  updatedAt: string;
}

/** Mirrors Gatehouse.Application.Fulfillment.Dtos.DesiredStateDto. */
export interface DesiredStateDto {
  action: string;
  personRef: string;
  personName: string;
  vendorAccessLevelRef: string;
  accessLevelDisplayName: string;
  validFrom: string;
  validTo: string;
  pacsInstanceName: string;
  vendor: string;
}

/** Mirrors Gatehouse.Application.Fulfillment.Dtos.ConfirmedStateDto. */
export interface ConfirmedStateDto {
  confirmedByKind: string;
  confirmedById: string;
  confirmedByName: string | null;
  confirmedAt: string;
  pacsRef: string | null;
}

/** Mirrors Gatehouse.Application.Audit.Dtos.AuditEntryDto. */
export interface AuditEntryDto {
  sequence: number;
  occurredAt: string;
  actorKind: string;
  actorId: string;
  actorName: string | null;
  action: string;
  subjectType: string;
  subjectId: string;
  payload: unknown;
  hash: string;
}

/** Mirrors Gatehouse.Application.Fulfillment.Dtos.FulfillmentDetailDto. */
export interface FulfillmentDetailDto {
  item: FulfillmentQueueItemDto;
  desired: DesiredStateDto;
  confirmed: ConfirmedStateDto | null;
  history: AuditEntryDto[];
  connectorManaged: boolean;
  commandStatus: string | null;
  commandAttempts: number | null;
  nextAttemptAt: string | null;
  lastError: string | null;
  capabilityGaps?: CapabilityGap[] | null;
}

/** Mirrors Gatehouse.Application.Fulfillment.Dtos.FailFulfillmentDto. */
export interface FailFulfillmentDto {
  reason: string | null;
}

/** Mirrors Gatehouse.Application.Audit.Dtos.AuditVerificationDto. */
export interface AuditVerificationDto {
  verified: boolean;
  entryCount: number;
  lastSequence: number | null;
  firstInvalidSequence: number | null;
  verifiedAt: string;
}

export type StaffTaskKind =
  "FulfillmentFailed" | "RevocationUnconfirmed" | "ReconciliationFinding" | "DriftAlert" | "PinReveal" | "ScheduledRemovalRefused";
export type StaffTaskStatus = "Open" | "Claimed" | "Resolved";

/** Mirrors Gatehouse.Application.Tasks.Dtos.StaffTaskDto. */
export interface StaffTaskDto {
  taskId: string;
  kind: string;
  status: string;
  siteId: string | null;
  siteName: string | null;
  subjectType: string;
  subjectId: string;
  summary: string;
  assignedTo: PersonRefDto | null;
  resolutionAction: string | null;
  resolutionNote: string | null;
  resolvedBy: PersonRefDto | null;
  resolvedAt: string | null;
  createdAt: string;
  findingKind?: string | null;
  personName?: string | null;
}

/** Mirrors Gatehouse.Application.Tasks.Dtos.ResolveStaffTaskDto. */
export interface ResolveStaffTaskDto {
  action: string;
  note: string | null;
}

/** Mirrors Gatehouse.Application.Access.Dtos.RevocationDto. */
export interface RevocationDto {
  revokedBy: PersonRefDto | null;
  revokedAt: string;
  reason: string;
  kind: "Normal" | "Emergency";
}

/** Mirrors Gatehouse.Application.Access.Dtos.RevokeEntitlementDto. */
export interface RevokeEntitlementDto {
  reason: string;
}

/** Mirrors Gatehouse.Application.Access.Dtos.AreaRosterEntryDto. */
export interface AreaRosterEntryDto {
  entitlementId: string;
  subject: PersonRefDto;
  personType: string;
  requestedStart: string;
  effectiveEnd: string;
  status: string;
  fulfillmentStatus: string | null;
  revocationFulfillmentStatus: string | null;
  revokedAt: string | null;
}

/** Mirrors Gatehouse.Application.Access.Dtos.AreaRosterDto. */
export interface AreaRosterDto {
  area: AreaRefDto;
  canRevoke: boolean;
  canEmergencyRevoke: boolean;
  entries: AreaRosterEntryDto[];
}

// ---- Live building (M5) ----

export type DoorState = "Locked" | "Unlocked" | "Open" | "Closed" | "HeldOpen" | "ForcedOpen" | "Lockdown" | "ScheduledUnlocked";
export type DoorEventType =
  | "AccessGranted"
  | "AccessDenied"
  | "DoorOpened"
  | "DoorClosed"
  | "DoorHeldOpen"
  | "DoorForced"
  | "LockdownStarted"
  | "LockdownCleared"
  | "ScheduledUnlockStarted"
  | "ScheduledUnlockEnded"
  | "DoorUnlocked"
  | "DoorRelocked";
export type ReaderDirection = "In" | "Out";
export type AlarmKind = "Forced" | "HeldOpen" | "UnauthorizedAccess";
export type FulfillmentMode = "Manual" | "Connector";
export type LiveUpdateKind = "DoorEvent" | "DoorState" | "Alarm" | "ConnectorHealth" | "Occupancy";

/** Mirrors Gatehouse.Application.LiveBuilding.Dtos.OccupancyDto (approximate, in/out readers). */
export interface OccupancyDto {
  areaId: string;
  count: number;
}
export type DoorCommandKind = "MomentaryUnlock" | "LockdownStart" | "LockdownClear";
export type DoorCommandScope = "Door" | "Area" | "Floor" | "Site";

/** Mirrors Gatehouse.Application.LiveBuilding.GetLiveFloor.LiveAreaDto. */
export interface LiveAreaDto {
  areaId: string;
  name: string;
  code: string;
  polygon: PointDto[];
}

/** Mirrors Gatehouse.Application.LiveBuilding.GetLiveFloor.LiveReaderDto. */
export interface LiveReaderDto {
  readerId: string;
  name: string;
  position: PointDto;
}

/** Mirrors Gatehouse.Application.LiveBuilding.GetLiveFloor.LiveDoorDto. */
export interface LiveDoorDto {
  doorId: string;
  areaId: string;
  name: string;
  position: PointDto;
  pacsDoorRef: string | null;
  connectorManaged: boolean;
  state: DoorState;
  stateSince: string | null;
  readers: LiveReaderDto[];
  canUnlock: boolean;
  canLockdown: boolean;
}

/** Mirrors Gatehouse.Application.LiveBuilding.Dtos.LiveDoorEventDto. */
export interface LiveDoorEventDto {
  eventId: string;
  occurredAt: string;
  type: DoorEventType;
  doorId: string | null;
  doorName: string | null;
  areaId: string | null;
  areaName: string | null;
  readerName: string | null;
  personId: string | null;
  personDisplayName: string | null;
  direction: ReaderDirection | null;
}

/** Mirrors Gatehouse.Application.LiveBuilding.Dtos.AlarmDto. */
export interface AlarmDto {
  alarmId: string;
  doorId: string;
  doorName: string;
  areaId: string;
  areaName: string;
  siteId: string;
  kind: AlarmKind;
  raisedAt: string;
  doorEventId: string;
  acknowledgedAt: string | null;
  acknowledgedByName: string | null;
  comment: string | null;
}

/** Mirrors Gatehouse.Application.LiveBuilding.Dtos.LiveFloorDto. */
export interface LiveFloorDto {
  floorId: string;
  siteId: string;
  siteName: string;
  floorName: string;
  planWidth: number;
  planHeight: number;
  areas: LiveAreaDto[];
  doors: LiveDoorDto[];
  openAlarms: AlarmDto[];
  recentEvents: LiveDoorEventDto[];
  occupancy?: OccupancyDto[] | null;
  /** C# default parameter (M10-09). */
  hasBackground?: boolean;
  backgroundUpdatedAt?: string | null;
}

/** Mirrors Gatehouse.Application.LiveBuilding.Dtos.DoorStateDto. */
export interface DoorStateDto {
  doorId: string;
  state: DoorState;
  since: string;
  lastEventAt: string;
}

/** Capability a PACS instance lacks (adapter-interface.md §6; Gatehouse.Application.Connectors.PacsCapabilityGaps). */
export type CapabilityGap = "NativeValidity" | "PinWrite" | "PhotoWrite" | "RecordExport" | "MomentaryUnlock" | "Lockdown";

/** Mirrors Gatehouse.Application.LiveBuilding.Dtos.ConnectorHealthDto. */
export interface ConnectorHealthDto {
  pacsInstanceId: string;
  name: string;
  vendor: string;
  fulfillmentMode: FulfillmentMode;
  online: boolean;
  lastHeartbeatAt: string | null;
  agentVersion: string | null;
  agentQueueDepth: number | null;
  queuedCommands: number;
  lastAckLatencyMs: number | null;
  pacsReachable: boolean | null;
  capabilityGaps: CapabilityGap[] | null;
}

/** Mirrors Gatehouse.Application.LiveBuilding.Dtos.LiveUpdateDto (SignalR method "live"). */
export interface LiveUpdateDto {
  kind: LiveUpdateKind;
  siteId: string;
  doorEvent: LiveDoorEventDto | null;
  doorState: DoorStateDto | null;
  alarm: AlarmDto | null;
  connector: ConnectorHealthDto | null;
  occupancy?: OccupancyDto | null;
}

/** Mirrors Gatehouse.Application.LiveBuilding.RequestDoorCommand.RequestDoorCommandCommand. */
export interface RequestDoorCommandDto {
  kind: DoorCommandKind;
  scope: DoorCommandScope;
  scopeId: string;
  reason: string | null;
  durationSeconds: number | null;
}

/** Mirrors Gatehouse.Application.LiveBuilding.RequestDoorCommand.DoorCommandResultDto. */
export interface DoorCommandResultDto {
  doorCommandId: string;
  doorCount: number;
  skippedDoorCount: number;
}

/** Mirrors Gatehouse.Api AcknowledgeAlarmRequest. */
export interface AcknowledgeAlarmDto {
  comment: string | null;
}

/** Mirrors Gatehouse.Application.Reconciliation.Dtos.ReconciliationReportSummaryDto. */
export interface ReconciliationReportSummaryDto {
  reportId: string;
  pacsInstanceId: string;
  pacsInstanceName: string;
  status: "Requested" | "Exporting" | "Comparing" | "Completed" | "Failed";
  requestedAt: string;
  requestedBy: PersonRefDto | null;
  capturedAt: string | null;
  comparedAt: string | null;
  findingCount: number | null;
  comparedRecordCount: number | null;
  inSyncCount: number | null;
}

/** Mirrors Gatehouse.Application.Reconciliation.Dtos.ReconciliationInstanceDto. */
export interface ReconciliationInstanceDto {
  pacsInstanceId: string;
  name: string;
  vendor: string;
  fulfillmentMode: string;
  siteNames: string[];
  canRun: boolean;
  blockedReason: string | null;
  lastReport: ReconciliationReportSummaryDto | null;
}

/** Mirrors Gatehouse.Application.Reconciliation.Dtos.ReconciliationNamedRefDto. */
export interface ReconciliationNamedRefDto {
  id: string;
  name: string;
}

/** Mirrors Gatehouse.Application.Reconciliation.Dtos.ReconciliationCoverageDto. */
export interface ReconciliationCoverageDto {
  sites: ReconciliationNamedRefDto[];
  areas: ReconciliationNamedRefDto[];
  recordTypes: string[];
  exportedRecordCount: number;
  comparedRecordCount: number;
  inSyncCount: number;
  skippedInFlightCount: number;
  ignoredInactiveCount: number;
}

/** Mirrors Gatehouse.Domain.Reconciliation.FindingValues. */
export interface FindingValues {
  cardNumberMasked: string | null;
  validFrom: string | null;
  validTo: string | null;
}

/** Mirrors Gatehouse.Application.Reconciliation.Dtos.ReconciliationFindingDto. */
export interface ReconciliationFindingDto {
  findingId: string;
  kind: "MissingInPacs" | "ExtraInPacs" | "Mismatch" | "ExpiredStillPresent";
  person: PersonRefDto | null;
  personRef: string | null;
  areaId: string | null;
  areaName: string | null;
  vendorAccessLevelRef: string;
  pacsRef: string | null;
  differences: string[];
  expected: FindingValues | null;
  actual: FindingValues | null;
  staffTaskId: string;
  taskStatus: string;
  resolutionAction: string | null;
}

/** Mirrors Gatehouse.Application.Reconciliation.Dtos.ReconciliationReportDto. */
export interface ReconciliationReportDto {
  summary: ReconciliationReportSummaryDto;
  coverage: ReconciliationCoverageDto | null;
  failureCode: string | null;
  failureMessage: string | null;
  findings: ReconciliationFindingDto[];
}

/** Mirrors Gatehouse.Application.People.Dtos.PersonSummaryDto. */
export interface PersonSummaryDto {
  personId: string;
  displayName: string;
  email: string;
  type: "Employee" | "External" | "Visitor" | "Guest";
  source: "Authoritative" | "Local";
  status: "Active" | "Terminated";
  eligibilityStart: string;
  eligibilityEnd: string | null;
  sponsor: PersonRefDto | null;
  manager: PersonRefDto | null;
}

/** Mirrors Gatehouse.Application.Credentials.Dtos.CardDto. */
export interface CardDto {
  cardId: string;
  personId: string;
  cardNumberMasked: string;
  state: "Assigned" | "ActivationPending" | "Active" | "Deactivated" | "Revoked";
  stateChangedAt: string;
  pacsStatus: string | null;
}

/** Mirrors Gatehouse.Application.People.Dtos.PersonEntitlementDto. */
export interface PersonEntitlementDto {
  entitlementId: string;
  areaName: string;
  status: string;
  effectiveEnd: string;
  revokeFulfillmentStatuses: string[];
  /**
   * Revoke endpoint the caller may use for an Approved entitlement: "revoke" (area owner),
   * "emergency-revoke" (security staff), else null.
   */
  revokeAction?: "revoke" | "emergency-revoke" | null;
}

/** Mirrors Gatehouse.Application.People.Dtos.PersonDetailDto. */
export interface PersonDetailDto {
  summary: PersonSummaryDto;
  cards: CardDto[];
  canManage: boolean;
  canActivateCards: boolean;
  canIssueMagicLink: boolean;
  entitlements: PersonEntitlementDto[];
  canIssueCards?: boolean;
  canDeactivateCards?: boolean;
  canTerminate?: boolean;
}

/** Mirrors Gatehouse.Application.People.Dtos.OnboardPersonDto (request of POST /v1/people). */
export interface OnboardPersonDto {
  type: "External" | "Visitor" | "Guest";
  givenName: string;
  familyName: string;
  email: string;
  start: string;
  end: string;
  timeZoneId: string;
  sponsorId: string | null;
  cardNumber: string | null;
}

/** Mirrors Gatehouse.Application.Identity.MagicLinks.IssuedMagicLinkDto (shown once). */
export interface MagicLinkIssuedDto {
  url: string;
  expiresAt: string;
}

/** Mirrors Gatehouse.Application.Credentials.RequestPinReveal.PinRevealRequestedDto. */
export interface PinRevealRequestedDto {
  taskId: string;
}

/** Mirrors Gatehouse.Application.Credentials.RevealPin.RevealedPinDto (never cached). */
export interface RevealedPinDto {
  pin: string;
  maskAfterSeconds: number;
}

/** Mirrors Gatehouse.Application.People.Lifecycle.LifecycleSettingsDto. */
export interface LifecycleSettingsDto {
  maxSponsorshipDays: number;
  maxVisitorDays: number;
  renewalEmployeeDays: number;
  renewalExternalDays: number;
  renewalGuestDays: number;
  renewalLeadDays: number;
}

/** Mirrors Gatehouse.Application.People.Lifecycle.LifecycleRunResult. */
export interface LifecycleRunResult {
  terminated: number;
  expired: number;
  renewalsRequested: number;
}

/** Mirrors Gatehouse.Application.People.HrSync.HrSyncResult. */
export interface HrSyncResult {
  created: number;
  updated: number;
  unchanged: number;
  skipped: number;
}

/** Mirrors Gatehouse.Application.Theming.ThemeDto (map state colors are never part of it). */
export interface ThemeDto {
  primary: string;
  primaryForeground: string;
  accent: string;
  accentForeground: string;
  background: string;
  foreground: string;
  radiusPx: number;
  terms: Partial<Record<TermKey, string>>;
}

export type TermKey = "Area" | "Site" | "Floor" | "Door";

/** Mirrors Gatehouse.Application.People.Retention.RetentionSettingsDto. */
export interface RetentionSettingsDto {
  photoDaysAfterTermination: number;
  personalDataDaysAfterTermination: number;
  doorEventDays: number;
}

/** Mirrors Gatehouse.Application.People.Retention.RetentionRunResult. */
export interface RetentionRunResult {
  photosDeleted: number;
  peopleAnonymized: number;
}

/** Mirrors Gatehouse.Application.Dashboard.DashboardDto. */
export interface DashboardDto {
  generatedAt: string;
  pendingApprovals: number;
  openAlarms: number;
  openTasksByKind: Record<string, number>;
  openFindingTasks: number;
  openDriftAlerts: number;
  unconfirmedFulfillmentsOverOneHour: number;
  reconciliation: DashboardCoverageDto[];
  connectors: { online: number; offline: number; neverSeen: number };
}

/** Mirrors Gatehouse.Application.Dashboard.DashboardCoverageDto. */
export interface DashboardCoverageDto {
  pacsInstanceId: string;
  name: string;
  comparedAt: string | null;
  comparedRecords: number | null;
  inSync: number | null;
  findings: number | null;
}

// ---- M10 platform (Gatehouse.Application.Platform.Dtos; enums in Gatehouse.Domain) ----

export type TenantStatus = "Active" | "Suspended" | "Deprovisioned" | "Provisioning" | "ProvisioningFailed";
export type IdpKind = "Oidc" | "Saml" | "Local";
export type ProvisioningStep = "Database" | "Migrate" | "Baseline" | "FirstAdmin" | "Organization" | "AdminInvite" | "Completed";
export type ProvisioningState = "Running" | "Failed" | "Completed";
export type PlatformAlertKind =
  "ProvisioningFailed" | "ConnectorOffline" | "CertificateExpiring" | "EnrollmentTokenExpired" | "StatisticsStale" | "AdminNotActivated";
export type AlertSeverity = "Info" | "Warning" | "Error";
export type ConnectorStatus = "PendingEnrollment" | "Active" | "Revoked";

/** Mirrors Gatehouse.Application.Platform.Dtos.PlatformOperatorDto. */
export interface PlatformOperatorDto {
  id: string;
  email: string;
  displayName: string;
}

/** Mirrors Gatehouse.Application.Platform.Dtos.CreateTenantRequest. */
export interface CreateTenantRequest {
  tenantId: string;
  displayName: string;
  emailDomain: string;
  adminEmail: string;
  adminGivenName: string;
  adminFamilyName: string;
  timeZoneId: string;
}

/** Mirrors Gatehouse.Application.Platform.Dtos.SuspendTenantRequest. */
export interface SuspendTenantRequest {
  reason: string;
}

/** Mirrors Gatehouse.Application.Platform.Dtos.PlatformTenantSummaryDto. */
export interface PlatformTenantSummaryDto {
  tenantId: string;
  displayName: string;
  status: TenantStatus;
  emailDomain: string | null;
  idpKind: IdpKind;
  provisioningStep: ProvisioningStep | null;
  people: number;
  sites: number;
  connectorsOnline: number;
  connectorsOffline: number;
  alertCount: number;
  createdAt: string;
}

/** Mirrors Gatehouse.Application.Platform.Dtos.TenantProvisioningDto. */
export interface TenantProvisioningDto {
  step: ProvisioningStep;
  state: ProvisioningState;
  attempts: number;
  lastError: string | null;
  startedAt: string;
  completedAt: string | null;
  adminInvitedAt: string | null;
  adminActivatedAt: string | null;
}

/** Mirrors Gatehouse.Application.Platform.Dtos.TenantStatisticsDto (counts only). */
export interface TenantStatisticsDto {
  people: number;
  employees: number;
  tenantAdmins: number;
  sites: number;
  floors: number;
  areas: number;
  doors: number;
  pacsInstances: number;
  connectorsOnline: number;
  connectorsOffline: number;
  connectorsNeverSeen: number;
  activeEntitlements: number;
  pendingRequests: number;
  openAlarms: number;
  unconfirmedFulfillments: number;
  linkedPeople: number;
  onboardingCompletedAt: string | null;
  computedAt: string;
}

/** Mirrors Gatehouse.Application.Platform.Dtos.PlatformConnectorDto. */
export interface PlatformConnectorDto {
  connectorId: string;
  displayName: string;
  status: ConnectorStatus;
  lastSeenAt: string | null;
  certificateNotAfter: string | null;
}

/** Mirrors Gatehouse.Application.Platform.Dtos.PlatformAlertDto. */
export interface PlatformAlertDto {
  kind: PlatformAlertKind;
  severity: AlertSeverity;
  tenantId: string;
  message: string;
  since: string;
}

/** Mirrors Gatehouse.Application.Platform.Dtos.PlatformTenantDto. */
export interface PlatformTenantDto extends PlatformTenantSummaryDto {
  provisioning: TenantProvisioningDto | null;
  statistics: TenantStatisticsDto | null;
  connectors: PlatformConnectorDto[];
  alerts: PlatformAlertDto[];
  suspendedAt: string | null;
  suspendedReason: string | null;
}

/** Mirrors Gatehouse.Application.Platform.Dtos.PlatformAuditEntryDto. */
export interface PlatformAuditEntryDto {
  sequence: number;
  occurredAt: string;
  actorKind: string;
  actorId: string;
  action: string;
  tenantId: string | null;
  data: unknown;
}

// ---- M10 tenant admin: identity provider (Gatehouse.Application.Identity.IdentityProviders) ----

/** Mirrors OidcSettingsRequest; `clientSecret` null on update keeps the stored secret. */
export interface OidcSettingsRequest {
  discoveryUrl: string;
  clientId: string;
  clientSecret: string | null;
  scopes: string[] | null;
}

/** Mirrors SamlSettingsRequest (exactly one of the two). */
export interface SamlSettingsRequest {
  metadataUrl: string | null;
  metadataXml: string | null;
}

/** Mirrors SaveIdentityProviderRequest. */
export interface SaveIdentityProviderRequest {
  kind: IdpKind;
  displayName: string;
  oidc: OidcSettingsRequest | null;
  saml: SamlSettingsRequest | null;
}

/** Mirrors IdentityProviderDto. */
export interface IdentityProviderDto {
  kind: IdpKind;
  alias: string;
  displayName: string;
  discoveryUrl: string | null;
  issuer: string | null;
  clientId: string | null;
  hasClientSecret: boolean;
  samlEntityId: string | null;
  samlSsoUrl: string | null;
  signingCertificateThumbprint: string | null;
  emailDomain: string;
  brokerRedirectUri: string;
  spEntityId: string | null;
  savedAt: string;
  lastTestedAt: string | null;
  lastTestOk: boolean | null;
}

/** Mirrors IdpTestCheckDto. */
export interface IdpTestCheckDto {
  name: string;
  ok: boolean;
  code: string | null;
  detail: string | null;
}

/** Mirrors IdpTestResultDto. */
export interface IdpTestResultDto {
  ok: boolean;
  checks: IdpTestCheckDto[];
}

// ---- M10 tenant admin: people and roles (Gatehouse.Application.People.Dtos) ----

export type AssignedRole = "SecurityStaff" | "TenantAdmin";

/** Mirrors CreateEmployeeRequest. */
export interface CreateEmployeeRequest {
  email: string;
  givenName: string;
  familyName: string;
  managerId: string | null;
  timeZoneId: string | null;
}

/** Mirrors PeopleImportErrorDto. */
export interface PeopleImportErrorDto {
  line: number;
  code: string;
  field: string | null;
}

/** Mirrors PeopleImportResultDto. */
export interface PeopleImportResultDto {
  dryRun: boolean;
  created: number;
  updated: number;
  unchanged: number;
  errors: PeopleImportErrorDto[];
}

/** Mirrors RoleAssignmentDto. */
export interface RoleAssignmentDto {
  id: string;
  person: PersonRefDto;
  role: AssignedRole;
  siteId: string | null;
  siteName: string | null;
}

/** Mirrors AssignRoleRequest. */
export interface AssignRoleRequest {
  personId: string;
  role: AssignedRole;
  siteId: string | null;
}

// ---- M10 tenant admin: sites, floors, areas, doors (Gatehouse.Application.Sites.Dtos) ----

/** Mirrors SaveSiteRequest. */
export interface SaveSiteRequest {
  name: string;
  code: string;
  timeZoneId: string;
}

/** Mirrors SaveFloorRequest. */
export interface SaveFloorRequest {
  name: string;
  level: number;
  planWidth: number;
  planHeight: number;
}

/** Mirrors AdminFloorDto. */
export interface AdminFloorDto {
  floorId: string;
  siteId: string;
  name: string;
  level: number;
  planWidth: number;
  planHeight: number;
}

/** Mirrors SecurityLevelAdminDto. */
export interface SecurityLevelAdminDto {
  id: string;
  name: string;
  rank: number;
  approverChain: "Manager" | "AreaOwner" | "Both";
  maxDurationDays: number;
}

/** Mirrors SaveAreaRequest; `ownerIds` required on create, null on update keeps owners. */
export interface SaveAreaRequest {
  name: string;
  code: string;
  polygon: PointDto[];
  securityLevelId: string;
  ownerIds: string[] | null;
  accessControlled?: boolean | null;
}

/** Mirrors SetAreaOwnersRequest. */
export interface SetAreaOwnersRequest {
  personIds: string[];
}

/** Mirrors SaveDoorRequest. */
export interface SaveDoorRequest {
  name: string;
  position: PointDto;
  pacsDoorRef: string | null;
  /** On update: move the door to this area of the same floor. */
  areaId?: string | null;
}

/** Mirrors SaveReaderRequest. */
export interface SaveReaderRequest {
  name: string;
  direction: ReaderDirection;
  position: PointDto;
  pacsReaderRef: string | null;
}

/** Mirrors PlanReaderDto. */
export interface PlanReaderDto {
  id: string;
  name: string;
  direction: ReaderDirection;
  position: PointDto;
  pacsReaderRef: string | null;
}

/** Mirrors PlanDoorDto. */
export interface PlanDoorDto {
  id: string;
  name: string;
  position: PointDto;
  pacsDoorRef: string | null;
  readers: PlanReaderDto[];
}

/** Mirrors PlanAreaDto. */
export interface PlanAreaDto {
  id: string;
  name: string;
  code: string;
  polygon: PointDto[];
  securityLevelId: string;
  owners: PersonRefDto[];
  doors: PlanDoorDto[];
  /** False: no PACS door (corridor, storage); excluded from mapping, onboarding and requests. */
  accessControlled?: boolean;
}

/** Mirrors FloorPlanDto. */
export interface FloorPlanDto {
  floorId: string;
  siteId: string;
  name: string;
  level: number;
  planWidth: number;
  planHeight: number;
  areas: PlanAreaDto[];
  hasBackground: boolean;
  backgroundUpdatedAt: string | null;
}

// ---- M10 tenant admin: PACS instances and connectors (Gatehouse.Application.Fulfillment.PacsAdmin) ----

export type PacsVendor = "LegacyPacs" | "CloudPacs";

/** Mirrors SavePacsInstanceRequest. */
export interface SavePacsInstanceRequest {
  name: string;
  vendor: PacsVendor;
  siteIds: string[];
}

/** Mirrors AccessLevelMappingRequest. */
export interface AccessLevelMappingRequest {
  areaId: string;
  vendorAccessLevelRef: string;
  displayName: string | null;
}

/** Mirrors AccessLevelMappingDto. */
export interface AccessLevelMappingDto {
  areaId: string;
  areaName: string;
  vendorAccessLevelRef: string;
  displayName: string;
}

/** Mirrors AdminConnectorDto. */
export interface AdminConnectorDto {
  connectorId: string;
  status: ConnectorStatus;
  createdAt: string;
  enrollmentTokenExpiresAt: string | null;
  certificateNotAfter: string | null;
  lastSeenAt: string | null;
}

/** Mirrors PacsInstanceAdminDto. */
export interface PacsInstanceAdminDto {
  id: string;
  name: string;
  vendor: PacsVendor;
  siteIds: string[];
  fulfillmentMode: FulfillmentMode;
  mappings: AccessLevelMappingDto[];
  unmappedAreas: number;
  connector: AdminConnectorDto | null;
  health: ConnectorHealthDto | null;
}

/** Mirrors ConnectorEnrollmentDto; returned once with `Cache-Control: no-store`. */
export interface ConnectorEnrollmentDto {
  connectorId: string;
  enrollmentToken: string;
  expiresAt: string;
  gatewayUrl: string;
  caCertificatePem: string;
  vendor: PacsVendor;
  /** `Gateway:AgentImage`; null = no published image, the install snippet shows build instructions (M11-21). */
  agentImage: string | null;
}

// ---- M10 onboarding (Gatehouse.Application.Tenancy.Onboarding) ----

export type OnboardingStep = "IdentityProvider" | "Sites" | "People" | "Pacs" | "Connector" | "Staff";
export type OnboardingStepState = "NotStarted" | "InProgress" | "Complete" | "Skipped";

/** Mirrors OnboardingStepDto. */
export interface OnboardingStepDto {
  step: OnboardingStep;
  state: OnboardingStepState;
  skippable: boolean;
  detail: string;
  href: string;
}

/** Mirrors OnboardingStatusDto. */
export interface OnboardingStatusDto {
  steps: OnboardingStepDto[];
  canComplete: boolean;
  completedAt: string | null;
}
