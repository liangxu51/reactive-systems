import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 4200,
    proxy: {
      '/api/orders': 'http://localhost:8080',
      '/api/products': 'http://localhost:8081',
    },
  },
});
