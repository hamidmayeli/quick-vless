React + TypeScript + Vite + rest_api_faker + PNPM

package.json sample

```json
{
  "name": "outline-manager",
  "private": true,
  "version": "0.0.0",
  "type": "module",
  "scripts": {
    "dev:web": "vite --host 0.0.0.0",
    "mock:api": "npx rest_api_faker --watch ./mock/db.cjs --routes ./mock/routes.json --middlewares ./mock/middleware.cjs --port 3001 --host 0.0.0.0",
    "dev": "concurrently \"npm run mock:api\" \"npm run dev:web\"",
    "build": "tsc -b && vite build",
    "lint": "eslint .",
    "preview": "vite preview"
  },
  "dependencies": {
    "qrcode.react": "^4.2.0",
    "react": "^19.2.8",
    "react-dom": "^19.2.8",
    "react-router-dom": "^7.18.3",
    "recharts": "^3.10.1"
  },
  "devDependencies": {
    "@eslint/js": "^10.0.1",
    "@types/node": "^26.4.1",
    "@types/react": "^19.2.18",
    "@types/react-dom": "^19.2.5",
    "@vitejs/plugin-react": "^6.1.1",
    "concurrently": "^10.0.5",
    "eslint": "^10.9.1",
    "eslint-plugin-react-hooks": "^7.1.1",
    "eslint-plugin-react-refresh": "^0.5.5",
    "globals": "^17.12.0",
    "rest_api_faker": "^0.0.6",
    "typescript": "~7.0.2",
    "typescript-eslint": "^8.69.0",
    "vite": "^8.2.2",
    "vite-plugin-pwa": "^1.3.0",
    "vite-tsconfig-paths": "^6.1.1",
    "workbox-window": "^7.4.1"
  }
}
```

[rest_api_faker](https://github.com/hamidmayeli/rest_api_faker) is used to be able to develop independent of backend.

It is a frontend for [backend](../backend/)
