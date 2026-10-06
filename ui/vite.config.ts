import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  build: {
    rolldownOptions: {
      output: {
        // Keep eager imports and page lifecycles intact; separate stable code from app changes.
        codeSplitting: {
          groups: [
            { name: 'react-core', test: /node_modules[\\/](react|react-dom|scheduler)[\\/]/ },
            { name: 'translations', test: /src[\\/]locales[\\/]/ },
          ],
        },
      },
    },
  },
})
