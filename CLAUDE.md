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
- For employee/supplier data the company is controller and Control Finance is operator (Terms section 5, Privacy
  Policy section 1); counterparty HMACs are Control Finance's own processing under legitimate interest, assessed in
  `docs/lgpd-legitimo-interesse.md`. Changing the CNPJ modules or the payment matcher means updating the Policy,
  Terms, Privacidade e dados and that assessment.

### Domain model (`backend/Models`, `backend/Data/AppDbContext.cs`)

`User` → `BankAccount`, `Category`, `Transaction` (each cascade-deletes with the user). `Transaction` optionally links to a `BankAccount` and/or `Category` (`SetNull` on delete).
One login = one holder: the CPF **or** CNPJ given at registration (`User.DocumentType`); there is no
profile/sub-account dimension, so PF/PJ data isolation is plain `UserId` isolation. Bank connection only
accepts that same document (hash check) and refuses an institution whose Polp `type` doesn't serve it
(`DocumentTypes.InstitutionAccepts`). PF/PJ differences in the UI branch on `frontend/src/utils/holder.js`.
`BankAccount.Ownership` (Personal/Business/Mixed) is only a user-editable tag, not an isolation boundary.
`BankAccount.IsDefault` (CNPJ only, `PUT /api/bank-accounts/{id}/default`, `DELETE /api/bank-accounts/default`, set from the
account switcher menu) marks the company's default account: one per user (partial unique index + user-row lock),
only a non-card account of an `AUTHORISED` connection; `/polp/integrations` returns `isDefault` only while the
connection is `AUTHORISED`, never promotes another account, and never changes the selected scope or any total. A CNPJ consent created when the company has no
bank account gets `PolpIntegration.AutoDefaultPending`; syncs of a flagged consent try `DefaultBankAccounts.AssignPendingAsync`
(retried until it succeeds, never fails the sync); once any default exists every flag is cleared, so later banks never become
default automatically. Changing the default refetches only `/polp/integrations`, never `bumpDataRefresh`.
`PolpIntegration` tracks an open-finance link
per user/institution. `ConsentRecord` is the append-only log of accepted Terms/Privacy versions and
bank-connection notices; existing users get `UserInfoDto.PendingPrivacyVersion` from `/auth/me`/login until they
acknowledge the current version in the dashboard notice (`POST /api/account/privacy-acknowledgement`, same log,
`Kind = privacy_notice` = ciência, never consent; `ConsentKinds.Manifestation` maps each kind to ciência / aceite
contratual / consentimento / declaração; a version that needs consent sets `LegalDocuments.PrivacyVersionRequiresConsent`
and must collect `ConsentKinds.SpecificConsent` per purpose in its own flow) (versions in `LegalDocuments.cs` and `frontend/src/legal/versions.js`,
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
columns, purged by the history retention, included in the export and listed in the privacy policy).
Loan contracts (CPF only): CPF consents request `CREDIT_OPERATIONS` (`PolpService.ProductsFor`); older consents
get it only by reconnecting (recreate). `PolpLoanImportService` mirrors `GET /consents/{id}/loans` into
`LoanContracts` (minimized encrypted JSON) on backfill, reconcile and `loans` webhooks; `GET /api/loans` is
read-only and contracts never enter totals. Loan proceeds in the statement (`LOAN_DISBURSEMENTS*`) are
`PolpMovementKind.LoanDisbursement`, out of totals. Migration `LoanContracts` is schema-only (an earlier
local version wiped every bank connection and was applied only to the dev database); migrations must never
delete bank data or queue consent revocations.
Financings are out of scope. Any new imported data must also be described in the Privacy
Policy and the Privacidade e dados screen (bump `PRIVACY_VERSION` in both places).

Disconnecting (`DELETE /api/polp/integrations/{id}`, `PolpDisconnectService`) deletes local data
of that connection immediately, marks it `DISCONNECT_PENDING` (ignored by every sync path) and revokes the
consent (`DELETE /consents/{id}`); failures are retried hourly by `PendingDisconnectRetryService`.
An authorization ended at the bank (`PolpAuthorizationEnd.EndedStatuses`: REVOKED, EXPIRED, REJECTED, terminal
locally) is not a disconnect: its accounts get `IsActive = false` (every read of bank data must filter `IsActive`;
investment/reserve reads exclude ended statuses), `AuthorizationEndedAt` is stamped, and after
`PolpAuthorizationEnd.Retention` (30 days, written in the connection notice, Privacy Policy and Privacidade e dados)
`PurgeEndedConnectionsAsync` runs the same `DisconnectAsync`, only after Polp confirms the consent is still ended.
Employees, suppliers, marks and costs are never deleted by it.
`DeleteAccountsDataAsync` (every path that removes bank accounts: disconnect, ended-authorization purge, single
account delete) also deletes the cash closings that used those accounts (scope = the account, an item pointing to
one of its transactions, or an all-accounts closing with balances from its first balance reading on) and nulls
`PayeePaymentMarkEvents.BankPaidAmount` in the months its transactions covered; marks and mark events stay. The Open Finance partnership framing
(RC 1/2020 arts. 36-38, RC 21/2026) is unconfirmed pending Polp/Celcoin (privacy report, section N).
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
only returns masks for CPF, Pix key and account; the full Pix key only via
`GET {id}/payment-methods/{methodId}/pix-key`, loaded by the employee detail ("Dados de pagamento").
Employees and suppliers have up to `PayeePayment.MaxMethodsPerPayee` payment methods each
(`EmployeePaymentMethods`/`SupplierPaymentMethods`, same columns via the unmapped base `PayeePaymentMethod`,
composite FK `(UserId, EmployeeId|SupplierId)`, Pix hash unique per company), managed by
`POST/PUT/DELETE {id}/payment-methods` with shared parsing in `PayeePayment.ParseMethod`/`Assign`/`Views`;
the detail screens show full Pix keys via `{id}/payment-methods/{methodId}/pix-key`. Quick registration (`POST`) and `PUT {id}` take only the five
core fields (name, CPF, role, gross salary, admission; empty CPF on edit keeps it); department and employment
type go through `PUT {id}/classification` (`EmploymentType` is nullable: never presumed CLT); payment data
through `{id}/payment-methods`. Terminating keeps the record (`/termination`,
`/reactivation`); `DELETE {id}` permanently removes an employee only when already terminated (409 otherwise). Name search/sort run in memory after the status/department filter
(names are encrypted), capped at `MaxEmployeesPerCompany`. `GrossSalary` is the informed gross salary,
never presented as total cost: INSS, FGTS, encargos, 13º, férias and payment matching are not built yet.

### Suppliers (Fornecedores, CNPJ only)

`SuppliersController` (`api/suppliers`) mirrors employees: `[BusinessHolderOnly]`, `UserId` isolation,
encrypted legal/trade name, document and payment data, per-company HMAC (`SupplierKeys`) against duplicate
CPF/CNPJ or Pix key, no active/inactive status (`DELETE {id}` removes permanently), 25 per page. Payment rules shared
by both live in `PayeeKeys` (normalization, masks) and `PayeePayment` (Pix/bank parsing and masked views);
the frontend shares `PaymentFields`, `PaymentDialogs`, `RecordDetailParts`, `useRegistryData` and the
`registry-*`/`record-detail-*` styles. Suppliers have **no payment tracking at all** (no paid status, filter,
counter, marks, amounts or bank matching; migration `RemoveSupplierPaymentStatus` dropped them): they are a registry of
contacts and payment methods only. Employee payment status (list check, `payment=paid|unpaid` filter, detail) uses
`PayeePaymentStatus` + `SupplierPaymentMatcher.MatchEmployee` over the month's paid outgoing Polp
transactions (`PolpMovementKind.None`). Polp v2 sends no Pix key or end-to-end id, only `partie_*` (recipient
bank, branch, number, check digit, person type, CPF/CNPJ), so matching uses exact bank identifiers only, never names:
`Transaction.CounterpartyCpfHash` (per-company HMAC = `EmployeeKeys.CpfLookupInput`, computed by the sync from
`partie_cnpj_cpf` for CNPJ logins only; plain CPF never stored) equal to the employee's CPF or to one of their CPF-type
Pix keys (a CPF key is the holder's CPF); a present CPF decides alone. Without any document and not `PESSOA_JURIDICA`,
`CounterpartyAccountHash` equal to a registered TED account. Each payment method also has the CPF/CNPJ of who
receives (`HolderDocumentType`/`HolderDocument`, encrypted, required for TED and e-mail/phone/random Pix keys, taken
from the key itself for CPF/CNPJ keys, null in old methods, never presumed): a bank document equal to a method holder
identifies the employee too, unless the bank also sends an account that differs from that TED method's account. A
holder CNPJ only counts when the CNPJ came from the bank (`Transaction.CounterpartyCnpjFromBank`, set by the sync,
details version 5). The Pix key itself is never comparable (Polp does not send it). Ambiguity = nothing. On top of it, the user's manual
decision for the month (`EmployeePaymentMarks`, one row per employee and `ReferenceMonth` = 1st of
the month in the user's time zone, unique index, history kept) wins: final paid = mark ?? bank. Marks never touch
transactions; the source (`Bank` | `ManualPaid` | `ManualUnpaid`) is returned by `GET/PUT {id}/payment-month` and the
UI never shows a manual mark as bank-confirmed. The PUT carries the month shown and gets 409 after the month turns.
Filters: `payment=paid|unpaid`; list `paymentStatus` = `Bank` | `Manual` | null.
Every mark change appends a `PayeePaymentMarkEvents` row (audit only, never updated, previous/new status, bank
amount at the time encrypted) in the same `SaveChanges`; the mark's `Version` is a concurrency token, so events are
ordered and "previous" is always the real one. Events have no FK to the payee (survive its deletion), cascade with
`Users`, are in the export. "Pago no mês" (`BankPaidAmount`, list and `payment-month`) is only the decimal sum of the
month's bank-identified transactions (`PayeeMonthStatus`). A bank CPF hash wins over a CNPJ on the same row (that CNPJ can only be
Polp enrichment, since the sync clears the hash when the bank sends a CNPJ), so Pix to e-mail/phone/random keys is
identified when the bank sends the recipient CPF. The only paid amount is the bank one: the manual mark is status
only, with no amount (migration `EmployeeMarksWithoutAmount` dropped it; old declared values survive only in
`PayeePaymentMarkEvents.Previous/NewManualAmount`, never written again). UI: the bank amount is a field in "Dados
principais"; the manual "Pago este mês" checkbox is a quiet control at the end of the page (`usePaymentMonth` +
`PaymentMonthToggle`). Card transactions are
never evidence (their CNPJ comes only from Polp enrichment); the bank's `partie_cnpj_cpf` CNPJ overrides the enriched one.
Polp v2 exposes no transaction status, refund type or link to an original transaction, so reversals are not netted.
"Identified bank payment" never means salary fully paid, and its absence is not "overdue".
Custos adicionais e encargos (`PayrollController`, `api/payroll`, `[BusinessHolderOnly]`): nothing is calculated;
every amount is typed by the company. One table, `PayrollObligations`: `EmployeeCost` = free-named employee cost
(`Description` encrypted, `Amount` nullable = not informed, `POST/PUT/DELETE employees/{id}/costs`, max 50 per
employee); every employee gets "13º salário" and "Férias" once, without amount (`EmployeeCostDefaults.Seed`: on
creation, or on the first `GET employees/{id}` under the user-row lock for older employees; `Employee.CostsSeeded`
makes sure a deleted cost never comes back). Migration `EmployeeCosts` discarded the old 13º/férias rows (and their
links and events) without conversion. Costs are only title + value (modal "Adicionar custo"/edit) and have no bank
links (migration `RemoveEmployeeCostLinks`). "Total cadastrado" sums only informed amounts. Charges (Inss/Fgts/Irrf/OtherCharge, `EmployeeId` null, paid consolidated, one
Inss/Fgts/Irrf per competence) keep the "Encargos da folha" page. Bank payments are never assigned automatically:
only for charges, the user attributes part of a real outgoing (`PayrollObligationPayments.Amount`, sum per transaction
never above its amount, user-row lock), candidates only from `IdentifiedTaxPayments` to a verified authority
(INSS/Other: RFB or INSS; IRRF: RFB; FGTS: none). Status `Identified` only when the bank amount covers the typed
amount. The employee's monthly "pago no mês" is not affected by any of this. Every change appends
`PayrollObligationEvents` (no FK, name and amounts encrypted). All three tables are in the export.
Form fields in business modals use `components/ui/FormDialog/FormField.jsx` (`TextField`, `MoneyField`,
`DateField`): every text input gets a concrete example as placeholder from `utils/fieldExamples.js`
(`example="role"` or a literal); labels always stay. Pix examples follow the selected key type.

### Fechamento de caixa (Fluxo de caixa, CNPJ only)

`CashClosingsController` (`api/cash-flow/closings`, `[BusinessHolderOnly]`) closes/reopens one day per scope
(`ScopeAccountId`, `Guid.Empty` = all accounts). A closing is immutable (`CashClosing` + `CashClosingItem` snapshot of every
Polp transaction of the day: id, type, kind, encrypted amount, no FK to `Transactions`); reopening only stamps
`ReopenedAt` (`Version` concurrency token) and closing again creates a new row, so history is kept. One active closing per
day/scope via a partial unique index. Close sends the totals the user saw (409 if a sync changed them); close and reopen only for
the current day in the user's time zone (past days are read-only); the history stays in the DB/export, not in the UI. Totals follow the aggregates rule (`PolpMovementKind.None` only); opening/closing balances come only from
`BankBalanceObservation` (last reading of the previous day / of the day, null if any cash account lacks one), never
rebuilt from transactions. They are point readings (time returned per account), so the day shows a reconciliation:
balance change vs posted cash-account movements (incl. transfers/bills/loans), leftover shown as an unexplained difference. Late or corrected Polp data never touches the closing: `CashClosingLedger.Adjustments` diffs
snapshot vs current (`added`/`changed`/`removed`, each with its income/expense delta, so closing + deltas = current).
Purged with the synced-history retention (which also nulls the bank amount of older mark events) and when an account it
used is removed (see Polp disconnect), included in the export. The page is day-only; month/year comparison lives
at the bottom (`CashAnalysis`) and `fluxo-de-caixa/mes/:month` (`CashMonth`) details a month.

### Impostos (CNPJ only)

`TaxesController` (`GET api/taxes?year&month[&bankAccountId]`, `[BusinessHolderOnly]`) lists bank payments identified as
taxes, one query per year over `TaxPaymentDetection.IdentifiedTaxPayments` (paid account debits, `PolpMovementKind.None`,
never cards). Always requires `PolpTaxPayment` (`category_ref = GOVERNMENT_AND_NON_PROFIT_TAX_PAYMENT`, Polp's AI
classification, never enough alone) **plus** one bank fact: `Transaction.TaxAuthority` (bank-sent `partie_cnpj_cpf` equal
to a verified authority CNPJ: Receita Federal, INSS; a CNPJ alone never identifies a tax) or bank type
`CONVENIO_ARRECADACAO` (also utilities, so never alone). Both flags are written by the sync (details version 6, migration
`TaxPayments`). Names/descriptions never decide; never add an authority CNPJ without an official source. Polp sends
no DARF/DAS/GPS revenue code, so the specific tax is never shown, only the authority. Read-only: no totals elsewhere change;
"identified bank payment" never means the obligation is settled, and its absence never means unpaid.

### Other backend services

- `RateLimitService`: singleton, in-memory sliding-window rate limiting on both identifier
  (email/CPF) and IP for auth endpoints, to stop both credential stuffing and distributed attempts
  against one account.
- `ScheduledEmailService` / `EmailService`: background hosted service sending transactional email
  (via Resend): one aggregated due-date reminder per user (opt-out) and an opt-in monthly notice,
  both without names or amounts.
- `AccountController`: data export (JSON), e-mail preferences, account deletion. The deletion itself lives in
  `AccountDeletionService` (idempotent), shared with the inactivity job.
- Inactivity: `User.LastActivityAt` is written only by login and by `OnTokenValidated` after a valid session
  (`UserActivity`, at most every 12 h: memory cache + conditional UPDATE that also resets the notice fields; never
  from webhooks/jobs). It fails closed: if the write can't be confirmed (one retry on transient errors) the request
  gets 503 (`ActivityNotRecordedException`, treated as transient by `DatabaseFailure`) and login issues no token, so
  the app is never used without recorded activity; the cache is set only after the DB confirmed it. `InactiveAccountService` (daily, `JobLockKeys.InactiveAccounts`, index on `LastActivityAt`)
  e-mails notices 90/30/7 days before `LastActivityAt + 3 years`; a notice counts only after Resend accepts it, a late
  notice pushes `InactivityDeletionAt` to keep its lead, and no e-mail config means no notices and so no deletion.
  Deletion is two-phase: a conditional UPDATE claims the account (3 years + 12 h without activity, all three notices
  for the same `LastActivityAt` via `InactivityNoticeEpoch`, announced date past; sets `IsActive = false`,
  `TokenVersion + 1`, `InactivityDeletionClaimedAt`); only claims older than 1 h are deleted, after `ConfirmAsync`
  re-checks everything under a row lock and gives the account back on any mismatch. A successful confirmation sets
  `InactivityNoticeStage = DeletionStartedStage` (4) in the same transaction, before any destructive step: from then
  on the account is never given back and `AccountDeletionService` is only resumed (activity no longer resets it).
  A claim still not started after 7 days (persistent confirmation failure) is undone: account active again, data
  intact, notice sequence restarted, which also voids the old attempt. Max 50 per round, one scope per account. Access logs survive (no FK); consent records go with the account.
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
- Investimentos: one block per product type, each item opens its own page (`InvestmentDetail.jsx`). The app is a
  mirror of the bank: it never calculates applied amount, yield, profitability, average price or appreciation (Polp v2
  sends none of them for any product), only shows bank fields (balances, quantity, unit prices, taxes, contract terms,
  movements, position history). Any older text below about projections refers to the manual simulators. Polp sends no rate for investments, so projections only use a real rule: poupança's legal
  rule (Selic meta), Tesouro Selic's indexer (Selic efetiva) or the rate the user typed
  (`InvestmentRateProfile`, per `BankAccount`). Official rates come from the Banco Central SGS
  (`ReferenceRatesService`, `GET /api/market/reference-rates`, series 432/1178/4389); if unavailable,
  no projection, never a hardcoded rate. The emergency reserve is automatic only: caixinhas (reserved
  balances) whose user-given name contains "reserva", "emergência" or "colchão" (`EmergencyReserveNames`, computed in
  `PlanningController.LoadAsync`); applications never enter, there is no manual marking, and a caixinha already linked
  to a goal stays in the goal. Migration `AutomaticEmergencyReserve` dropped the old manual links.

## Conventions

- **NEVER add a button, dialog, form or flow to register a bank account or credit card manually**
  ("Cadastrar conta manual", "Adicionar cartão", etc.). The whole product is automatic: accounts and
  cards come only from the bank via Polp. The only way to add an account is "Conectar banco".
  The backend has no endpoint to create or edit an account's name/balance either (only ownership
  tag and delete); don't add one back.

- Backend comments are in Portuguese and are used specifically to explain *why* a non-obvious
  security/infra decision was made (cookie flags, CSRF exemptions, forwarded-headers trust, index
  choices): match that style when touching this code rather than describing *what* the code does.
- Migrations never delete audit trails (`*Events`), user choices or bank data unless the product owner decided it
  and the decision is recorded here; the existing ones (`RemoveSupplierPaymentStatus`, `EmployeeCosts`,
  `RemoveEmployeeCostLinks`, `AutomaticEmergencyReserve`, `RemovePayrollCharges`) were such decisions, applied
  only to the dev database.
- When adding a new encrypted column, add it via a value converter in `AppDbContext.OnModelCreating`
  (column name suffixed `Encrypted`) and generate a migration; don't encrypt/decrypt manually in
  controllers/services.
