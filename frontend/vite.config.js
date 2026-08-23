import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 4200,
    proxy: {
      // The docker-compose api-gateway (localhost:8080) owns the per-route
      // map and injects the HTTP Basic credential the backends require -
      // pointing dev straight at a bare service would 401 in the browser.
      // Run `docker compose up api-gateway ...` (or the full stack) first.
      '/api': 'http://localhost:8080',
    },
  },
});
