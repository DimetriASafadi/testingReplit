import { defineConfig } from "vite";

const port = Number(process.env.PORT);
const base = process.env.BASE_PATH;
if (!Number.isInteger(port) || port <= 0 || port > 65535) {
  throw new Error("A valid PORT must be supplied by the artifact workflow.");
}
if (!base?.startsWith("/")) {
  throw new Error("BASE_PATH must be supplied by the artifact workflow.");
}

export default defineConfig({
  base,
  build: { outDir: "dist", emptyOutDir: true },
  server: {
    host: "0.0.0.0",
    port,
    strictPort: true,
    allowedHosts: true,
    fs: { strict: true },
  },
  preview: { host: "0.0.0.0", port, strictPort: true, allowedHosts: true },
});
