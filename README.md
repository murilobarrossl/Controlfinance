# Control Finance

Aplicativo web de finanças pessoais: contas, transações, cartões de crédito e parcelas, com
sincronização opcional via open finance (Polp). API em ASP.NET Core 8 (`backend/`) e frontend em
React + Vite (`frontend/`).

## Pré-requisitos

| Ferramenta | Versão | Observação |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download/dotnet/8.0) | 8.0 | o projeto usa `net8.0`; o runtime 8 precisa estar instalado |
| [Node.js](https://nodejs.org/) | 20.19+ ou 22.12+ (o `frontend/.nvmrc` indica 22) | exigência do Vite 8 |
| [PostgreSQL](https://www.postgresql.org/download/) | qualquer versão atual | porta 5432 |

Opcional, só para criar migrations: `dotnet tool install --global dotnet-ef`.

## Rodando localmente

### 1. Banco de dados

Crie um banco vazio, por exemplo `controlfinance_dev`:

```bash
createdb -U postgres controlfinance_dev
```

PowerShell (ou qualquer shell, se `createdb` não estiver no PATH): `psql -U postgres -c "CREATE DATABASE controlfinance_dev;"`

### 2. Backend

Crie `backend/appsettings.Local.json` (gitignorado) com a conexão e as chaves de desenvolvimento:

```json
{
  "ConnectionStrings": { "DefaultConnection": "Host=localhost;Port=5432;Database=controlfinance_dev;Username=postgres;Password=postgres" },
  "JwtSettings": { "SecretKey": "COLE_AQUI_UMA_CHAVE_ALEATORIA" },
  "Encryption": { "Key": "COLE_AQUI_OUTRA_CHAVE_ALEATORIA" }
}
```

Ajuste usuário e senha ao seu PostgreSQL. Gere as duas chaves (32+ caracteres, diferentes entre si):

```bash
openssl rand -base64 32
```

PowerShell: `[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }))`

Depois:

```bash
cd backend
dotnet restore
dotnet run
```

A API sobe em `http://localhost:5080` com `ASPNETCORE_ENVIRONMENT=Development` (definido em
`backend/Properties/launchSettings.json`, sem precisar de variável de ambiente). Swagger em
`http://localhost:5080/swagger`. As migrations do EF Core são aplicadas automaticamente na
inicialização, então o banco precisa existir e estar acessível antes do `dotnet run`.

A porta é 5080 e não 5000 porque o macOS usa a 5000 para o Receptor AirPlay.

### 3. Frontend

Em outro terminal:

```bash
cd frontend
npm install
cp .env.example .env.local
npm run dev
```

PowerShell: `Copy-Item .env.example .env.local`

O app abre em `http://localhost:5173`. `VITE_API_URL` deve apontar para a API, incluindo `/api`
(`http://localhost:5080/api`). Sem a variável, o frontend usa esse mesmo valor.

Outros comandos: `npm run build`, `npm run lint`, `npm run preview`.

## Configuração

Segredos nunca vão para o git. O `backend/appsettings.json` versionado só tem placeholders, e
`appsettings.Development.json`, `appsettings.Local.json` e `.env` são gitignorados. Configure em
`backend/appsettings.Local.json` (tem precedência sobre os demais arquivos) ou com variáveis de
ambiente usando `__` no lugar de `:` (ver `backend/.env.example`).

Para ativar a Polp, acrescente ao `appsettings.Local.json`:

```json
"Polp": { "ClientId": "...", "ClientSecret": "...", "WebhookToken": "...", "WebhookSigningSecret": "..." }
```

```bash
# bash/zsh
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=controlfinance_dev;Username=postgres;Password=postgres"
```

```powershell
# PowerShell
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5432;Database=controlfinance_dev;Username=postgres;Password=postgres"
```

Chaves obrigatórias: `ConnectionStrings:DefaultConnection`, `JwtSettings:SecretKey` (32+ caracteres)
e `Encryption:Key`. Em Development, chave ausente ou placeholder gera só um aviso no console; fora
de Development a API não sobe. A integração Polp só fica ativa quando `Polp:ClientId` e
`Polp:ClientSecret` não são placeholders; sem eles o app funciona normalmente, sem sincronização
bancária. Os detalhes (webhook, assinatura, chaves versionadas) estão em `backend/README.md` e
`backend/.env.example`.

A origem do frontend precisa estar em `Cors:AllowedOrigins` (o padrão já inclui
`http://localhost:5173`).

## Documentação

- `CLAUDE.md`: arquitetura e convenções do projeto.
- `backend/README.md`: API, autenticação e configuração do backend.
- `RELATORIO_ADEQUACAO_PRIVACIDADE_CONTROL_FINANCE.md`: privacidade e proteção de dados.
