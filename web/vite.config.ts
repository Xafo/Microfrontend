import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Se publica dentro del sitio IIS de la API (wwwroot). Rutas relativas para funcionar
// en cualquier aplicación/virtual directory de IIS.
export default defineConfig({
  plugins: [react()],
  base: './',
  build: {
    outDir: '../src/Enrolamiento.EnvioCorreos.Api/wwwroot',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: { '/api': 'http://localhost:5180' },
  },
})
