export enum ApiDefinitionStatus {
  Draft = 0,
  Active = 1,
  Deprecated = 2,
  Archived = 3
}

export enum ApiAuthenticationType {
  None = 0,
  ApiKey = 1,
  BearerToken = 2,
  Basic = 3
}

export type ApiDefinition = {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  baseUrl: string;
  status: ApiDefinitionStatus;
  authenticationType: ApiAuthenticationType;
  documentationUrl: string | null;
  tags: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  rowVersion: string;
};

export type ApiDefinitionFormValues = {
  name: string;
  slug: string;
  description: string;
  baseUrl: string;
  status: ApiDefinitionStatus;
  authenticationType: ApiAuthenticationType;
  documentationUrl: string;
  tags: string;
};

export type PagedResult<T> = {
  items: T[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export const statusLabels: Record<ApiDefinitionStatus, string> = {
  [ApiDefinitionStatus.Draft]: "Draft",
  [ApiDefinitionStatus.Active]: "Active",
  [ApiDefinitionStatus.Deprecated]: "Deprecated",
  [ApiDefinitionStatus.Archived]: "Archived"
};

export const authenticationLabels: Record<ApiAuthenticationType, string> = {
  [ApiAuthenticationType.None]: "None",
  [ApiAuthenticationType.ApiKey]: "API key",
  [ApiAuthenticationType.BearerToken]: "Bearer token",
  [ApiAuthenticationType.Basic]: "Basic"
};
