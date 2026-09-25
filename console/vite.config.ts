import { tanstackRouter } from '@tanstack/router-plugin/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// Local dev only: Aspire starts the api and realtime roles and passes their
// addresses in. The proxy keeps the console same origin, like Caddy does in
// production (spec 0002, request routing).
const apiUrl = process.env.API_HTTP ?? process.env.services__api__http__0 ?? 'http://localhost:8080'
const realtimeUrl =
  process.env.REALTIME_HTTP ?? process.env.services__realtime__http__0 ?? 'http://localhost:8081'

export default defineConfig({
  plugins: [tanstackRouter({ target: 'react', autoCodeSplitting: true }), react()],
  server: {
    proxy: {
      '/v1/realtime': { target: realtimeUrl, ws: true, changeOrigin: true },
      '/v1': { target: apiUrl, changeOrigin: true },
    },
  },
})
