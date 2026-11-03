import { describe, expect, it } from "vitest";
import { mockFetch } from "../test/fetchMock";
import { OperationFormValues, ParameterLocation, ParameterType } from "../types/operation";
import { createOperation, deleteOperation, toOperationPayload, updateOperation } from "./operations";

const values: OperationFormValues = {
  key: " current ",
  pathTemplate: " /v1/forecast/{city} ",
  timeoutSeconds: "12",
  maxResponseBytes: "4096",
  enabled: true,
  parameters: [
    { name: "city", location: ParameterLocation.Path, type: ParameterType.String, required: true, maxLength: "64", minimum: "", maximum: "", pattern: "[A-Za-z ]+", allowedValues: "", default: "" },
    { name: "units", location: ParameterLocation.Query, type: ParameterType.String, required: false, maxLength: "", minimum: "", maximum: "", pattern: "", allowedValues: "metric, imperial ,", default: "metric" },
    { name: "days", location: ParameterLocation.Query, type: ParameterType.Integer, required: false, maxLength: "", minimum: "1", maximum: "7", pattern: "", allowedValues: "", default: "" }
  ]
};

describe("operations api", () => {
  it("converts form text to the API payload", () => {
    expect(toOperationPayload(values)).toEqual({
      key: "current",
      method: 0,
      pathTemplate: "/v1/forecast/{city}",
      timeoutSeconds: 12,
      maxResponseBytes: 4096,
      enabled: true,
      parameters: [
        { name: "city", location: 0, type: 0, required: true, maxLength: 64, minimum: null, maximum: null, pattern: "[A-Za-z ]+", allowedValues: null, default: null },
        { name: "units", location: 1, type: 0, required: false, maxLength: null, minimum: null, maximum: null, pattern: null, allowedValues: ["metric", "imperial"], default: "metric" },
        { name: "days", location: 1, type: 1, required: false, maxLength: null, minimum: 1, maximum: 7, pattern: null, allowedValues: null, default: null }
      ]
    });
  });

  it("passes unparseable numbers through so the API reports them", () => {
    expect(toOperationPayload({ ...values, timeoutSeconds: "ten" }).timeoutSeconds).toBe("ten");
  });

  it("uses the definition's operations routes with rowVersion for updates and deletes", async () => {
    const { requests } = mockFetch([
      { method: "POST", path: "/api/api-definitions/def-1/operations", status: 201, body: {} },
      { method: "PUT", path: "/api/api-definitions/def-1/operations/op-1", status: 200, body: {} },
      { method: "DELETE", path: "/api/api-definitions/def-1/operations/op-1?rowVersion=AAAA%2BB%3D%3D", status: 204 }
    ]);

    await createOperation("def-1", values);
    await updateOperation("def-1", "op-1", "AAAA+B==", values);
    await deleteOperation("def-1", "op-1", "AAAA+B==");

    expect(requests.map(request => request.method)).toEqual(["POST", "PUT", "DELETE"]);
    expect(requests[1].body).toMatchObject({ rowVersion: "AAAA+B==" });
  });
});
