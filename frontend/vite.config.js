import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 4200,
    proxy: {
      // The api-gateway owns the per-route map and injects the HTTP Basic
      // credential the backends require - pointing dev straight at a bare
      // service would 401 in the browser, and EventSource cannot supply the
      // header itself. Forward the in-cluster gateway to this port first:
      //
      //   kubectl port-forward -n reactive-systems svc/api-gateway 8080:8080
      //
      // Alternatively run one service's Testcontainers launcher (which also
      // listens on 8080) and authenticate with the dev/dev credential - but
      // then only that service's routes exist.
      '/api': 'http://localhost:8080',
    },
  },
});
