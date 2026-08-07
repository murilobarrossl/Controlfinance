# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

Control Finance is a personal finance web app: an ASP.NET Core 8 API backend (`backend/`) and a
React + Vite frontend (`frontend/`). Users track bank accounts, transactions, credit cards, and
installments, optionally synced automatically via the Polp open-finance integration.

The `ControlFinance/` directory at repo root is stale (only contains `node_modules`, no source);
ignore it.

## Commands

### Backend (`backend/`, ASP.NET Core 8 / EF Core / PostgreSQL)

```bash
dotnet restore                        # install packages
dotnet run                            # run the API (applies EF migrations automatically on startup)
dotnet build                          # build only
dotnet ef migrations add <Name>       # add a migration after changing a Model/AppDbContext
dotnet ef database update             # apply migrations manually
```

Swagger UI is served at `/swagger` in Development.

There is no automated test project in this repo.

### Frontend (`frontend/`, React 19 + Vite)

```bash
npm run dev        # dev server (default http://localhost:5173)
npm run build       # production build
npm run lint         # ESLint
npm run preview      # preview a production build
```

There is no test runner configured (no Jest/Vitest).

### Local configuration

- Backend secrets go in `backend/appsettings.Local.json` (gitignored) or environment variables
  (double-underscore syntax, see `backend/.env.example`). `appsettings.json` in git only has
  placeholders. Required: `ConnectionStrings:DefaultConnection`, `JwtSettings:SecretKey` (32+
  chars), `Encryption:Key`, and for the Polp integration `Polp:ClientId`/`Polp:ClientSecret`.
- Frontend reads `VITE_API_URL` (see `frontend/.env.example`); defaults to
  `http://localhost:5000/api` if unset.
- Requires a local PostgreSQL instance (port 5432 by default).

## Architecture

### Backend request pipeline (`backend/Program.cs`)

Auth is **cookie-based**, not bearer-token-in-JS, despite still using JWTs internally:
- On login/register, `AuthController` issues the JWT inside an `httpOnly` `access_token` cookie
  (`SameSite=Lax` in dev, `SameSite=None; Secure` in prod, since frontend and API sit on different
  subdomains in production). JS never reads this cookie.
- `OnMessageReceived` in the JWT bearer handler falls back to reading the `access_token` cookie
  when there's no `Authorization` header, so the same auth scheme handles both cookie sessions and
  raw bearer tokens (Swagger, older clients).
- Every validated token is also checked against `ITokenRevocationService` (backed by
  `RevokedToken`/DB) by JTI, which is what makes logout actually invalidate a still-unexpired JWT.
- Because the session cookie is `SameSite=None` in prod, a **double-submit CSRF cookie**
  (`XSRF-TOKEN`, readable by JS) is required on every unsafe (non-GET/HEAD/OPTIONS) request and
  echoed back as the `X-XSRF-TOKEN` header (see `frontend/src/api/client.js`). Requests carrying an
  `Authorization` header instead of relying on the cookie are exempt from this check (can't be
  forged cross-site), which is also what keeps Swagger working unmodified. `/api/auth/login` and
  `/api/auth/register` are exempt too (pre-auth).
- `ApiControllerBase.UserId` is the standard way controllers read the authenticated user's id from
  the `sub` claim; inherit from it rather than parsing claims manually.

### Data protection / encryption

- Sensitive columns (CPF/CNPJ, money amounts: `Balance`, `Amount`, `CreditLimit`, `UsedLimit`,
  `TotalAmount`, `InstallmentAmount`) are encrypted at rest with AES-256-GCM via
  `IEncryptionService`, wired in transparently as EF Core `ValueConverter`s in
  `AppDbContext.OnModelCreating`. Application code always reads/writes plaintext; only the DB
  columns (suffixed `...Encrypted`) hold ciphertext.
- `User.Document` (CPF/CNPJ) is encrypted and therefore not directly queryable; `DocumentHash` is a
  deterministic HMAC-SHA256 lookup hash used for uniqueness/search instead.
- The ASP.NET Data Protection key ring (used for the antiforgery/CSRF tokens) is persisted to
  Postgres (`PersistKeysToDbContext`), not the filesystem: required so CSRF tokens survive
  restarts/redeploys and work across multiple instances.

### Domain model (`backend/Models`, `backend/Data/AppDbContext.cs`)

`User` → `BankAccount`, `Category`, `CreditCard`, `Transaction` (each cascade-deletes with the
user). `Transaction` optionally links to a `BankAccount` and/or `Category` (`SetNull` on delete).
`Installment` optionally links to a `CreditCard`. `PolpIntegration` tracks an open-finance link
per user/institution.

### Polp integration (open finance sync)

`IPolpService`/`PolpService` talks to the external Polp API; `PolpController` orchestrates the
flow: list connectors → create integration (redirects user to bank auth) → poll integration status
→ sync accounts + transaction history into local `BankAccount`/`Transaction` rows. Sync is
idempotent (matches existing accounts by `PolpAccountId`, existing transactions by
`(BankAccountId, Description)` where `Description` stores the remote transaction id) and
per-integration failures don't abort a `sync-all` batch. First sync for a user seeds a default set
of categories.

### Other backend services

- `RateLimitService`: singleton, in-memory sliding-window rate limiting on both identifier
  (email/CPF) and IP for auth endpoints, to stop both credential stuffing and distributed attempts
  against one account.
- `ScheduledEmailService` / `EmailService`: background hosted service sending transactional email
  (via Resend), e.g. due-date reminders.
- `TokenRevocationService`: backs the logout flow described above.

### Frontend structure (`frontend/src`)

- Routing in `App.jsx` (`react-router-dom` v7). Only the home page is in the initial bundle;
  every other route (auth, dashboard pages, legal pages) is `lazy()`-loaded per route.
- `AuthContext` holds the current user in memory only; there is no client-readable session token to
  persist. On mount it calls `GET /api/auth/me` to discover whether a session cookie is already
  valid. `ProtectedRoute` gates dashboard/bank-connection routes on this.
- `api/client.js`'s `apiFetch` is the single fetch wrapper: sends cookies (`credentials:
  "include"`), attaches the CSRF header on unsafe methods, and redirects to `/loginemail` on a 401
  from any non-auth endpoint (session expired/revoked). All `api/*.js` modules build on this.
- `pages/dashboard/*` are the authenticated app screens (income/expenses, reports, categories,
  investments, budget); `pages/home/sections/*` compose the public marketing landing page.
- Some non-sensitive, per-user local state (investment goals, emergency reserve) is kept in
  `localStorage`, namespaced by the user id that `AuthContext` mirrors there
  (`cf_current_user_id`), see `utils/investmentStorage.js`, `utils/emergencyReserveStorage.js`.

## Conventions

- Backend comments are in Portuguese and are used specifically to explain *why* a non-obvious
  security/infra decision was made (cookie flags, CSRF exemptions, forwarded-headers trust, index
  choices): match that style when touching this code rather than describing *what* the code does.
- When adding a new encrypted column, add it via a value converter in `AppDbContext.OnModelCreating`
  (column name suffixed `Encrypted`) and generate a migration; don't encrypt/decrypt manually in
  controllers/services.
