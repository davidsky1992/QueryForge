import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach, vi } from "vitest";
import { setApiKey } from "../api/httpClient";

afterEach(() => {
  cleanup();
  setApiKey(null);
  vi.unstubAllGlobals();
});
