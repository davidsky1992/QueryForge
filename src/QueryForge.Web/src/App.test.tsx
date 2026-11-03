import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import App from "./App";
import { ApiAuthenticationType } from "./types/apiDefinition";
import { definition, page } from "./test/fixtures";
import { mockFetch } from "./test/fetchMock";

const listPath = /^\/api\/api-definitions\?pageNumber=1&pageSize=10/;

async function enterKey() {
  const user = userEvent.setup();
  await user.type(screen.getByPlaceholderText("API key"), "admin-key");
  await user.click(screen.getByRole("button", { name: "Use key" }));
  return user;
}

describe("App", () => {
  it("shows the secret fields only for API key authentication", async () => {
    mockFetch([{ method: "GET", path: listPath, status: 200, body: page([]) }]);
    render(<App />);
    const user = await enterKey();

    expect(screen.queryByLabelText("Upstream secret name")).not.toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText("Auth"), "API key");

    expect(screen.getByLabelText("Upstream secret name")).toBeInTheDocument();
    expect(screen.getByLabelText("API key header")).toBeInTheDocument();
  });

  it("keeps the secret reference when an API key definition is edited", async () => {
    const keyed = definition({
      authenticationType: ApiAuthenticationType.ApiKey,
      upstreamSecretName: "weather-api",
      apiKeyHeaderName: "X-API-Key",
      secretConfigured: true
    });
    const { requests } = mockFetch([
      { method: "GET", path: listPath, status: 200, body: page([keyed]) },
      { method: "GET", path: "/api/api-definitions/def-1/operations", status: 200, body: [] },
      { method: "PUT", path: "/api/api-definitions/def-1", status: 200, body: { ...keyed, name: "Weather API v2" } },
      { method: "GET", path: listPath, status: 200, body: page([{ ...keyed, name: "Weather API v2" }]) },
      { method: "GET", path: "/api/api-definitions/def-1/operations", status: 200, body: [] }
    ]);
    render(<App />);
    const user = await enterKey();
    expect(await screen.findByText("weather-api in X-API-Key — configured")).toBeInTheDocument();

    const row = screen.getByRole("row", { name: /Weather API/ });
    await user.click(within(row).getByRole("button", { name: "Edit" }));
    await user.clear(screen.getByLabelText("Name"));
    await user.type(screen.getByLabelText("Name"), "Weather API v2");
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    await screen.findByText("API definition updated.");
    expect(requests.find(request => request.method === "PUT")!.body).toMatchObject({
      name: "Weather API v2",
      upstreamSecretName: "weather-api",
      apiKeyHeaderName: "X-API-Key"
    });
  });
});
