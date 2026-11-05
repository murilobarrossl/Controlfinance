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
  chars), `Encryption:Key`, and for the Polp integration `Polp:ClientId`/`Polp:ClientSecret`/
  `Polp:WebhookToken`/`Polp:WebhookSigningSecret`. Outside Development, `ConfigSecrets.Validate`
  (`Program.cs`) refuses to start if any of these is missing, a placeholder, shorter than 32 chars
  or reused between keys. Polp counts as enabled only when ClientId/ClientSecret are not placeholders.
- Frontend reads `VITE_API_URL` (see `frontend/.env.example`); defaults to
  `http://localhost:5080/api` if unset.
- Requires a local PostgreSQL instance (port 5432 by default).

## Architecture

### Backend request pipeline (`backend/Program.cs`)

Auth is **cookie-based**, not bearer-token-in-JS, despite still using JWTs internally:
- On login/register, `AuthController` issues the JWT inside an `httpOnly` `access_token` cookie
  (`SessionCookie`; `Path=/api`, `Secure` outside Development, `SameSite` from
  `Auth:CookieSameSite`, default `Lax`). JS never reads this cookie, and the JWT is never in a
  response body. Frontend and API should share the same registrable domain in production; with
  `SameSite=None` (different domains) browsers that block third-party cookies break login.
- `OnMessageReceived` in the JWT bearer handler falls back to reading the `access_token` cookie
  when there's no `Authorization` header, so the same auth scheme handles both cookie sessions and
  raw bearer tokens (Swagger, API clients).
- `OnTokenValidated` checks, per request: JTI revocation (`ITokenRevocationService`, makes logout
  real) and `SessionValidator` (user exists, is active, and the token's `tv` claim equals
  `User.TokenVersion`, which is what makes "logout from all devices" and account deletion end
  other sessions). Both use a 30s memory cache.
- CSRF (`CsrfProtection`): every unsafe request authenticated by the cookie must send
  `X-CSRF-Token` equal to the `csrf` claim inside the JWT. The frontend gets that value from the
  body of `/auth/login`, `/auth/register` and `/auth/me` and keeps it in memory only
  (`api/client.js`). Not a double-submit cookie because the frontend can't read cookies of the API
  origin. Requests with an `Authorization` header are exempt (can't be forged cross-site).
- `ApiControllerBase.UserId` is the standard way controllers read the authenticated user's id from
  the `sub` claim; inherit from it rather than parsing claims manually.

### Data protection / encryption

- Sensitive columns (money amounts: `Balance`, `Amount`, `CreditLimit`, `UsedLimit`,
  `TotalAmount`, `InstallmentAmount`, `CurrentInvoiceAmount`; `Transaction.Name`; masked
  `BankAccount.Number`) are encrypted at rest with AES-256-GCM via
  `IEncryptionService`, wired in transparently as EF Core `ValueConverter`s in
  `AppDbContext.OnModelCreating`. Application code always reads/writes plaintext; only the DB
  columns (suffixed `...Encrypted`) hold ciphertext.
- The CPF/CNPJ number is **not stored**: only `DocumentHash` (HMAC-SHA256, used for CPF login,
  uniqueness and to check the document typed on the bank-connection screen) and `DocumentType`.
  `User.Document` is a legacy column being emptied by `PrivacyBackfill`; Polp gets the document
  typed by the user at connection time, in memory only (for a CNPJ connection also the operator CPF, which Polp requires; same rule).
- Because `Transaction.Name` is encrypted, report search and sort-by-name run in memory
  (`TransactionsController.GetReport`), capped like sort-by-amount.
- Data minimization rules (see `RELATORIO_ADEQUACAO_PRIVACIDADE_CONTROL_FINANCE.md`): no Polp
  response bodies in logs or exception messages, no names/amounts in e-mails (Resend is a US
  provider), only last 4 digits of account numbers, no phone number, auth URL not persisted.
- `PrivacyBackfill` runs on every startup (idempotent, batched) to convert pre-existing rows.
- Key ring (`EncryptionService`): optional versioned data keys (`Encryption:Keys:{id}` +
  `ActiveKeyId`, ciphertext prefix `cfk:{id}:`) and **independent** lookup-hash keys
  (`Encryption:LookupKeys:{id}` + `ActiveLookupKeyId`, prefix `cfh:{id}:`), HKDF-derived. Unprefixed
  values are legacy and read with `Encryption:Key`. Always look up hashed values with
  `ComputeLookupHashCandidates` (CPF is not stored, so hash rotation is lazy: re-hashed when the user
  types the document again). `EncryptionKeyRotation` (opt-in `Encryption:RotateOnStartup`) re-encrypts
  every `...Encrypted` column. Never remove a key before rotation finished.
- `Installment.Description` is encrypted too; `RecurrenceDecision.NormalizedName` stores an HMAC
  (`RecurrenceKey`), never the plain normalized transaction name.
- Retention and access logs are opt-in config: `DataRetention:SyncedHistoryMonths` (skip at import +
  daily purge of old imported transactions), `AccessLog:Mode` (`Off` | `Authentication`, IP encrypted,
  purged after `AccessLog:RetentionDays`; rows have no FK to `Users` on purpose).
- Session checks skip the 10s cache for unsafe methods and `/api/account/export`
  (`SessionValidator`, `TokenRevocationService`).
- There is no lawyer or external legal review: legal questions are investigated and decided during
  development; unresolved ones are recorded as open risks in the privacy report.

### Domain model (`backend/Models`, `backend/Data/AppDbContext.cs`)

`User` → `BankAccount`, `Category`, `CreditCard`, `Transaction` (each cascade-deletes with the
user). `Transaction` optionally links to a `BankAccount` and/or `Category` (`SetNull` on delete).
`Installment` optionally links to a `CreditCard`. `PolpIntegration` tracks an open-finance link
per user/institution. `ConsentRecord` is the append-only log of accepted Terms/Privacy versions and
bank-connection notices (versions in `LegalDocuments.cs` and `frontend/src/legal/versions.js`,
must match). `Installment` has no FK to `User`: delete it explicitly. `Transaction`/`Installment`
→ `BankAccount` is `SetNull`, so deleting accounts must go through
`PolpDisconnectService.DeleteAccountsDataAsync` to avoid orphaned statements.

### Polp integration (open finance sync, API v2 / Celcoin)

`IPolpService`/`PolpService` talks to the Polp **v2** API (`Polp:BaseUrl`; testing uses
`.../api/v2/sandbox`, which fabricates data and never touches a real bank). Names kept from v1:
`PolpIntegration` is now a **consent** (`PolpIntegrationId` = consent UUID), `BankAccount.PolpAccountId`
is the account **or card** UUID. A credit card is still a `BankAccount` (`PolpAccountType =
CreditCardAccountDetector.CardType`), because v2 exposes cards as their own resource.

Flow: `PolpController.Connect` creates the consent (`avoidDuplicates`, `cliente_user_id` = local user
Guid, never e-mail) → user authorizes at the bank → `PolpSyncService.SyncOneAsync` runs **once** as
the initial backfill. After that sync is **webhook-driven**: Polp syncs on its own schedule and calls
`POST /api/polp/webhook/{Polp:WebhookToken}` (`PolpWebhookController` → `PolpWebhookProcessor`). The
request must pass two independent checks before the body is parsed: the URL token (constant time,
`WebhookTokenPrevious` allows rotation, 404 if wrong) and `X-Webhook-Signature: sha256=<hex>`, an
HMAC-SHA256 of the raw body with `Polp:WebhookSigningSecret` (401 if missing/invalid; 503 if the
secret isn't configured). The payload is only a notice (`event`, `resource`, `resource_id`,
`query_parameters`) and Polp signs no timestamp, so nothing in it is trusted: unknown ids are ignored,
the date window is rebuilt field by field (`PolpQueryWindow`) and data always comes from the
authenticated API. There is no "sync now"; the only on-demand call
is `POST /api/polp/accounts/{id}/balance-sync`. Regulatory monthly limits per CPF/institution apply:
reconnecting the same CPF burns them, hence `avoidDuplicates`.

Upsert is by Polp transaction id (`Transaction.Description`, per `BankAccountId`); the id is stable
when a card purchase moves from open to billed. `counterparty` and `category_ref` arrive later (a new
webhook) and only fill name/category that were never edited. `category_ref` maps to app categories by
top-level group (`PolpCategoryMap`). `execution_status` `PARTIAL_SUCCESS` never blocks the user. Card
closing/due day come from bills (`bill_closing_date`/`due_date`), not from the card resource.
Loans, financings and investments from Polp are out of scope.

Disconnecting (`DELETE /api/polp/integrations/{id}`, `PolpDisconnectService`) deletes local data
of that connection immediately, marks it `DISCONNECT_PENDING` (ignored by every sync path) and revokes the
consent (`DELETE /consents/{id}`); failures are retried hourly by `PendingDisconnectRetryService`.
Account deletion (`POST /api/account/deletion`) queues failed provider deletions in
`PendingProviderDeletion` (technical id only). The legacy v1 API is announced by Polp as discontinued on
2026-11-28; migration `PolpV2Consents` deleted all v1 data (test connections only).

### Other backend services

- `RateLimitService`: singleton, in-memory sliding-window rate limiting on both identifier
  (email/CPF) and IP for auth endpoints, to stop both credential stuffing and distributed attempts
  against one account.
- `ScheduledEmailService` / `EmailService`: background hosted service sending transactional email
  (via Resend): one aggregated due-date reminder per user (opt-out) and an opt-in monthly notice,
  both without names or amounts.
- `AccountController`: data export (JSON), e-mail preferences, account deletion.
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
  investments, cards); `pages/home/sections/*` compose the public marketing landing page.
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
