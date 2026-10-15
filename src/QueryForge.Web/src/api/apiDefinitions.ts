import { request } from "./httpClient";
import {
  ApiDefinition,
  ApiDefinitionFormValues,
  ApiDefinitionStatus,
  PagedResult
} from "../types/apiDefinition";

export type ApiDefinitionListParams = {
  pageNumber: number;
  pageSize: number;
  search?: string;
  status?: ApiDefinitionStatus | "";
};

export function listApiDefinitions(params: ApiDefinitionListParams): Promise<PagedResult<ApiDefinition>> {
  const query = new URLSearchParams({
    pageNumber: String(params.pageNumber),
    pageSize: String(params.pageSize)
  });

  if (params.search) {
    query.set("search", params.search);
  }

  if (params.status !== undefined && params.status !== "") {
    query.set("status", String(params.status));
  }

  return request<PagedResult<ApiDefinition>>(`/api/api-definitions?${query.toString()}`);
}

export function createApiDefinition(values: ApiDefinitionFormValues): Promise<ApiDefinition> {
  return request<ApiDefinition>("/api/api-definitions", {
    method: "POST",
    body: JSON.stringify(normalizePayload(values))
  });
}

export function updateApiDefinition(id: string, values: ApiDefinitionFormValues): Promise<ApiDefinition> {
  return request<ApiDefinition>(`/api/api-definitions/${id}`, {
    method: "PUT",
    body: JSON.stringify(normalizePayload(values))
  });
}

export function deleteApiDefinition(id: string): Promise<void> {
  return request<void>(`/api/api-definitions/${id}`, {
    method: "DELETE"
  });
}

function normalizePayload(values: ApiDefinitionFormValues) {
  return {
    ...values,
    description: values.description.trim() || null,
    documentationUrl: values.documentationUrl.trim() || null,
    tags: values.tags.trim() || null
  };
}
