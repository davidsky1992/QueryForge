import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { definition, operation } from "../test/fixtures";
import { mockFetch } from "../test/fetchMock";
import { OperationsPanel } from "./OperationsPanel";

const listPath = "/api/api-definitions/def-1/operations";

describe("OperationsPanel", () => {
  it("lists the definition's operations", async () => {
    mockFetch([{ method: "GET", path: listPath, status: 200, body: [operation()] }]);

    render(<OperationsPanel definition={definition()} />);

    expect(await screen.findByText("current")).toBeInTheDocument();
    expect(screen.getByText("GET /v1/forecast/{city}")).toBeInTheDocument();
    expect(screen.getByText("city")).toBeInTheDocument();
  });

  it("creates an operation with its parameters", async () => {
    const { requests } = mockFetch([
      { method: "GET", path: listPath, status: 200, body: [] },
      { method: "POST", path: listPath, status: 201, body: operation() },
      { method: "GET", path: listPath, status: 200, body: [operation()] }
    ]);
    const user = userEvent.setup();
    render(<OperationsPanel definition={definition()} />);
    await screen.findByText("No operations yet.");

    await user.click(screen.getByRole("button", { name: "New operation" }));
    const form = screen.getByRole("form", { name: "New operation" });
    await user.type(within(form).getByLabelText("Key"), "current");
    await user.clear(within(form).getByLabelText("Path template"));
    await user.type(within(form).getByLabelText("Path template"), "/v1/forecast/{{city}");
    await user.click(within(form).getByRole("button", { name: "Add parameter" }));
    await user.type(within(form).getByLabelText("Parameter 1 name"), "city");
    await user.selectOptions(within(form).getByLabelText("Parameter 1 location"), "Path");
    await user.click(within(form).getByLabelText("Parameter 1 required"));
    await user.click(within(form).getByRole("button", { name: "Create operation" }));

    expect(await screen.findByText("Operation created.")).toBeInTheDocument();
    const posted = requests.find(request => request.method === "POST")!;
    expect(posted.body).toMatchObject({
      key: "current",
      pathTemplate: "/v1/forecast/{city}",
      timeoutSeconds: 10,
      maxResponseBytes: 262144,
      parameters: [{ name: "city", location: 0, type: 0, required: true }]
    });
  });

  it("shows field errors from a validation problem", async () => {
    mockFetch([
      { method: "GET", path: listPath, status: 200, body: [] },
      {
        method: "POST",
        path: listPath,
        status: 400,
        body: { title: "One or more validation errors occurred.", status: 400, errors: { pathTemplate: ["pathTemplate must not contain empty segments ('//')."] } }
      }
    ]);
    const user = userEvent.setup();
    render(<OperationsPanel definition={definition()} />);
    await screen.findByText("No operations yet.");

    await user.click(screen.getByRole("button", { name: "New operation" }));
    await user.type(screen.getByLabelText("Key"), "bad");
    await user.click(screen.getByRole("button", { name: "Create operation" }));

    const errors = await screen.findByRole("list", { name: "Validation errors" });
    expect(errors).toHaveTextContent("pathTemplate: pathTemplate must not contain empty segments");
    expect(screen.getByRole("form", { name: "New operation" })).toBeInTheDocument(); // the input is kept for correction
  });

  it("reloads and explains when a save hits a concurrency conflict", async () => {
    const changed = operation({ timeoutSeconds: 20, rowVersion: "AAAAAAAAB9M=" });
    const { requests } = mockFetch([
      { method: "GET", path: listPath, status: 200, body: [operation()] },
      { method: "PUT", path: `${listPath}/op-1`, status: 409, body: { title: "Concurrency conflict", status: 409, code: "concurrency_conflict" } },
      { method: "GET", path: listPath, status: 200, body: [changed] }
    ]);
    const user = userEvent.setup();
    render(<OperationsPanel definition={definition()} />);
    await screen.findByText("current");

    await user.click(screen.getByRole("button", { name: "Edit" }));
    await user.click(screen.getByRole("button", { name: "Save operation" }));

    expect(await screen.findByText(/changed by someone else/)).toBeInTheDocument();
    expect(screen.queryByRole("form")).not.toBeInTheDocument();
    expect(requests.find(request => request.method === "PUT")!.body).toMatchObject({ rowVersion: "AAAAAAAAB9I=" });
    expect(requests.filter(request => request.method === "GET")).toHaveLength(2);
  });

  it("deletes with the operation's rowVersion after confirmation", async () => {
    vi.spyOn(window, "confirm").mockReturnValue(true);
    const { requests } = mockFetch([
      { method: "GET", path: listPath, status: 200, body: [operation()] },
      { method: "DELETE", path: `${listPath}/op-1?rowVersion=AAAAAAAAB9I%3D`, status: 204 },
      { method: "GET", path: listPath, status: 200, body: [] }
    ]);
    const user = userEvent.setup();
    render(<OperationsPanel definition={definition()} />);
    await screen.findByText("current");

    await user.click(screen.getByRole("button", { name: "Delete" }));

    expect(await screen.findByText("Operation deleted.")).toBeInTheDocument();
    await waitFor(() => expect(requests.some(request => request.method === "DELETE")).toBe(true));
  });

  it("opens the Try it panel for an operation", async () => {
    mockFetch([{ method: "GET", path: listPath, status: 200, body: [operation()] }]);
    const user = userEvent.setup();
    render(<OperationsPanel definition={definition()} />);
    await screen.findByText("current");

    await user.click(screen.getByRole("button", { name: "Try" }));

    expect(screen.getByRole("region", { name: "Try current" })).toBeInTheDocument();
    expect(screen.getByLabelText("city *")).toBeInTheDocument();
  });

  it("explains a 403 for non-admin keys", async () => {
    mockFetch([
      { method: "GET", path: listPath, status: 200, body: [] },
      { method: "POST", path: listPath, status: 403, body: { title: "Forbidden", status: 403 } }
    ]);
    const user = userEvent.setup();
    render(<OperationsPanel definition={definition()} />);
    await screen.findByText("No operations yet.");

    await user.click(screen.getByRole("button", { name: "New operation" }));
    await user.type(screen.getByLabelText("Key"), "x");
    await user.click(screen.getByRole("button", { name: "Create operation" }));

    expect(await screen.findByText(/Changing operations requires an admin key/)).toBeInTheDocument();
  });
});
