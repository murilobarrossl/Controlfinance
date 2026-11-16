# Control Finance - Backend API

## Stack
- **ASP.NET Core 8** - Web API
- **PostgreSQL** - Banco de dados
- **Entity Framework Core** - ORM + Migrations
- **JWT** - Autenticação stateless
- **BCrypt** - Hash de senhas

---

## Pré-requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [PostgreSQL](https://www.postgresql.org/download/) acessível localmente ou em um serviço como Supabase
- [EF Core CLI](https://learn.microsoft.com/ef/core/cli/dotnet): `dotnet tool install --global dotnet-ef`

---

## Configuração

1. **Banco:** informe a conexão PostgreSQL em `appsettings.Local.json`
   (gitignorado) ou na variável `ConnectionStrings__DefaultConnection`.
   Se já usa um banco existente, mantenha esse banco e suas credenciais. A ordem de prioridade,
   da menor para a maior, é `appsettings.json`, `appsettings.{Environment}.json`,
   `appsettings.Local.json`, variáveis de ambiente e argumentos da linha de comando.
   Arquivos `.env` não são carregados automaticamente pela API. O projeto não configura User Secrets.

2. **Segredos:** defina `JwtSettings:SecretKey` (mínimo 32 caracteres) e `Encryption:Key` em
   `appsettings.Local.json` ou em variáveis de ambiente, nunca no `appsettings.json` versionado.
   O passo a passo, com o JSON de exemplo e como gerar as chaves, está no `README.md` da raiz.

   Fora de Development a API **não sobe** se `ConnectionStrings:DefaultConnection`,
   `JwtSettings:SecretKey` ou `Encryption:Key` estiverem ausentes, forem placeholders ou tiverem
   menos de 32 caracteres. Lista completa em `.env.example`.

   **Webhook da Polp** (obrigatório quando `Polp:ClientId`/`Polp:ClientSecret` estão preenchidos):

   | Variável | Origem | Uso |
   |---|---|---|
   | `Polp__WebhookToken` | gerado por você (`openssl rand -hex 32`) | último segmento da URL: `/api/polp/webhook/<token>` |
   | `Polp__WebhookTokenPrevious` | token anterior, opcional | aceito durante a troca de URL |
   | `Polp__WebhookSigningSecret` | campo `secret` devolvido uma única vez pela Polp ao ativar `signing_enabled` | HMAC-SHA256 do corpo, header `X-Webhook-Signature: sha256=<hex>` |

   Os dois segredos precisam ser diferentes entre si e das demais chaves. Respostas: `404` token
   errado, `401` assinatura ausente ou inválida, `503` signing secret não configurado.

3. **Restaure os pacotes e rode a API** (as migrations são aplicadas automaticamente na
   inicialização):
   ```bash
   dotnet restore
   dotnet run
   ```
   A API usa o profile `http` de `Properties/launchSettings.json` (`http://localhost:5080`, ambiente
   Development). Swagger: `http://localhost:5080/swagger`.

4. **Migrations** (só ao alterar Models ou `AppDbContext`):
   ```bash
   dotnet ef migrations add NomeDaMigration
   ```

## Verificar a conexão sem modificar o banco

Na pasta `backend`, execute:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run -- --check-database
```

Esse comando usa a mesma configuração e o mesmo EF Core/Npgsql da API, abre a conexão,
executa `SELECT 1` e consulta o TLS da própria sessão em `pg_stat_ssl`. Encerra com código `0`
no sucesso ou `1` na falha, sem iniciar servidor, jobs, migrations ou backfills.
O teste não exige que o banco esteja com as migrations atualizadas e não consulta dados financeiros.

Falhas de abertura de conexão durante o uso da API também produzem um diagnóstico com a cadeia
de tipos das exceções, `SQLSTATE` do PostgreSQL, códigos de socket e indicação de cancelamento.
Mensagens arbitrárias das exceções não são registradas por esse diagnóstico, pois podem conter
segredos. O evento `Microsoft.EntityFrameworkCore.Database.Connection[20004]` sozinho não
identifica a causa: correlacione-o com esse diagnóstico. Uma conexão bem-sucedida agora não
determina a causa de uma falha anterior.

O perfil local `http` escuta somente em `http://localhost:5080`; o redirecionamento HTTPS e o
HSTS ficam habilitados fora de Development. O TLS da conexão PostgreSQL é independente do HTTP da API.

---

## Endpoints de Auth

| Método | Rota                  | Body                                                    | Resposta          |
|--------|-----------------------|---------------------------------------------------------|-------------------|
| POST   | `/api/auth/register`  | `name, email, document, password, acceptedTermsVersion, acceptedPrivacyVersion, confirmsAdult` | `201` + cookie de sessão |
| POST   | `/api/auth/login`     | `identifier` (e-mail ou CPF/CNPJ), `password`           | `200` + cookie de sessão |

### Exemplo - Register
```json
POST /api/auth/register
{
  "name": "Bruno Silva",
  "email": "bruno@empresa.com.br",
  "document": "12345678000199",
  "password": "Senha@123",
  "acceptedTermsVersion": "2026-10-31",
  "acceptedPrivacyVersion": "2026-10-31",
  "confirmsAdult": true
}
```

### Exemplo - Login (por e-mail ou documento)
```json
POST /api/auth/login
{
  "identifier": "bruno@empresa.com.br",
  "password": "Senha@123"
}
```

### Resposta de sucesso (ambos)
```json
{
  "csrfToken": "q3l2...",
  "user": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "name": "Bruno Silva",
    "email": "bruno@empresa.com.br",
    "documentType": "CNPJ",
    "dueRemindersEnabled": true,
    "monthlySummaryEnabled": false
  }
}
```

---

## Como o frontend usa a sessão

O JWT vai num cookie `httpOnly` (`access_token`), nunca no corpo da resposta nem no
`localStorage`. O frontend manda as requisições com `credentials: "include"` e, em toda requisição
que altera dados (POST/PUT/PATCH/DELETE), o cabeçalho `X-CSRF-Token` com o `csrfToken` recebido
no login/cadastro ou em `GET /api/auth/me` (guardado só em memória). Para Swagger ou clientes de
API, `Authorization: Bearer {token}` continua aceito e dispensa o cabeçalho de CSRF.

## Estrutura de pastas

```
backend/
├── Controllers/    # Endpoints (herdam de ApiControllerBase)
├── DTOs/           # Requests e responses
├── Data/           # AppDbContext (EF Core, conversores de criptografia)
├── Migrations/     # Migrations do EF Core
├── Models/         # Entidades
├── Properties/     # launchSettings.json (porta 5080, Development)
├── Services/       # Regras de negócio, Polp, criptografia, e-mail
├── appsettings.json              # só placeholders
├── Program.cs
└── ControlFinance.API.csproj
```
