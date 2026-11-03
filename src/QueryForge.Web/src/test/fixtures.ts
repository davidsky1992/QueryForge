import { ApiAuthenticationType, ApiDefinition, ApiDefinitionStatus } from "../types/apiDefinition";
import { ApiOperation, OperationHttpMethod, ParameterLocation, ParameterType } from "../types/operation";

export function definition(overrides: Partial<ApiDefinition> = {}): ApiDefinition {
  return {
    id: "def-1",
    name: "Weather API",
    slug: "weather",
    description: null,
    baseUrl: "https://api.example.test",
    status: ApiDefinitionStatus.Active,
    authenticationType: ApiAuthenticationType.None,
    documentationUrl: null,
    tags: null,
    createdAtUtc: "2026-11-01T00:00:00Z",
    updatedAtUtc: null,
    rowVersion: "AAAAAAAAB9E=",
    upstreamSecretName: null,
    apiKeyHeaderName: null,
    secretConfigured: false,
    ...overrides
  };
}

export function operation(overrides: Partial<ApiOperation> = {}): ApiOperation {
  return {
    id: "op-1",
    apiDefinitionId: "def-1",
    key: "current",
    method: OperationHttpMethod.Get,
    pathTemplate: "/v1/forecast/{city}",
    parameters: [
      {
        name: "city",
        location: ParameterLocation.Path,
        type: ParameterType.String,
        required: true,
        maxLength: 64,
        minimum: null,
        maximum: null,
        pattern: null,
        allowedValues: [],
        default: null
      }
    ],
    timeoutSeconds: 10,
    maxResponseBytes: 262144,
    enabled: true,
    createdAtUtc: "2026-11-01T00:00:00Z",
    updatedAtUtc: null,
    rowVersion: "AAAAAAAAB9I=",
    ...overrides
  };
}

export function page<T>(items: T[]) {
  return { items, pageNumber: 1, pageSize: 10, totalCount: items.length, totalPages: items.length === 0 ? 0 : 1 };
}
