import { defineConfig } from "vite";
import { fileURLToPath } from "node:url";

// The bundle is written directly into the embedded-resource folder of 'UniversalWorkItem.Boards.Ui'
// and committed there. 'index.html' in that folder is maintained by hand (it is not touched by the build).
// One classic IIFE script is required: module scripts cause CORS / scheme issues inside WebView2
// (see skill 'asp-engine-webview-coupling').
const outDir = fileURLToPath(new URL("../../dotnet/src/UniversalWorkItem.Boards.Ui/BoardUi", import.meta.url));

export default defineConfig({
  build: {
    outDir: outDir,
    emptyOutDir: false,
    minify: false,
    sourcemap: false,
    cssCodeSplit: false,
    lib: {
      entry: fileURLToPath(new URL("./src/main.ts", import.meta.url)),
      name: "UwiBoard",
      formats: ["iife"],
      fileName: () => "app.js",
      cssFileName: "app"
    }
  }
});
