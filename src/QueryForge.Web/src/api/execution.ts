import { request } from "./httpClient";
import { AuditEvent, AuditOutcome, ExecutionResult } from "../types/execution";
import { PagedResult } from "../types/apiDefinition";
import { OperationParameter, ParameterType } from "../types/operation";

export function executeOperation(definitionSlug: string, operationKey: string, parameters: Record<string, string | number | boolean>): Promise<ExecutionResult> {
  return request<ExecutionResult>(`/api/execute/${encodeURIComponent(definitionSlug)}/${encodeURIComponent(operationKey)}`, {
    method: "POST",
    body: JSON.stringify({ parameters })
  });
}

/**
 * Turns form text into parameter values. Empty inputs are left out so the API applies defaults or reports missing
 * required parameters; numbers that do not parse are sent as text so the API's validation reports them.
 */
export function toParameterValues(parameters: OperationParameter[], inputs: Record<string, string>): Record<string, string | number | boolean> {
  const values: Record<string, string | number | boolean> = {};
  for (const parameter of parameters) {
    const text = inputs[parameter.name] ?? "";
    if (text === "") {
      continue;
    }

    if (parameter.type === ParameterType.Boolean && (text === "true" || text === "false")) {
      values[parameter.name] = text === "true";
    } else if ((parameter.type === ParameterType.Integer || parameter.type === ParameterType.Number) && text.trim() !== "" && Number.isFinite(Number(text))) {
      values[parameter.name] = Number(text);
    } else {
      values[parameter.name] = text;
    }
  }

  return values;
}

export type AuditQuery = {
  pageNumber: number;
  pageSize: number;
  action?: string;
  outcome?: AuditOutcome;
  from?: string;
  to?: string;
};

export function listAuditEvents(query: AuditQuery): Promise<PagedResult<AuditEvent>> {
  const search = new URLSearchParams({ pageNumber: String(query.pageNumber), pageSize: String(query.pageSize) });
  if (query.action) {
    search.set("action", query.action);
  }

  if (query.outcome !== undefined) {
    search.set("outcome", String(query.outcome));
  }

  if (query.from) {
    search.set("from", query.from);
  }

  if (query.to) {
    search.set("to", query.to);
  }

  return request<PagedResult<AuditEvent>>(`/api/audit-events?${search.toString()}`);
}
