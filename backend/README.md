# Control Finance — Backend API

## Stack
- **ASP.NET Core 8** — Web API
- **PostgreSQL** — Banco de dados
- **Entity Framework Core** — ORM + Migrations
- **JWT** — Autenticação stateless
- **BCrypt** — Hash de senhas

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
   > ⚠️ Em produção, use variáveis de ambiente ou Azure Key Vault — nunca versione a chave real.

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
| POST   | `/api/auth/register`  | `name, email, phoneNumber, document, password`          | `201` + JWT       |
| POST   | `/api/auth/login`     | `identifier` (e-mail ou CPF/CNPJ), `password`           | `200` + JWT       |

### Exemplo — Register
```json
POST /api/auth/register
{
  "name": "Bruno Silva",
  "email": "bruno@empresa.com.br",
  "phoneNumber": "81999990000",
  "document": "12345678000199",
  "password": "Senha@123"
}
```

### Exemplo — Login (por e-mail ou documento)
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
  "token": "eyJhbGciOiJIUzI1NiIsInR5...",
  "user": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "name": "Bruno Silva",
    "email": "bruno@empresa.com.br",
    "document": "12345678000199"
  }
}
```

---

## Como o frontend deve usar o token

Após o login/registro, salve o token (ex: `localStorage`) e envie em toda requisição autenticada:

```http
Authorization: Bearer {token}
```

---

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
