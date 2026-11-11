import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";

export default defineConfig({
  base: "./",
  plugins: [vue()],
  build: {
    outDir: "../Assets",
    emptyOutDir: false,
    cssCodeSplit: false,
    rollupOptions: {
      output: {
        entryFileNames: "app.js",
        assetFileNames: assetInfo => assetInfo.names?.some(name => name.endsWith(".css"))
          ? "styles.css"
          : "assets/[name][extname]"
      }
    }
  }
});
