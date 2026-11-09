/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig, loadEnv } from 'vite'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const alvo = env.API_PROXY_TARGET || 'https://localhost:7080'
  return {
    plugins: [react()],
    server: {
      port: 5173,
      strictPort: true,
      // Proxy de desenvolvimento: as chamadas /api saem do servidor do Vite, não do navegador,
      // então não há requisição cross-origin (nem preflight CORS) e cookies ficam na mesma origem.
      proxy: {
        '/api': { target: alvo, secure: false },        // secure:false só porque o certificado de dev é autoassinado
        '/openapi': { target: alvo, secure: false },
      },
    },
    test: {
      environment: 'jsdom',
      setupFiles: ['./src/testes/setup.ts'],
      css: false,
    },
  }
})
