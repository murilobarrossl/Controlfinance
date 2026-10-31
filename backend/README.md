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
- [PostgreSQL](https://www.postgresql.org/download/) rodando localmente (porta 5432)
- [EF Core CLI](https://learn.microsoft.com/ef/core/cli/dotnet): `dotnet tool install --global dotnet-ef`

---

## Configuração

1. **Clone/copie** a pasta `ControlFinance.API` para seu projeto.

2. **Configure o banco** em `appsettings.json`:
   ```json
   "ConnectionStrings": {
     "DefaultConnection": "Host=localhost;Port=5432;Database=controlfinance;Username=postgres;Password=SUA_SENHA"
   }
   ```

3. **Configure a chave JWT** (mínimo 32 caracteres):
   ```json
   "JwtSettings": {
     "SecretKey": "COLOQUE_UMA_CHAVE_FORTE_AQUI_COM_32_CHARS+"
   }
   ```
   > ⚠️ Em produção, use variáveis de ambiente ou Azure Key Vault - nunca versione a chave real.

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

4. **Restaure os pacotes:**
   ```bash
   dotnet restore
   ```

5. **Crie e aplique a migration inicial:**
   ```bash
   dotnet ef migrations add InitialCreate
   dotnet ef database update
   ```
   > O `Program.cs` já chama `db.Database.Migrate()` automaticamente na inicialização em dev.

6. **Rode a API:**
   ```bash
   dotnet run
   ```
   Acesse o Swagger em: `http://localhost:5000/swagger`

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
ControlFinance.API/
├── Controllers/
│   └── AuthController.cs       # Rotas POST /api/auth/register e /login
├── DTOs/
│   └── AuthDtos.cs             # Request e Response DTOs
├── Models/
│   └── User.cs                 # Entidade do banco
├── Data/
│   └── AppDbContext.cs         # EF Core DbContext
├── Services/
│   ├── AuthService.cs          # Lógica de negócio
│   └── TokenService.cs         # Geração de JWT
├── appsettings.json
├── Program.cs
└── ControlFinance.API.csproj
```
