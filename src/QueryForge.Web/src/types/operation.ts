export enum OperationHttpMethod {
  Get = 0
}

export enum ParameterLocation {
  Path = 0,
  Query = 1
}

export enum ParameterType {
  String = 0,
  Integer = 1,
  Number = 2,
  Boolean = 3
}

export type OperationParameter = {
  name: string;
  location: ParameterLocation;
  type: ParameterType;
  required: boolean;
  maxLength: number | null;
  minimum: number | null;
  maximum: number | null;
  pattern: string | null;
  allowedValues: string[];
  default: string | null;
};

export type ApiOperation = {
  id: string;
  apiDefinitionId: string;
  key: string;
  method: OperationHttpMethod;
  pathTemplate: string;
  parameters: OperationParameter[];
  timeoutSeconds: number;
  maxResponseBytes: number;
  enabled: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  rowVersion: string;
};

// Form state keeps every input as text so partially typed values are not lost; it is converted on submit.
export type ParameterFormValues = {
  name: string;
  location: ParameterLocation;
  type: ParameterType;
  required: boolean;
  maxLength: string;
  minimum: string;
  maximum: string;
  pattern: string;
  allowedValues: string;
  default: string;
};

export type OperationFormValues = {
  key: string;
  pathTemplate: string;
  timeoutSeconds: string;
  maxResponseBytes: string;
  enabled: boolean;
  parameters: ParameterFormValues[];
};

export const locationLabels: Record<ParameterLocation, string> = {
  [ParameterLocation.Path]: "Path",
  [ParameterLocation.Query]: "Query"
};

export const parameterTypeLabels: Record<ParameterType, string> = {
  [ParameterType.String]: "String",
  [ParameterType.Integer]: "Integer",
  [ParameterType.Number]: "Number",
  [ParameterType.Boolean]: "Boolean"
};
