export type Role = 'user' | 'assistant';

export type StatusTone = 'idle' | 'busy' | 'ok' | 'error';
export type ConsolePage = 'overview' | 'chat' | 'memory' | 'debates' | 'analytics' | 'operations';
export type MemoryView = 'graph' | 'relationships' | 'workbench';

export interface PageNavItem {
  id: ConsolePage;
  label: string;
  caption: string;
  path: string;
}

export interface ChatTurn {
  role: Role;
  text: string;
  startedAtUtc?: string;
  completedAtUtc?: string;
  toolCalls?: ToolInvocationAudit[];
  memoryLayers?: string[];
}

export interface ParsedAssistantPayload {
  stages: string[];
  finalAnswer: string;
}

export interface CachedParsedPayload {
  sourceText: string;
  payload: ParsedAssistantPayload;
}

export interface StreamChunk {
  sessionId: string;
  delta: string;
  isFinal: boolean;
  generatedAtUtc: string;
  contextTurnCount: number;
}

export interface RawStreamChunk {
  sessionId?: string;
  delta?: string;
  isFinal?: boolean;
  generatedAtUtc?: string;
  contextTurnCount?: number;
  SessionId?: string;
  Delta?: string;
  IsFinal?: boolean;
  GeneratedAtUtc?: string;
  ContextTurnCount?: number;
  message?: string;
  detail?: string;
}

export interface SelfPreference {
  key: string;
  value: string;
  updatedAtUtc: string;
}

export interface SelfModelSnapshot {
  preferences: SelfPreference[];
}

export interface ToolInvocationAudit {
  auditId: string;
  toolName: string;
  isWrite: boolean;
  argumentsJson: string;
  resultJson: string;
  succeeded: boolean;
  error?: string | null;
  executedAtUtc: string;
}

export interface EventingRow {
  eventId: string;
  eventType: string;
  aggregateType: string;
  aggregateId: string;
  status: string;
  retryCount: number;
  lastError?: string | null;
  occurredAtUtc: string;
  lastAttemptedAtUtc?: string | null;
  publishedAtUtc?: string | null;
  consumerCheckpointCount: number;
  payloadPreview: string;
}

export interface SubconsciousDebateRow {
  debateId: string;
  sessionId?: string;
  topicKey: string;
  triggerEventType: string;
  triggerPayloadJson?: string;
  triggerEventId?: string | null;
  state: string;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  lastError?: string | null;
}

export interface SubconsciousLifecycleRow {
  eventId: string;
  eventType: string;
  status: string;
  lastError?: string | null;
  occurredAtUtc: string;
  publishedAtUtc?: string | null;
  payloadPreview: string;
}

export interface DebateTurnRow {
  turnId: string;
  debateId: string;
  turnNumber: number;
  agentName: string;
  role: string;
  message: string;
  confidence?: number | null;
  createdAtUtc: string;
}

export interface DebateOutcomeRow {
  debateId: string;
  outcomeJson: string;
  outcomeHash: string;
  validationStatus: string;
  applyStatus: string;
  applyError?: string | null;
  appliedAtUtc?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface DebateEventRow {
  eventId: string;
  eventType: string;
  status: string;
  retryCount: number;
  lastError?: string | null;
  occurredAtUtc: string;
  publishedAtUtc?: string | null;
  payloadPreview: string;
}

export interface DebateReviewResponse {
  debateId: string;
  sessionId: string;
  validation: {
    isValid: boolean;
    requiresUserInput: boolean;
    status: string;
    error?: string | null;
  };
  applyPreview?: {
    proposedClaimsToCreate: number;
    appliedClaimsToCreate: number;
    proposedClaimsToSupersede: number;
    appliedClaimsToSupersede: number;
    proposedProceduralUpdates: number;
    appliedProceduralUpdates: number;
    proposedSelfUpdates: number;
    appliedSelfUpdates: number;
    skipped: Array<{
      category: string;
      reference: string;
      reason: string;
      confidence?: number | null;
    }>;
    anyApplied: boolean;
  } | null;
}

export interface ScheduledActionRow {
  actionId: string;
  companionId?: string;
  sessionId: string;
  actionType: string;
  inputJson: string;
  runAtUtc: string;
  status: string;
  attempts: number;
  maxAttempts: number;
  lastError?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  completedAtUtc?: string | null;
}

export interface MemoryRelationshipRow {
  relationshipId: string;
  sessionId: string;
  fromType: number;
  fromId: string;
  toType: number;
  toId: string;
  relationshipType: string;
  confidence: number;
  strength: number;
  status: string | number;
  validFromUtc?: string | null;
  validToUtc?: string | null;
  metadataJson?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface RelationshipGraphNode {
  key: string;
  nodeType: number;
  nodeId: string;
  x: number;
  y: number;
  degree: number;
}

export interface RelationshipGraphEdge {
  key: string;
  fromKey: string;
  toKey: string;
  relationshipType: string;
  status: string;
  confidence: number;
  strength: number;
}
