import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { operation } from "../test/fixtures";
import { mockFetch } from "../test/fetchMock";
import { ParameterLocation, ParameterType } from "../types/operation";
import { TryItPanel } from "./TryItPanel";

const executePath = "/api/execute/weather/current";

const withDays = operation({
  parameters: [
    ...operation().parameters,
    { name: "days", location: ParameterLocation.Query, type: ParameterType.Integer, required: false, maxLength: null, minimum: 1, maximum: 7, pattern: null, allowedValues: [], default: "3" }
  ]
});

function renderPanel() {
  const onClose = vi.fn();
  render(<TryItPanel definitionSlug="weather" operation={withDays} onClose={onClose} />);
  return { onClose, user: userEvent.setup() };
}

describe("TryItPanel", () => {
  it("renders an input per declared parameter and marks required ones", () => {
    renderPanel();

    expect(screen.getByLabelText("city *")).toBeInTheDocument();
    expect(screen.getByLabelText("days")).toHaveAttribute("placeholder", "3");
  });

  it("runs the operation and shows the envelope", async () => {
    const { requests } = mockFetch([
      { method: "POST", path: executePath, status: 200, body: { upstreamStatus: 200, contentType: "application/json", body: { temp: 31 }, durationMs: 143 } }
    ]);
    const { user } = renderPanel();

    await user.type(screen.getByLabelText("city *"), "Hanoi");
    await user.type(screen.getByLabelText("days"), "2");
    await user.click(screen.getByRole("button", { name: "Run" }));

    const result = await screen.findByLabelText("Execution result");
    expect(result).toHaveTextContent("Upstream status 200");
    expect(result).toHaveTextContent("application/json");
    expect(result).toHaveTextContent("143 ms");
    expect(result).toHaveTextContent('"temp": 31');
    expect(requests[0].body).toEqual({ parameters: { city: "Hanoi", days: 2 } });
  });

  it("renders upstream markup as text, never as HTML", async () => {
    const hostile = "<img src=x onerror=\"window.__pwned=true\"><script>window.__pwned=true</script>";
    mockFetch([{ method: "POST", path: executePath, status: 200, body: { upstreamStatus: 200, contentType: "text/html", body: hostile, durationMs: 1 } }]);
    const { user } = renderPanel();

    await user.type(screen.getByLabelText("city *"), "Hanoi");
    await user.click(screen.getByRole("button", { name: "Run" }));

    const result = await screen.findByLabelText("Execution result");
    expect(result).toHaveTextContent(hostile);
    expect(result.querySelector("img")).toBeNull();
    expect(result.querySelector("script")).toBeNull();
    expect((window as unknown as { __pwned?: boolean }).__pwned).toBeUndefined();
  });

  it("explains that execution is disabled when the route does not exist", async () => {
    mockFetch([{ method: "POST", path: executePath, status: 404 }]);
    const { user } = renderPanel();

    await user.click(screen.getByRole("button", { name: "Run" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Execution is disabled on this server");
  });

  it("distinguishes an unknown operation from disabled execution", async () => {
    mockFetch([{ method: "POST", path: executePath, status: 404, body: { title: "Resource not found", status: 404 } }]);
    const { user } = renderPanel();

    await user.click(screen.getByRole("button", { name: "Run" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("The operation was not found.");
  });

  it("shows parameter errors from a validation problem", async () => {
    mockFetch([{ method: "POST", path: executePath, status: 400, body: { title: "One or more validation errors occurred.", status: 400, errors: { "parameters.city": ["Parameter is required."] } } }]);
    const { user } = renderPanel();

    await user.click(screen.getByRole("button", { name: "Run" }));

    expect(await screen.findByRole("list", { name: "Parameter errors" })).toHaveTextContent("parameters.city: Parameter is required.");
  });

  it.each([
    [502, "Upstream request failed", "upstream_destination_blocked"],
    [503, "Upstream credential unavailable", "upstream_secret_unavailable"],
    [504, "Upstream timeout", "upstream_timeout"],
    [409, "Operation cannot be executed", "operation_disabled"]
  ])("shows status %i with its code", async (status, title, code) => {
    mockFetch([{ method: "POST", path: executePath, status, body: { title, status, code } }]);
    const { user } = renderPanel();

    await user.type(screen.getByLabelText("city *"), "Hanoi");
    await user.click(screen.getByRole("button", { name: "Run" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(`${title} (${status}, ${code})`);
  });

  it("explains a 403 for keys that cannot execute", async () => {
    mockFetch([{ method: "POST", path: executePath, status: 403, body: { title: "Forbidden", status: 403 } }]);
    const { user } = renderPanel();

    await user.click(screen.getByRole("button", { name: "Run" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Use an executor or admin key.");
  });

  it("closes", async () => {
    const { onClose, user } = renderPanel();

    await user.click(screen.getByRole("button", { name: "Close" }));

    expect(onClose).toHaveBeenCalled();
  });
});
