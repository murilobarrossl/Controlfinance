# Control Finance - Frontend

React 19 + Vite 8. Requer Node 20.19+ ou 22.12+ (`.nvmrc` indica 22).

```bash
npm install
cp .env.example .env.local   # PowerShell: Copy-Item .env.example .env.local
npm run dev                  # http://localhost:5173
```

`VITE_API_URL` aponta para a API, incluindo `/api` (padrão `http://localhost:5080/api`). Detalhes de
setup completo no `README.md` da raiz.

| Comando | O que faz |
|---|---|
| `npm run dev` | servidor de desenvolvimento |
| `npm run build` | build de produção em `dist/` |
| `npm run preview` | serve o build de produção |
| `npm run lint` | ESLint |
