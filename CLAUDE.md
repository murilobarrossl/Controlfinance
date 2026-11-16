# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

Control Finance is a personal finance web app: an ASP.NET Core 8 API backend (`backend/`) and a
React + Vite frontend (`frontend/`). Users track bank accounts, transactions and credit cards, synced
automatically via the Polp open-finance integration.

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

Tests live in `backend.Tests/` (xUnit, EF Core SQLite in-memory, references the API project):

```bash
dotnet test backend.Tests                                        # run all (from repo root)
dotnet test backend.Tests --filter "FullyQualifiedName~PolpSync" # run a subset
```

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
  `CurrentInvoiceAmount`; `Transaction.Name`; masked
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
  response bodies in logs or exception messages (only exception: a 422 from Polp is logged as a
  `PolpValidationSummary` with error code, field names and messages with every digit masked), no names/amounts in e-mails (Resend is a US
  provider), only last 4 digits of account numbers, no phone number, auth URL not persisted.
- `PrivacyBackfill` runs on every startup (idempotent, batched) to convert pre-existing rows.
- Key ring (`EncryptionService`): optional versioned data keys (`Encryption:Keys:{id}` +
  `ActiveKeyId`, ciphertext prefix `cfk:{id}:`) and **independent** lookup-hash keys
  (`Encryption:LookupKeys:{id}` + `ActiveLookupKeyId`, prefix `cfh:{id}:`), HKDF-derived. Unprefixed
  values are legacy and read with `Encryption:Key`. Always look up hashed values with
  `ComputeLookupHashCandidates` (CPF is not stored, so hash rotation is lazy: re-hashed when the user
  types the document again). `EncryptionKeyRotation` (opt-in `Encryption:RotateOnStartup`) re-encrypts
  every `...Encrypted` column. Never remove a key before rotation finished.
- `RecurrenceDecision.NormalizedName` stores an HMAC
  (`RecurrenceKey`), never the plain normalized transaction name.
- Retention and access logs are opt-in config: `DataRetention:SyncedHistoryMonths` (skip at import +
  daily purge of old imported transactions), `AccessLog:Mode` (`Off` | `Authentication`, IP encrypted,
  purged after `AccessLog:RetentionDays`; rows have no FK to `Users` on purpose).
- Session checks skip the 10s cache for unsafe methods and `/api/account/export`
  (`SessionValidator`, `TokenRevocationService`).
- There is no lawyer or external legal review: legal questions are investigated and decided during
  development; unresolved ones are recorded as open risks in the privacy report.

### Domain model (`backend/Models`, `backend/Data/AppDbContext.cs`)

`User` → `BankAccount`, `Category`, `Transaction` (each cascade-deletes with the user). `Transaction` optionally links to a `BankAccount` and/or `Category` (`SetNull` on delete).
One login = one holder: the CPF **or** CNPJ given at registration (`User.DocumentType`); there is no
profile/sub-account dimension, so PF/PJ data isolation is plain `UserId` isolation. Bank connection only
accepts that same document (hash check) and refuses an institution whose Polp `type` doesn't serve it
(`DocumentTypes.InstitutionAccepts`). PF/PJ differences in the UI branch on `frontend/src/utils/holder.js`.
`BankAccount.Ownership` (Personal/Business/Mixed) is only a user-editable tag, not an isolation boundary.
`PolpIntegration` tracks an open-finance link
per user/institution. `ConsentRecord` is the append-only log of accepted Terms/Privacy versions and
bank-connection notices (versions in `LegalDocuments.cs` and `frontend/src/legal/versions.js`,
must match). `Transaction` → `BankAccount` is `SetNull`, so deleting accounts must go through
`PolpDisconnectService.DeleteAccountsDataAsync` to avoid orphaned statements. Credit cards come only
from the bank (no manual cards or installments; migration `RemoveManualCardsAndInstallments`).

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

Transactions are never created or freely edited by the user: `TransactionsController` has no POST and
no `PUT {id}` (guarded by a test). Users only personalize `Name`/`CategoryId` (`PUT {id}/details`, which
the sync respects) and `IsFixed`; `DELETE {id}` only removes legacy manual rows (no Polp id in
`Description`). Upsert is by Polp transaction id (`Transaction.Description`, per `BankAccountId`); the id is stable
when a card purchase moves from open to billed. `counterparty` and `category_ref` arrive later (a new
webhook) and only fill name/category that were never edited. `category_ref` maps to app categories by
top-level group (`PolpCategoryMap`). `execution_status` `PARTIAL_SUCCESS` never blocks the user. Card
closing/due day come from bills (`bill_closing_date`/`due_date`), not from the card resource.
Investments and reserved balances ("caixinhas") are imported (`Services/Investments`, encrypted JSON
columns, purged by the history retention, included in the export and listed in the privacy policy);
loans and financings are out of scope. Any new imported data must also be described in the Privacy
Policy and the Privacidade e dados screen (bump `PRIVACY_VERSION` in both places).

Disconnecting (`DELETE /api/polp/integrations/{id}`, `PolpDisconnectService`) deletes local data
of that connection immediately, marks it `DISCONNECT_PENDING` (ignored by every sync path) and revokes the
consent (`DELETE /consents/{id}`); failures are retried hourly by `PendingDisconnectRetryService`.
Account deletion (`POST /api/account/deletion`) asks Polp to revoke every consent of the user (any
status); inside the deletion transaction it locks the user row and queues every consent Polp did not
confirm in `PendingProviderDeletion` (technical id only, retried hourly, never dropped). A consent
created by a `Connect` racing the deletion is queued too (`QueueOrphanConsentAsync`). The legacy v1 API is announced by Polp as discontinued on
2026-11-28; migration `PolpV2Consents` deleted all v1 data (test connections only).

### Employees (Folha de pagamento, CNPJ only)

`EmployeesController` (`api/employees`) is guarded by `[BusinessHolderOnly]` (`Controllers/Filters`), which
reads `User.DocumentType` from the DB on every request (403 for non-CNPJ); every query also filters by
`UserId`. Employee data is administrative input typed by the company (allowed manual entry: it never
creates transactions or payments). Name, CPF, gross salary, Pix key, branch and account are encrypted
columns; uniqueness uses per-company HMACs (`EmployeeKeys.CpfLookupInput`/`PixLookupInput` include the
company id, so the same CPF never correlates across companies or with a login's `DocumentHash`). The API
only returns masks for CPF, Pix key and account. Quick registration (`POST`) and `PUT {id}` take only the five
core fields (name, CPF, role, gross salary, admission; empty CPF on edit keeps it); department and employment
type go through `PUT {id}/classification` (`EmploymentType` is nullable: never presumed CLT); payment data
through `PUT`/`DELETE {id}/pix` and `{id}/bank-account`. Terminating keeps the record (`/termination`,
`/reactivation`); `DELETE {id}` permanently removes an employee only when already terminated (409 otherwise). Name search/sort run in memory after the status/department filter
(names are encrypted), capped at `MaxEmployeesPerCompany`. `GrossSalary` is the informed gross salary,
never presented as total cost: INSS, FGTS, encargos, 13º, férias and payment matching are not built yet.

### Suppliers (Fornecedores, CNPJ only)

`SuppliersController` (`api/suppliers`) mirrors employees: `[BusinessHolderOnly]`, `UserId` isolation,
encrypted legal/trade name, document and payment data, per-company HMAC (`SupplierKeys`) against duplicate
CPF/CNPJ or Pix key, no active/inactive status (`DELETE {id}` removes permanently), 25 per page. Payment rules shared
by both live in `PayeeKeys` (normalization, masks) and `PayeePayment` (Pix/bank parsing and masked views);
the frontend shares `PaymentFields`, `PaymentDialogs`, `RecordDetailParts`, `useRegistryData` and the
`registry-*`/`record-detail-*` styles. "Pagamentos reconhecidos" is a placeholder: payments are not matched
to suppliers yet and must never be matched by name or amount alone.
Form fields in business modals use `components/ui/FormDialog/FormField.jsx` (`TextField`, `MoneyField`,
`DateField`): every text input gets a concrete example as placeholder from `utils/fieldExamples.js`
(`example="role"` or a literal); labels always stay. Pix examples follow the selected key type.

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
- CPF and CNPJ share one dashboard layout. `layouts/DashboardLayout/navigation.js` picks the tabs by
  holder type (`utils/holder.js`, `documentType` from `/auth/me`; unknown type = CPF experience). CNPJ gets
  the mobile drawer docked as a collapsible desktop sidebar (`.dashboard-layout--business`). Routes of
  one environment are 404 for the other: `BusinessOnlyRoute` / `PersonalOnlyRoute` (`routes/HolderRoute.jsx`;
  Categorias and Investimentos are CPF-only, so shared pages must not link there for CNPJ);
  `pages/dashboard/empresa/*` holds the business pages (`BusinessModule` is the shared scaffold; until a
  module is built it shows "Em preparação" and "Ainda não disponível.", never a fake "no records" state).
  Shared pages branch on the holder internally (Receitas e despesas is one page for both: CPF always
  sees the month; CNPJ picks Dia | Mês | Ano, defined in `utils/periods.js` as ranges for the same
  aggregates endpoint; the current year is compared with the same span of the previous year). The
  transaction list stays in Relatórios. `GET /transactions/report` still accepts `excludeTransfers`. Business endpoints added later must check `DocumentType` server-side too.
- `pages/dashboard/*` are the authenticated app screens (income/expenses, reports, categories,
  investments, cards); `pages/home/sections/*` compose the public marketing landing page.
- Savings goals and the emergency reserve live on the server (`PlanningController`,
  `SavingsGoals`/`EmergencyReserves`, names and amounts encrypted). They used to be in
  `localStorage` (namespaced by `cf_current_user_id`); `api/planning.js` `loadPlanning` imports any
  leftover browser copy once via `POST /api/planning/import` (never overwrites server data) and only
  then deletes it locally. `utils/investmentStorage.js`/`emergencyReserveStorage.js` only read that
  legacy copy now.
- Investimentos: one block per product type (`getInvestmentModality`), each application opens its own
  projection. Polp sends no rate for investments, so projections only use a real rule: poupança's legal
  rule (Selic meta), Tesouro Selic's indexer (Selic efetiva) or the rate the user typed
  (`InvestmentRateProfile`, per `BankAccount`). Official rates come from the Banco Central SGS
  (`ReferenceRatesService`, `GET /api/market/reference-rates`, series 432/1178/4389); if unavailable,
  no projection, never a hardcoded rate. `EmergencyReserve.BankAccountId` marks one application as the
  reserve (`SetNull`); the manual reserve amount is kept for when nothing is linked.

## Conventions

- **NEVER add a button, dialog, form or flow to register a bank account or credit card manually**
  ("Cadastrar conta manual", "Adicionar cartão", etc.). The whole product is automatic: accounts and
  cards come only from the bank via Polp. The only way to add an account is "Conectar banco".
  The backend has no endpoint to create or edit an account's name/balance either (only ownership
  tag and delete); don't add one back.

- Backend comments are in Portuguese and are used specifically to explain *why* a non-obvious
  security/infra decision was made (cookie flags, CSRF exemptions, forwarded-headers trust, index
  choices): match that style when touching this code rather than describing *what* the code does.
- When adding a new encrypted column, add it via a value converter in `AppDbContext.OnModelCreating`
  (column name suffixed `Encrypted`) and generate a migration; don't encrypt/decrypt manually in
  controllers/services.
