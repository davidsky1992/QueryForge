import { request } from "./httpClient";
import {
  ApiOperation,
  OperationFormValues,
  OperationHttpMethod,
  ParameterFormValues
} from "../types/operation";

function operationsPath(definitionId: string) {
  return `/api/api-definitions/${encodeURIComponent(definitionId)}/operations`;
}

export function listOperations(definitionId: string): Promise<ApiOperation[]> {
  return request<ApiOperation[]>(operationsPath(definitionId));
}

export function createOperation(definitionId: string, values: OperationFormValues): Promise<ApiOperation> {
  return request<ApiOperation>(operationsPath(definitionId), {
    method: "POST",
    body: JSON.stringify(toOperationPayload(values))
  });
}

export function updateOperation(definitionId: string, operationId: string, rowVersion: string, values: OperationFormValues): Promise<ApiOperation> {
  return request<ApiOperation>(`${operationsPath(definitionId)}/${encodeURIComponent(operationId)}`, {
    method: "PUT",
    body: JSON.stringify({ ...toOperationPayload(values), rowVersion })
  });
}

export function deleteOperation(definitionId: string, operationId: string, rowVersion: string): Promise<void> {
  const query = new URLSearchParams({ rowVersion });
  return request<void>(`${operationsPath(definitionId)}/${encodeURIComponent(operationId)}?${query.toString()}`, {
    method: "DELETE"
  });
}

/** Converts form text to the API shape. Unparseable numbers are sent as-is so the API's validation reports them. */
export function toOperationPayload(values: OperationFormValues) {
  return {
    key: values.key.trim(),
    method: OperationHttpMethod.Get,
    pathTemplate: values.pathTemplate.trim(),
    timeoutSeconds: toNumber(values.timeoutSeconds),
    maxResponseBytes: toNumber(values.maxResponseBytes),
    enabled: values.enabled,
    parameters: values.parameters.map(toParameterPayload)
  };
}

function toParameterPayload(parameter: ParameterFormValues) {
  const allowedValues = parameter.allowedValues
    .split(",")
    .map(value => value.trim())
    .filter(value => value.length > 0);

  return {
    name: parameter.name.trim(),
    location: parameter.location,
    type: parameter.type,
    required: parameter.required,
    maxLength: toOptionalNumber(parameter.maxLength),
    minimum: toOptionalNumber(parameter.minimum),
    maximum: toOptionalNumber(parameter.maximum),
    pattern: parameter.pattern === "" ? null : parameter.pattern,
    allowedValues: allowedValues.length > 0 ? allowedValues : null,
    default: parameter.default === "" ? null : parameter.default
  };
}

function toNumber(text: string): number | string {
  const value = Number(text.trim());
  return text.trim() !== "" && Number.isFinite(value) ? value : text;
}

function toOptionalNumber(text: string): number | string | null {
  return text.trim() === "" ? null : toNumber(text);
}
