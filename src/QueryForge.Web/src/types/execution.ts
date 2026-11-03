export type ExecutionResult = {
  upstreamStatus: number;
  contentType: string | null;
  // Parsed JSON, a string for text responses, or null for an empty body. Untrusted upstream data: render as text only.
  body: unknown;
  durationMs: number;
};

export enum AuditOutcome {
  Succeeded = 0,
  Failed = 1,
  Rejected = 2
}

export type AuditEvent = {
  id: string;
  occurredAtUtc: string;
  actor: string;
  action: string;
  targetType: string;
  targetId: string | null;
  outcome: AuditOutcome;
  upstreamStatus: number | null;
  durationMs: number | null;
  errorCode: string | null;
  traceId: string | null;
};

export const auditOutcomeLabels: Record<AuditOutcome, string> = {
  [AuditOutcome.Succeeded]: "Succeeded",
  [AuditOutcome.Failed]: "Failed",
  [AuditOutcome.Rejected]: "Rejected"
};

export const auditActions = ["operation.created", "operation.updated", "operation.deleted", "operation.executed"] as const;
