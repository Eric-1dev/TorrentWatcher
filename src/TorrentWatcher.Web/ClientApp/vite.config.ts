import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// Сборка кладётся прямо в wwwroot ASP.NET-проекта: один контейнер, один порт.
export default defineConfig({
  plugins: [vue()],
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5080',
        changeOrigin: true,
      },
      '/openapi': {
        target: 'http://localhost:5080',
        changeOrigin: true,
      },
    },
  },
})
