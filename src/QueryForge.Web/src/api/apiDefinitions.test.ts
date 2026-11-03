import { describe, expect, it } from "vitest";
import { mockFetch } from "../test/fetchMock";
import { ApiAuthenticationType, ApiDefinitionFormValues, ApiDefinitionStatus } from "../types/apiDefinition";
import { createApiDefinition, updateApiDefinition } from "./apiDefinitions";

const values: ApiDefinitionFormValues = {
  name: "Weather API",
  slug: "weather",
  description: "",
  baseUrl: "https://api.example.test",
  status: ApiDefinitionStatus.Active,
  authenticationType: ApiAuthenticationType.ApiKey,
  documentationUrl: "",
  tags: "",
  upstreamSecretName: " weather-api ",
  apiKeyHeaderName: " X-API-Key "
};

describe("apiDefinitions", () => {
  it("sends the secret reference with updates, so editing a definition does not clear it", async () => {
    const { requests } = mockFetch([{ method: "PUT", path: "/api/api-definitions/def-1", status: 200, body: {} }]);

    await updateApiDefinition("def-1", "AAAAAAAAB9E=", values);

    expect(requests[0].body).toMatchObject({
      upstreamSecretName: "weather-api",
      apiKeyHeaderName: "X-API-Key",
      rowVersion: "AAAAAAAAB9E="
    });
  });

  it("sends empty optional fields as null", async () => {
    const { requests } = mockFetch([{ method: "POST", path: "/api/api-definitions", status: 201, body: {} }]);

    await createApiDefinition({ ...values, authenticationType: ApiAuthenticationType.None, upstreamSecretName: "", apiKeyHeaderName: "  " });

    expect(requests[0].body).toMatchObject({
      description: null,
      documentationUrl: null,
      tags: null,
      upstreamSecretName: null,
      apiKeyHeaderName: null
    });
  });
});
