import { defineConfig, mergeConfig } from "vitest/config";
import viteConfig from "./vite.config";

// Tests run once with `vitest run`; no Vitest API or UI server is started.
export default mergeConfig(
  viteConfig,
  defineConfig({
    test: {
      environment: "jsdom",
      setupFiles: ["./src/test/setup.ts"],
      restoreMocks: true
    }
  })
);
