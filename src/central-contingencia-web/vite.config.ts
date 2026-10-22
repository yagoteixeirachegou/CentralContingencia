import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import basicSsl from '@vitejs/plugin-basic-ssl'
import { fileURLToPath } from 'node:url'
export default defineConfig({
  plugins: [react(), tailwindcss(), basicSsl()],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  server: {
    host: 'localhost', port: 5173, strictPort: true,
    proxy: { '/api': { target: 'https://localhost:7090', secure: false } },
  },
  test: { environment: 'jsdom', setupFiles: ['./src/test/setup.ts'] },
})
