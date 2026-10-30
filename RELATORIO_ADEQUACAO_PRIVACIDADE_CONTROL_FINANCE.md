# Relatório de adequação de privacidade — Control Finance

**Data:** 30/10/2026 (etapa 1, Partes A–G) · atualizado em 31/10/2026 (etapa 2, Partes H–J)
**Base:** `AUDITORIA_JURIDICA_CONTROL_FINANCE.md`, reverificada contra o código.

**Sem revisão jurídica externa.** Não existe advogado nem consultoria jurídica envolvidos no projeto. As questões jurídicas são tratadas no próprio desenvolvimento: investigar a norma em fonte oficial, decidir com fundamento e implementar. Quando a norma não permite conclusão segura, isso fica registrado como **incerteza jurídica em aberto** (Parte E.5 e Parte I), nunca como conformidade presumida.

> **Correção de 31/10/2026:** a versão de 30/10 remetia vários pontos a "validação por advogado" e classificava itens como "Dependente de validação jurídica". Essa premissa estava errada. As referências foram corrigidas neste documento, na auditoria, nas páginas jurídicas e nos comentários do código; nenhum risco foi removido.

**Aviso importante sobre o estado do código.** Nada foi compilado, executado ou testado, como pedido. Toda alteração deste relatório foi feita por edição e revisada por leitura. "Resolvido" na Parte G significa "implementado no código, aguardando sua verificação", nunca "testado". As duas migrations novas foram **escritas à mão**, porque `dotnet ef migrations add` compila o projeto (ver Parte E.1).

---

## Resumo

- **O que o sistema deixou de coletar, guardar ou enviar:**
  - deixou de coletar telefone;
  - deixou de guardar o número do CPF/CNPJ e o número completo de conta e cartão;
  - deixou de gravar em log o JSON bruto da Polp;
  - os e-mails deixaram de levar nome, transações e valores ao provedor americano;
  - deixou de devolver o CPF ao navegador;
  - o token de sessão saiu do `localStorage`.
- **O que passou a funcionar de verdade:**
  - a desconexão de banco apaga os dados e pede a exclusão na Polp;
  - existe exclusão de conta;
  - existe cópia dos dados (exportação);
  - existe "sair de todos os aparelhos";
  - os aceites ficam registrados com versão;
  - há um aviso antes da conexão bancária;
  - as preferências de e-mail são controladas pelo usuário.
- **O que foi corrigido na comunicação:** saíram "nunca guardamos seus dados", "ponta a ponta", "não compartilhamos com terceiros", "tempo real", "estrutura regulamentada pelo Banco Central", a exportação para imposto de renda e o botão "Assine agora" sem checkout.
- **O que continua bloqueando o lançamento:**
  - identificação da empresa;
  - condições comerciais e checkout;
  - contrato de produção com a Polp. A API v1 usada aqui foi anunciada pela Polp como **descontinuada em 28/11/2026**;
  - mecanismo de transferência internacional;
  - questões jurídicas ainda em aberto (Parte I).

---

## A. Alterações implementadas

### A.1 Dados que deixaram de ser coletados

| Dado | Antes | Agora | Arquivos |
|---|---|---|---|
| Telefone | Obrigatório no cadastro, sem nenhum uso | Não é pedido. A coluna é removida pela migration | `Cadastro.jsx`, `AuthDtos.cs`, `User.cs`, `20261030120000_PrivacyHardening.cs` |
| E-mail dentro do JWT | Claim `email` em todo token | Removido | `TokenService.cs` |

### A.2 Dados que deixaram de ser armazenados

| Dado | Antes | Agora | Arquivos |
|---|---|---|---|
| Número do CPF/CNPJ | Cifrado de forma recuperável (`Users.Document`) | Só `DocumentHash` (HMAC) e `DocumentType` (CPF/CNPJ). Na conexão bancária o usuário digita o documento, que é conferido com o hash, enviado à Polp e descartado | `User.cs`, `AuthService.cs`, `PolpController.cs`, `PrivacyBackfill.cs`, `ConectarBanco.jsx` |
| Número de conta e cartão | Número completo enviado pela Polp, cifrado | Só "•••• 1234" (cifrado) | `AccountNumberMask.cs`, `PolpSyncService.cs`, `PrivacyBackfill.cs` |
| URL de autenticação da Polp | Gravada em `PolpIntegrations` | Não é gravada (vai direto ao navegador). Coluna removida | `PolpIntegration.cs`, `PolpController.cs`, migration |
| Nome e descrição de transações | Texto puro (inclui nomes de terceiros em transferências) | Cifrado (`NameEncrypted`). Busca e ordenação por nome passam a ser feitas em memória | `AppDbContext.cs`, `TransactionsController.cs`, `PrivacyBackfill.cs` |
| Dados de contas "desconectadas" | Ficavam guardados para sempre (`IsActive = false`) | Apagados na desconexão; os já existentes são apagados pela migration `PurgeDisconnectedAccountData` | `PolpDisconnectService.cs`, `BankAccountsController.cs`, `20261030120100_PurgeDisconnectedAccountData.cs` |
| JSON bruto da Polp em log | Contas, cartões, faturas e extrato completo, em `Information` | Nenhum corpo de resposta da Polp vai para log ou exceção | `PolpService.cs` |
| Token de sessão no navegador | JWT no `localStorage` | Cookie `httpOnly`. O navegador guarda só um id técnico do usuário, para separar metas e reserva por pessoa | `client.js`, `AuthContext.jsx`, `authToken.js`, `SessionCookie.cs` |

### A.3 Dados que deixaram de ser enviados

| Para quem | Antes | Agora |
|---|---|---|
| Resend (EUA) | Nome no assunto e no corpo; nome e valor de cada conta pendente; receitas, despesas e saldo do mês | Só o endereço de e-mail e um aviso genérico: "N contas vencem nos próximos dias", "o resumo está no painel" |
| Navegador | CPF/CNPJ completo em todo `/auth/me` e no login | Só o tipo do documento |
| Navegador | Corpo de erro da Polp (`detail = ex.Message`), que pode trazer `tax_id` e detalhes internos | Mensagem genérica |
| Logs da hospedagem | Extratos, saldos, números de conta e e-mail do usuário em avisos de falha de e-mail | Só ids técnicos e tipo de erro. `HttpClient` e EF em `Warning` |
| Polp | CPF/CNPJ (lido do banco de dados) | CPF/CNPJ digitado pelo usuário na hora. **Continua sendo enviado**, porque a Polp exige o documento para criar a integração |

### A.4 Controle do usuário (funcionalidades novas)

| Funcionalidade | Endpoint | Tela |
|---|---|---|
| Desconectar banco: apaga contas, cartões, extrato, parcelamentos e decisões dessa conexão, para a sincronização e pede `DELETE /integrations/{id}` à Polp. Informa se a Polp **aceitou** ou se ficou **pendente** | `DELETE /api/polp/integrations/{id}` | Seletor de contas e Privacidade e dados |
| Remover conta manual (com lançamentos e parcelamentos; antes era só ocultada) | `DELETE /api/bank-accounts/{id}` | Seletor de contas |
| Excluir conta (pede a senha de novo) | `POST /api/account/deletion` | Privacidade e dados |
| Cópia dos dados (JSON) | `GET /api/account/export` | Privacidade e dados |
| Preferências de e-mail: lembrete (ligado por padrão) e aviso mensal (desligado; só com opt-in) | `PUT /api/account/notifications` | Privacidade e dados |
| Sair de todos os aparelhos | `POST /api/auth/logout-all` | Privacidade e dados |
| Apagar dados guardados no navegador | (local) | Privacidade e dados |
| Uso sem conectar banco | — | "Agora não, usar sem conectar" na tela de conexão |

### A.5 Segurança

- **Sessão:**
  - JWT em cookie `httpOnly`, `Path=/api`, `Secure` fora de Development e `SameSite` configurável (`Auth:CookieSameSite`, padrão `Lax`).
  - O JWT não aparece mais no corpo de nenhuma resposta.
- **CSRF:** toda requisição que altera dados e se autentica pelo cookie exige `X-CSRF-Token` igual a um segredo dentro do próprio JWT. O frontend recebe esse segredo no corpo de `/auth/login`, `/auth/register` e `/auth/me` e o guarda só em memória (`CsrfProtection.cs`).
- **Validade da sessão a cada requisição** (`SessionValidator.cs`), além da revogação por JTI: usuário existe, está ativo e `tv` = `User.TokenVersion`. Isso faz "sair de todos os aparelhos" e "excluir conta" derrubarem sessões abertas em outros aparelhos (atraso de até 30 s por instância, por causa do cache).
- **Cabeçalhos da API:** `Cache-Control: no-store` e `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'` em `/api`.
- **Sem fallback para sandbox:** a URL da Polp não cai mais em `dev.polp.com.br` quando falta configuração, e há alerta no log de inicialização se a URL de sandbox estiver em uso fora de Development.
- **Rate limit na exclusão de conta.**
- **Sincronização respeita a desconexão:** ela relê a integração dentro do lock e não roda se a conexão foi desconectada. Isso evita que um sync já enfileirado recrie dados apagados.
- **Pedidos de exclusão à Polp que falham são refeitos de hora em hora** (`PendingDisconnectRetryService`). Na exclusão de conta, o pedido sobrevive sem nenhum dado pessoal (`PendingProviderDeletion`, só o id técnico).

### A.6 Transparência e consentimento

- **Registro de aceites** (`ConsentRecord`): tipo, versão do texto e data. Cobre os Termos, a ciência da Política e cada autorização de conexão bancária (instituição, versão do aviso e data). O backend recusa versão diferente da vigente (`LegalDocuments.cs` ↔ `frontend/src/legal/versions.js`).
- **IP e user-agent não foram incluídos no registro de aceite** (minimização, LGPD art. 6º, III). Decisão mantida em 31/10: data e versão do texto já identificam o que foi aceito e quando (ver Parte I).
- **Cadastro:**
  - checkbox (não pré-marcado) só para os Termos;
  - a Política é apresentada como ciência, em texto separado;
  - a conexão bancária é um aceite à parte, depois do cadastro.
- **Tela de conexão (aviso antes de qualquer dado sair):** o que é acessado, para quê, quem participa (Polp e instituição participante), o que é guardado, como a sincronização funciona (não é em tempo real), como desconectar e revogar no banco. Inclui checkbox obrigatório e link para a Política.

### A.7 Arquivos

**Backend novos:**
- `Controllers/AccountController.cs`
- `Models/ConsentRecord.cs`, `Models/PendingProviderDeletion.cs`
- `Services/AccountNumberMask.cs`, `CsrfProtection.cs`, `LegalDocuments.cs`, `PolpDisconnectService.cs`, `PrivacyBackfill.cs`, `SessionCookie.cs`, `SessionValidator.cs`
- `Migrations/20261030120000_PrivacyHardening.cs`, `Migrations/20261030120100_PurgeDisconnectedAccountData.cs`

**Backend alterados:**
- `Program.cs`, `appsettings.json` (níveis de log e `Auth:CookieSameSite`), `.env.example`, `README.md`
- `Controllers/AuthController.cs`, `BankAccountsController.cs`, `PolpController.cs`, `TransactionsController.cs`
- `DTOs/AuthDtos.cs`, `Data/AppDbContext.cs`, `Migrations/AppDbContextModelSnapshot.cs` (editado à mão)
- `Models/User.cs`, `BankAccount.cs`, `PolpIntegration.cs`
- `Services/AccountOwnershipDefault.cs`, `AuthService.cs`, `EmailService.cs`, `NullPolpService.cs`, `PolpService.cs`, `PolpSyncService.cs`, `ScheduledEmailService.cs`, `TokenService.cs`

**Frontend novos:**
- `api/account.js`, `legal/versions.js`
- `pages/dashboard/Privacidade/Privacidade.jsx` e `.css`

**Frontend alterados:**
- `App.jsx`, `api/auth.js`, `api/client.js`, `api/polp.js`
- `context/AuthContext.jsx`, `context/AccountContext.jsx`, `routes/ProtectedRoute.jsx`, `utils/authToken.js`, `services/polpConnection.js`
- `components/dashboard/AccountSwitcher/*`, `components/dashboard/NavUserMenu/*`, `layouts/DashboardLayout/*`
- `pages/cadastro/*`, `pages/login/Login.jsx`, `pages/conectar-banco/*`
- `pages/home/sections/Hero`, `SobreNos`, `Funcionalidades`, `FAQ`, `Planos`; `components/layout/Footer/Footer.jsx`
- `pages/legal/PoliticaDePrivacidade.jsx`, `pages/legal/TermosDeUso.jsx`
- `utils/accountKind.js`, `pages/dashboard/Investimentos/Investimentos.jsx`

**Documentação:** `CLAUDE.md` (a seção de autenticação descrevia um cookie e um CSRF que não existiam no código; agora descreve o que existe).

---

## B. Arquitetura de dados

### B.1 Avaliação: guardar ou consultar a Polp sob demanda

| Alternativa | Privacidade | Consequências reais | Decisão |
|---|---|---|---|
| **Só sob demanda** (nada bancário no banco de dados; consultar a Polp a cada tela) | Menos dado em repouso **do nosso lado** | O dado continua guardado na Polp e na instituição participante, e cada visualização gera um novo fluxo de dados para o terceiro (mais transmissões, não menos). Chamadas da Polp levam de 0,8 a 20 s, então relatórios, busca e categorias ficariam inviáveis. Com a integração desativada (hoje) ou descontinuada (v1 em 28/11/2026), o painel ficaria vazio. Edições do usuário (renomear e recategorizar) e lembretes de vencimento precisam de algo persistido | Rejeitada |
| **Só agregados** (totais por mês e categoria) | Menos detalhe | Elimina extrato, relatórios, busca, edição de categoria, parcelamentos e detecção de transferências, que são o núcleo do produto | Rejeitada |
| **Híbrida minimizada** (escolhida) | Guarda só o que alguma tela usa, cifrado, apagado na desconexão | Mantém as funcionalidades, funciona com o fornecedor fora do ar e reduz fluxos para terceiros | **Adotada** |

O que a arquitetura híbrida deixa de fora:
- número completo de conta e cartão;
- titular da conta (`owner`, que a Polp manda e nunca foi gravado);
- `tax_id` da integração;
- URL de autenticação;
- CPF/CNPJ;
- JSON bruto.

**Pendente (decisão de negócio):** um prazo máximo de histórico importado (ex.: N meses), que reduziria ainda mais o dado guardado. Hoje vale "enquanto a conexão existir", com teto técnico de 50 páginas por conta na importação.

### B.2 Caminho dos dados

```
CADASTRO
Navegador ──(nome, e-mail, senha, CPF/CNPJ, versões aceitas)──► API
  API: bcrypt(senha); HMAC(CPF) + tipo; descarta o número do CPF; grava ConsentRecord
  Banco de dados: Users(nome, e-mail, hash da senha, DocumentHash, DocumentType, preferências)
  Resend (EUA) ◄── só o e-mail + texto genérico de boas-vindas
  Navegador ◄── cookie httpOnly (JWT) + csrfToken (memória) + {id, nome, e-mail, tipoDoc}

CONEXÃO BANCÁRIA (opcional)
Navegador ──(instituição, CPF/CNPJ digitado, versão do aviso)──► API
  API confere HMAC(CPF digitado) == DocumentHash; grava ConsentRecord
  API ──(instituição, CPF/CNPJ)──► Polp ──► instituição participante do Open Finance ──► banco
  Navegador ◄── URL de autenticação (não gravada) → login no ambiente do banco
SINCRONIZAÇÃO (ao abrir o painel ou sob pedido; mínimo de 5 min por conexão)
  Polp ──(contas, cartões, faturas, transações)──► API (em memória)
  Banco de dados ◄── contas (nome do produto, tipo, saldo*, ••••1234*), cartões (limite*, fatura*, dias),
             transações (nome*, valor*, data, status, categoria)        * = cifrado AES-256-GCM
  Descartado em memória: titular, número completo, corpo bruto da resposta

USO
Banco de dados ──► API (decifra em memória) ──► Navegador (exibição; Cache-Control: no-store)
Navegador (localStorage): id técnico do usuário, conta selecionada, metas, reserva, integração pendente

E-MAILS AUTOMÁTICOS
Banco de dados ──► API: conta quantas pendências ──► Resend: e-mail + "N contas vencem em breve"

DESCARTE
Desconectar banco ──► apaga contas/transações/parcelamentos daquela conexão ──► DELETE na Polp
  (falha → status DISCONNECT_PENDING, sem dados, retentativa de hora em hora)
Excluir conta ──► DELETE na Polp para cada conexão (falha → PendingProviderDeletion só com id)
            ──► apaga Installments + Users (cascade: contas, transações, categorias, cartões,
                integrações, aceites) ──► invalida sessões ──► apaga cookie
Backups da hospedagem: saem quando expiram (prazo PENDENTE)
```

---

## C. Propaganda e funcionamento real

| Onde | Promessa anterior | Realidade encontrada | O que foi feito |
|---|---|---|---|
| Hero | "Nunca vemos nem guardamos seus dados" | O produto precisa guardar saldos e extrato para funcionar | **Sistema:** passou a guardar menos (sem CPF, sem número completo, sem dados de contas desconectadas, nomes cifrados). **Texto:** "A senha do seu banco não passa por nós. Guardamos só o necessário para o painel, cifrado, e você pode baixar ou apagar tudo quando quiser", e o sistema cumpre isso |
| Hero | "Conexão criptografada de ponta a ponta" | Não existe ponta a ponta: o servidor decifra os dados para exibir | Afirmação removida. Nenhuma justificativa foi criada para mantê-la |
| FAQ | "Não compartilha seus dados com terceiros" | Há compartilhamento com Polp, instituição participante, Resend e hospedagem | **Sistema:** reduziu o envio à Resend ao endereço de e-mail. **Texto:** nova pergunta lista os fornecedores indispensáveis |
| FAQ, conexão, Termos | "Desconectar quando quiser" e "revogar na plataforma" | Desconectar só escondia a conta, e o sync continuava | **Sistema:** desconexão real implementada. **Texto:** descreve o efeito e o limite (exclusão na Polp é assíncrona; efeito no consentimento do banco não documentado pela Polp) |
| Termos e Política | "Você pode encerrar sua conta" e "dados são removidos" | Não existia exclusão | **Sistema:** exclusão implementada. **Texto:** informa a ressalva de backups |
| Política | CPF "protegido por hash que impede sua recuperação" | O CPF era cifrado de forma recuperável e devolvido ao navegador | **Sistema:** o CPF passou a ser realmente só hash. O texto agora é verdadeiro e explica que o hash é dado pseudonimizado |
| Funcionalidades, Sobre nós, FAQ | "Sincronização em tempo real" | Sync ao abrir o painel, com intervalo mínimo e assíncrono | Texto: "atualizada sempre que você abre o painel", "não em tempo real" |
| Rodapé | "Estrutura regulamentada pelo Banco Central" | O Control Finance não é regulado pelo BCB | Texto: "não é instituição financeira; a conexão usa o Open Finance (regulado pelo BCB) por meio de fornecedor" |
| Conexão | "Conexão via Open Finance, regulamentada pelo Banco Central" | Ambíguo sobre quem é regulado | Reescrito, deixando claro que o Control Finance não é instituição financeira |
| Funcionalidades | "Exportação... para declaração de imposto de renda" | Não existia | Substituído por "Seus dados, seu controle", com a exportação JSON que **passou a existir** |
| Funcionalidades | "Histórico completo desde o primeiro uso" | Teto de 50 páginas por conta | Texto ajustado |
| Sobre nós | "Preço acessível comparado aos concorrentes" e "70% das famílias endividadas" | Sem dado guardado que sustente (CDC art. 36, parágrafo único) | Retirados; marcador PENDENTE no código para recolocar com fonte |
| Planos e FAQ | "Assine agora", "cobrança recorrente e automática" | Não há checkout nem cobrança | Botões viraram "Contratação em breve" (desabilitados); texto diz que nenhuma cobrança é feita hoje |
| Login | "Dados protegidos com criptografia" e "simule investimentos" | Genérico; o simulador é de financiamento | "Valores e extratos guardados de forma cifrada"; "simule financiamentos" |
| E-mails | Rodapé "MI Tech" | Identidade não confirmada | Removido; rodapé explica a ausência de valores e como desligar os avisos |
| Investimentos | "Garantida pelo FGC" e "rende perto do CDI" | Generalizações sem os limites do FGC | Textos sem promessa de rendimento, com remissão ao FGC; aviso de que a classificação é automática e não é recomendação; aviso de que o simulador não inclui IOF, tarifas nem CET |

---

## D. Documentos jurídicos

| Documento | Mudança | Ainda depende de |
|---|---|---|
| Política de Privacidade (`PoliticaDePrivacidade.jsx`, versão 2026-10-30) | Reescrita a partir do comportamento do código: dados por categoria, finalidades, bases legais propostas, fornecedores, transferência internacional, medidas de segurança (sem expressões absolutas), retenção por categoria, direitos e como exercer (no painel e por canal), desconexão × exclusão, cookies e `localStorage`, idade, versões | Razão social, CNPJ, endereço, encarregado ou enquadramento de pequeno porte; hospedagem (provedor e região) e prazo dos backups; instituição participante e subprocessadores efetivos da Polp; mecanismo de transferência internacional; bases legais revisadas em 31/10 (ver Parte I) |
| Termos de Uso (`TermosDeUso.jsx`, versão 2026-10-30) | Reescritos: o que o serviço é e não é (não é IF, não participa do Open Finance, não recomenda investimentos); PF e PJ (eliminada a contradição com "fins pessoais"); conexão bancária com limites reais; ausência de cobrança hoje; responsabilidade sem afastar o CDC; encerramento; marcas de bancos; foro do consumidor | Dados da empresa e do titular da PI; condições comerciais. Idade mínima e foro decididos em 31/10 (ver Parte I) |
| Aviso da conexão bancária (`ConectarBanco.jsx`, versão 2026-10-30) | Novo | Confirmação da Polp sobre a instituição participante |
| Rodapé | Aviso legal corrigido; campos `[PENDENTE]` de razão social, CNPJ e endereço | Dados da empresa |

Nenhum dado empresarial foi inventado. Todo campo desconhecido está marcado como `[PENDENTE]`. O CNPJ da Polp (63.588.124/0001-50) foi tirado do site da própria Polp e está marcado como "segundo o site da própria Polp".

---

## E. Pendências

### E.1 Técnicas (podem ser feitas pela equipe)

1. **Verificar as migrations escritas à mão.** Rodar `dotnet build` e `dotnet ef migrations add VerificacaoSnapshot` em um banco de desenvolvimento: se o snapshot estiver certo, a migration gerada sai vazia e deve ser descartada. As migrations não têm `.Designer.cs`; os atributos `[DbContext]` e `[Migration]` estão na própria classe. Se preferir, gere-as de novo com a ferramenta a partir do modelo atual.
2. **Revisar `PurgeDisconnectedAccountData` antes de produção.** Ela é irreversível: apaga contas com `IsActive = false` e as transações delas.
3. **Migration posterior, depois de o `PrivacyBackfill` ter rodado em produção:**
   - remover `Users.Document` e `Transactions.Name` (coluna antiga);
   - tirar a shadow property `LegacyName` e o `Document` do modelo;
   - tornar `NameEncrypted` obrigatória.

   Antes de remover, confirmar com SQL que as colunas estão vazias.
4. **Domínio do frontend e da API.** Precisam estar no mesmo domínio registrável (ex.: `app.` e `api.` do mesmo domínio) para o cookie `SameSite=Lax` funcionar. A URL de produção encontrada no histórico do git (`*.up.railway.app`) é de outro domínio: nesse cenário seria preciso `Auth__CookieSameSite=None`, e Safari e outros navegadores que bloqueiam cookies de terceiros impediriam o login.
5. **Swagger.** Como o JWT não vai mais no corpo, o fluxo de teste mudou: o cookie funciona se o Swagger estiver na mesma origem da API, mas as requisições que alteram dados precisam do cabeçalho `X-CSRF-Token` obtido em `/auth/me`.
6. **Correção de nome e e-mail pelo próprio usuário:** ainda não existe; hoje é feita pelo canal de suporte.
7. **Conexões sem conta** (integração que não chegou a sincronizar): não aparecem na lista de Privacidade e dados para desconexão. Elas não têm dados bancários, mas a integração fica na Polp até alguém pedir a exclusão.
8. **Campos ainda em texto puro:**
   - `Transaction.Description` (anotação manual ou id remoto);
   - `Installment.Description`;
   - nomes de categorias e de contas.

   Têm baixa sensibilidade, mas podem receber o mesmo conversor.
9. **Monitorar o backfill.** Ele roda a cada inicialização, em lotes de 500. Em bases grandes a primeira inicialização é mais lenta.
10. **Outros pontos:** não há MFA nem recuperação de senha. O rate limit é em memória (não compartilhado entre instâncias).

### E.2 Polp

1. **Contrato e plano de produção, e credenciais** (hoje a integração está desligada por `NullPolpService`).
2. **Migração da API v1 (legada) para a v2.** A Polp anuncia a descontinuação da v1 em 28/11/2026. A v2 usa outro modelo (consentimentos, `DELETE /api/v2/consents/{consent}`). Isso exige novo desenvolvimento e possivelmente novo contrato. Sem isso, a conexão bancária para de funcionar.
3. **Confirmar o efeito de `DELETE /integrations/{id}` sobre o consentimento no banco.** A documentação diz que a exclusão é assíncrona e remove os dados na Polp e o item no agregador, mas não fala do consentimento.
4. **Confirmar qual instituição participante atende o Control Finance** (Celcoin ou Pluggy) **e quais subprocessadores tratam esses dados.** A lista pública inclui a OpenAI (EUA) para categorizar transações.
5. **Prazo de exclusão nos backups da Polp** após a exclusão da integração. O DPA cita 90 dias, mas para o encerramento do contrato.
6. **Aceite do DPA e finalidade declarada** (Termos Polp 8.2). **Prazo de 24 h para notificar incidentes à Polp** (Termos 8.10).
7. **Licença de uso dos logos de bancos** servidos pela Polp.

### E.3 Outros fornecedores

1. **Resend:** cláusulas-padrão da ANPD ou outro fundamento do art. 33 da LGPD; retenção de logs de envio; remetente de produção (hoje o padrão é `onboarding@resend.dev`).
2. **Hospedagem** (Railway, DigitalOcean ou Cloudflare, citados no código e no git, não confirmados): provedor, região, transferência internacional, retenção de logs e backups, controle de acesso. **Apagar os logs históricos que contêm o JSON bruto da Polp.**
3. **Gateway de pagamento:** não existe; escolher antes de cobrar.

### E.4 Empresariais

1. Razão social, CNPJ, endereço físico e relação com "MI Tech".
2. Encarregado ou canal de dados (ou formalização do enquadramento como agente de pequeno porte — Res. CD/ANPD 2/2022).
3. Condições comerciais: preço final, renovação, reajuste, teste grátis, cancelamento, reembolso e arrependimento.
4. Fonte do dado de endividamento; levantamento de preços dos concorrentes, se quiserem voltar a comparar.
5. Política interna de acesso administrativo ao banco de dados e aos logs.
6. Plano de resposta a incidentes, com responsáveis e contatos (ANPD, Polp em 24 h, titulares).
7. Busca de marca no INPI; origem e licença da imagem principal e dos ícones; titularidade do código.

### E.5 Questões jurídicas em aberto (versão de 30/10, reescrita em 31/10)

Na versão de 30/10 esta seção se chamava "Jurídicas (exigem advogado)". Não há advogado: cada item abaixo foi tratado no desenvolvimento em 31/10, e o resultado (decisão, fundamento e incerteza que sobra) está na **Parte I**.

1. Bases legais da Política (conexão bancária; nomes de terceiros no extrato).
2. Retenção dos registros de aceite após a exclusão da conta.
3. Registros de acesso do Marco Civil (art. 15).
4. IP e user-agent como prova de aceite.
5. Idade mínima e foro.
6. Leitura consolidada da Res. Conjunta 1/2020 e da Res. CD/ANPD 15/2024.
7. Uso dos logos de bancos.
8. Coerência final da Política e dos Termos com o sistema.

---

## F. Riscos restantes

1. **Integração bancária com prazo de validade:** a API v1 da Polp tem descontinuação anunciada para 28/11/2026 e o contrato está encerrado. Anunciar a conexão bancária sem contrato e sem a v2 continua sendo risco de oferta não cumprida (CDC art. 30).
2. **Dados antigos em backups e logs:** backups anteriores a esta mudança contêm CPF recuperável, números completos de conta, telefone e nomes de transação em texto puro, e os logs da hospedagem contêm o JSON bruto. Esses dados só somem quando expiram ou são apagados pelo operador. A avaliação de incidente da auditoria (C03) continua pendente.
3. **Código não verificado:** nada foi compilado nem testado. Migrations e snapshot foram feitos à mão. A purga é irreversível.
4. **Cookie entre domínios:** se frontend e API ficarem em domínios diferentes, o login pode falhar em navegadores que bloqueiam cookies de terceiros (E.1.4).
5. **Transferência internacional sem mecanismo formal:** Resend (EUA) recebe e-mails; os subprocessadores da Polp incluem empresas nos EUA (OpenAI, entre outras).
6. **O HMAC do CPF é pseudonimização, não anonimização:** com a chave é possível testar CPFs (o espaço é pequeno). A chave do HMAC é derivada do mesmo segredo `Encryption:Key`, com contexto separado (`EncryptionService.cs`). Ela não é reaproveitada para cifragem, mas o vazamento desse único segredo expõe as duas coisas, então ele precisa ser tratado como segredo crítico. *(31/10: anel de chaves com segredos independentes implementado; ver H.1.)*
7. **Atraso de 30 s por instância** para outras sessões caírem após "sair de todos os aparelhos" ou excluir a conta (cache). *(31/10: escrita e exportação sem cache; leituras com 10 s; ver H.5.)*
8. **Consentimento no banco:** a exclusão na Polp pode não revogar a autorização no banco (não documentado). O texto orienta o usuário a revogar também no app do banco.
9. **Publicidade dependente de fornecedor:** "Sincronização Bancária" continua anunciada em Funcionalidades e Planos, embora hoje esteja desligada (`NullPolpService`). Se o lançamento acontecer sem contrato com a Polp, esses textos precisam sair.
10. **Sem identificação da empresa nem encarregado:** impede a comercialização (Decreto 7.962/2013, art. 2º; LGPD arts. 9º e 41).

---

## G. Situação dos itens da auditoria em 30/10

Registro histórico da etapa 1. Os itens antes classificados como "Dependente de validação jurídica" foram reclassificados em 31/10 (a premissa de revisão por advogado era falsa). A situação atual está na **Parte J**.

| Item | Tema | Situação | Observação |
|---|---|---|---|
| C01 | Publicidade falsa sobre dados | **Parcialmente resolvido** | Sistema reduziu coleta e envio; afirmações falsas removidas. Parte da nova redação depende de fatos pendentes de fornecedores |
| C02 | Desconexão não interrompia a coleta | **Parcialmente resolvido** / Dependente de terceiros | Exclusão local, fim do sync e pedido à Polp implementados (não testados). Efeito no consentimento do banco depende da Polp; sem credenciais, o pedido fica pendente |
| C03 | Logs com dados financeiros | **Parcialmente resolvido** | Código não registra mais nada disso. Logs históricos na hospedagem e avaliação de incidente pendentes |
| C04 | Sem exclusão de conta | **Parcialmente resolvido** | Exclusão e exportação implementadas. Prazo dos backups e retenção de aceites pendentes |
| C05 | Fornecedor não identificado | **Dependente de terceiros** (informação da empresa) | Campos marcados `[PENDENTE]`; "MI Tech" removido dos e-mails |
| C06 | Sem estrutura comercial | **Parcialmente resolvido** / Dependente de terceiros | Promessa de contratação removida (botões desabilitados, texto honesto). Checkout e termos comerciais inexistentes |
| A01 | Política não correspondia ao sistema | **Parcialmente resolvido** | Reescrita fiel ao código, com pendências marcadas |
| A02 | Transferência internacional | **Parcialmente resolvido** / Dependente de terceiros | Envio à Resend reduzido ao e-mail. Mecanismo do art. 33 pendente |
| A03 | Aceite não registrado | **Resolvido** (não testado) | `ConsentRecord` com versão e data. Retenção e uso de IP: questão jurídica em aberto, tratada na Parte I |
| A04 | Conexão sem informação prévia | **Resolvido** (não testado) | Aviso completo, aceite registrado, uso sem conexão possível |
| A05 | Funcionalidades inexistentes anunciadas | **Parcialmente resolvido** | Correção textual (a adequada, porque as funções não existem), mais uma funcionalidade real nova (exportação). Sincronização segue anunciada com a integração desligada (F.9) |
| A06 | Encarregado ou canal | **Dependente de terceiros** (empresa) | Canal de suporte citado; encarregado `[PENDENTE]` |
| A07 | Plano de incidentes | **Não resolvido** | Depende de responsáveis e processo da empresa |
| A08 | Termos incompletos e contraditórios | **Parcialmente resolvido** | Reescritos sem contradição PF/PJ e sem afastar o CDC |
| A09 | Contrato Polp e URL de sandbox | **Dependente de terceiros** | Sem fallback para sandbox e com alerta no log; contrato, v2 e prazo de 28/11/2026 pendentes |
| M01 | Token no `localStorage` | **Resolvido** (não testado) | Cookie `httpOnly` + CSRF + validação de sessão. Depende da configuração de domínio (F.4) |
| M02 | Telefone e CPF excessivos | **Resolvido** (não testado) | Telefone removido; CPF só como hash; documento fora das respostas |
| M03 | Retenção indefinida | **Parcialmente resolvido** | Desconexão, exclusão e purga de contas já desconectadas. Sem política para contas inativas; backups pendentes |
| M04 | Registros de acesso (Marco Civil) | **Não resolvido** | Não implementado na aplicação; decisão e hospedagem pendentes |
| M05 | E-mails | **Resolvido** (não testado) | Sem nomes nem valores; um por usuário; resumo opt-in; filtros de ativo |
| M06 | Menores | **Parcialmente resolvido** | Só texto (18+), sem verificação no cadastro |
| M07 | Dados de terceiros no extrato | **Parcialmente resolvido** | Nome da transação cifrado; base legal pendente |
| M08 | Erros expondo o provedor | **Resolvido** (não testado) | Mensagens genéricas; corpo não vai para log nem exceção |
| M09 | Textos de investimentos | **Parcialmente resolvido** | Correção textual e avisos |
| M10 | `localStorage` sem aviso | **Resolvido** (não testado) | Aviso na Política e no painel; botão para apagar dados locais |
| B01 | Imagens sem procedência | **Não resolvido** | Depende de informação do design |
| B02 | Licença e avisos de terceiros | **Não resolvido** | — |
| B03 | Marca | **Dependente de terceiros** (INPI) | — |
| B04 | Logos de bancos | **Dependente de terceiros** (Polp) | Termos agora dizem que os logos não indicam parceria |
| B05 | CSP e cabeçalhos | **Parcialmente resolvido** | CSP e `no-store` na API; CSP do frontend depende da hospedagem |
| B06 | Remetente de e-mail | **Parcialmente resolvido** | "MI Tech" removido; remetente de produção é configuração pendente |

---

# Etapa 2 — 31/10/2026

Nada foi compilado, executado ou testado. Nenhuma operação foi feita em produção nem em fornecedores. Nenhuma migration irreversível foi criada: a migration nova só adiciona estrutura. Toda exclusão nova de dado é **opt-in por configuração**.

## H. Aprofundamento técnico

### H.1 Gerenciamento de chaves

**Situação anterior (confirmada no código):**
- um único segredo (`Encryption:Key`) gerava, por SHA-256 simples (não é uma função de derivação de chave), a chave AES e a chave do HMAC do CPF;
- o texto cifrado não indicava a chave usada;
- consequências: trocar o segredo tornava todo dado antigo ilegível (rotação impossível na prática), e o vazamento do segredo expunha cifra e hash juntos.

**Implementado (`EncryptionService.cs`, `EncryptionKeyRotation.cs`):**
- **Anel de chaves de cifragem** (`Encryption:Keys:{id}` + `Encryption:ActiveKeyId`). O texto cifrado novo carrega o id (`cfk:{id}:…`), então várias chaves convivem durante a troca.
- **Anel de chaves de busca independente** (`Encryption:LookupKeys:{id}` + `Encryption:ActiveLookupKeyId`), com hash prefixado `cfh:{id}:…`. Os segredos de cifragem e de busca são configurados separadamente, então o vazamento de um não expõe o outro.
- **Derivação por HKDF-SHA256** com rótulo de finalidade e id. Segredos com menos de 32 caracteres são recusados.
- **Compatibilidade:** sem as seções novas, o comportamento é idêntico ao anterior. O formato legado (sem prefixo) continua sendo lido com `Encryption:Key`.
- **Rotação da cifragem:** `Encryption:RotateOnStartup=true` regrava todas as colunas cifradas com a chave ativa, em lotes. É desligada por padrão.
- **Rotação do hash do CPF:** o CPF não é guardado, então não dá para recalcular os hashes em lote. A solução é gradual. Login, cadastro e conexão bancária aceitam hashes de qualquer versão de chave (`ComputeLookupHashCandidates`), e o hash é regravado com a chave ativa quando o usuário digita o documento (login por CPF ou conexão bancária).

**Procedimento de troca de chave (operador):**
1. Gerar segredos aleatórios de 32+ caracteres, diferentes entre si e do `Encryption:Key`.
2. Configurar `Keys`/`ActiveKeyId` e `LookupKeys`/`ActiveLookupKeyId`. A chave antiga continua configurada.
3. Ligar `RotateOnStartup`, subir a aplicação, conferir no log a mensagem "Rotação de chave concluída" e desligar.
4. Só depois, retirar a chave de cifragem antiga. A partir daí, campos cifrados com ela em backups antigos ficam ilegíveis (**descarte criptográfico**; ver H.4).
5. A chave de busca legada só pode sair quando nenhum `DocumentHash` legado restar. Isso se verifica com SQL: hashes sem prefixo `cfh:`. Quem nunca voltar a digitar o CPF mantém o hash antigo; retirar a chave antes disso desativa o login por CPF dessas contas (o login por e-mail continua funcionando).

**Impacto de comprometimento (após configurar o anel):**

| Segredo vazado | O que fica exposto | O que fica protegido |
|---|---|---|
| Chave de cifragem ativa | Campos cifrados por ela (valores, nomes de transações, descrições de parcelamento, dígitos de conta, IP de acesso) **se o atacante também tiver o banco** | Hash do CPF (segredo independente); senhas (bcrypt) |
| Chave de busca | Teste de CPFs contra `DocumentHash` (o espaço de CPFs é pequeno) **se o atacante tiver o banco** | Todos os campos cifrados |
| `Encryption:Key` legado | Tudo que ainda estiver no formato legado | O que já foi rotacionado para o anel novo |
| `JwtSettings:SecretKey` | Emissão de sessões falsas | Dados em repouso. Mitigação: trocar o segredo invalida todas as sessões |

**Armazenamento dos segredos:** hoje são variáveis de ambiente na hospedagem (`.env.example`). Não foi possível confirmar quem tem acesso a elas, se há gerenciador de segredos nem se os segredos aparecem em logs de deploy. Ver J, pendências de hospedagem.

### H.2 Retenção do histórico bancário

**Alternativas avaliadas:**

| Alternativa | Efeito | Decisão |
|---|---|---|
| Prazo fixo no código | Impõe uma escolha de negócio sem configuração; apaga dado de imediato no deploy | Rejeitada |
| Prazo por usuário | Mais complexo; pouco ganho, porque o usuário já pode desconectar ou excluir | Rejeitada por ora |
| Guardar só agregados antigos | Perde extrato e edição de categorias do período; dado derivado continua sendo dado pessoal | Rejeitada |
| **Prazo configurável, aplicado na entrada e por limpeza diária** | Minimiza na origem e não toca dado do usuário | **Implementada** |

**Implementado** (`DataRetentionService.cs`, `PolpSyncService.cs`, `AccountController.cs`, `Privacidade.jsx`):
- `DataRetention:SyncedHistoryMonths` (vazio por padrão = sem prazo, comportamento atual);
- quando configurado, transações importadas mais antigas que o corte (e já resolvidas) deixam de ser importadas e são apagadas uma vez por dia;
- **não afeta** lançamentos manuais, parcelamentos (entidade própria), categorias, contas e transações pendentes;
- o prazo em vigor aparece para o usuário em Privacidade e dados (`GET /api/account/data-policy`).

**Impacto funcional:**
- Relatórios em "Todo o período" passam a cobrir só a janela retida.
- A detecção de recorrência anual (endpoint de recorrências, sem tela no frontend) precisa de mais de 12 meses de histórico.
- **Recomendação técnica: 24 meses**, porque cobre comparações anuais e a detecção de cobranças anuais. A escolha do valor é decisão da empresa (pendência J.4). Não há prazo legal fixo para esse dado; o limite decorre do princípio da necessidade (LGPD art. 6º, III) e do término do tratamento (art. 15, I).

### H.3 Mapa de exclusão

| Onde | Desconectar banco | Excluir conta | Situação após esta etapa |
|---|---|---|---|
| `BankAccounts` da conexão | Apagadas | Apagadas (cascade) | OK |
| `Transactions` dessas contas | Apagadas (explícito; FK é `SetNull`) | Apagadas (cascade por usuário) | OK |
| `Installments` ligados a essas contas | Apagados | Apagados (explícito; sem FK para `Users`) | OK |
| `RecurrenceDecisions` | Apagadas | Apagadas (cascade) | OK; a chave agora é HMAC (H.6) |
| `PolpIntegrations` | Marcada `DISCONNECT_PENDING`, removida quando a Polp aceita | Cascade; pedido à Polp antes | OK |
| `PendingProviderDeletions` | — | Só o id técnico da Polp, até a Polp aceitar | Sem dado pessoal |
| `ConsentRecords` | Mantidos (prova da autorização dada) | Apagados (cascade) | Ver I.2 |
| `Categories` | **Mantidas**, inclusive as criadas a partir das categorias da Polp | Apagadas (cascade) | Ficam por decisão: são a organização do próprio usuário, com nomes genéricos; o usuário pode apagá-las em Categorias |
| `CreditCards` manuais | Não afetados (não vieram do banco) | Apagados (cascade) | OK |
| `Users` | — | Apagado | OK |
| `AccessLogs` (se ligado) | — | **Mantidos** até o fim do prazo de retenção | Intencional: guarda obrigatória ressalvada pelo Marco Civil art. 7º, X. Vazio enquanto `Mode=Off` |
| `RevokedTokens` | — | Mantidos até expirar (≤ 8 h) | Só JTI e expiração, sem dado pessoal |
| `DataProtectionKeys` | — | — | Sem dado pessoal |
| Cache de sessão e revogação (memória) | — | `Invalidate` local; outras instâncias até 10 s para leitura e imediato para escrita (H.5) | OK |
| Rate limit (memória) | — | Expira em 15 min | **Corrigido:** guardava e-mail/CPF digitados em texto puro como chave; agora usa HMAC com chave aleatória do processo |
| Sync em background em andamento | Bloqueado pelo lock + releitura do status | Falha por FK (usuário inexistente), nada é gravado | OK |
| Cache de transações no navegador (15 s) | **Corrigido:** limpo ao desconectar | **Corrigido:** limpo ao sair e excluir | OK |
| `localStorage` | Conta selecionada é zerada | Limpo (metas, reserva, conta, integração pendente, id) | OK |
| Postgres: linhas apagadas | Ficam em páginas mortas até o VACUUM; podem estar no WAL e em backups/PITR | Idem | Depende da hospedagem (H.4) |
| Polp e agregador | Exclusão assíncrona pedida | Idem | Prazo final não documentado (E.2) |
| Resend | Logs de envio antigos | Idem | Retenção não documentada (H.7) |
| E-mails já entregues e arquivo exportado | Na caixa e no aparelho do usuário | Idem | Fora do controle do sistema; informado na tela de exclusão |

**Achado novo:** `Transaction.Description` de transações importadas é o id remoto da Polp, sem dado pessoal. A API ainda aceita descrição livre (até 500 caracteres) em `POST /api/transactions`, embora nenhuma tela envie. Fica registrado em H.6.

### H.4 Backups e logs antigos

**O repositório mostra:** nenhum script ou configuração de backup; logs só pelo console padrão do ASP.NET, que a hospedagem coleta; nenhum sink de arquivo, APM ou serviço de monitoramento. Retenção e acesso a logs e backups dependem inteiramente da hospedagem (Railway, DigitalOcean ou Cloudflare aparecem no código e no git; **qual é usada não foi confirmado**).

**Dados antigos que podem existir fora do banco atual:**

| Onde | O que pode conter | Como some |
|---|---|---|
| Logs da hospedagem (anteriores a 30/10) | JSON bruto da Polp (números de conta completos, saldos, limites, faturas, extratos com nomes de terceiros); e-mail de usuários em avisos de falha de e-mail; corpo de erros da Polp | Retenção do provedor ou exclusão manual pelo operador. **Não executada** |
| Backups do banco (anteriores à etapa 1) | Telefone e nomes de transação em texto puro; CPF e números de conta cifrados com `Encryption:Key`; URL de autenticação; contas desconectadas | Expiração do backup. Os campos **cifrados** podem ficar ilegíveis por descarte criptográfico (H.1, passo 4), mas só depois de rotacionar todo o banco atual para o anel novo. Campos em texto puro não são afetados |
| Backups entre as etapas 1 e 2 | Descrições de parcelamento e decisões de recorrência em texto puro | Idem |
| Resend | E-mails antigos com nome, nomes de transações e valores | Retenção da Resend (não documentada aqui) |

Nenhuma exclusão foi feita em infraestrutura externa. As ações estão na lista de pendências (J.3).

### H.5 Invalidação de sessões

**Problema:** o estado de sessão e as revogações ficam em cache por 30 s por instância. Depois de "sair de todos os aparelhos" ou de excluir a conta, outra instância podia aceitar a sessão antiga por até 30 s, inclusive para escrever ou exportar dados.

| Opção | Avaliação |
|---|---|
| Sem cache (consulta a cada requisição) | Mais seguro, com 1 a 2 SELECTs a mais por requisição. Viável, mas desnecessário para leituras |
| Invalidação distribuída (LISTEN/NOTIFY do Postgres ou Redis) | Imediato, mas exige conexão dedicada; Redis seria um fornecedor novo. Desproporcional agora |
| **Sem cache para escrita e exportação; cache de 10 s para leitura** | **Implementada.** Nenhuma escrita nem exportação é aceita com sessão encerrada, em nenhuma instância. Leituras comuns em outra instância podem passar por até 10 s |

**Arquivos:** `SessionValidator.cs`, `TokenRevocationService.cs`, `Program.cs`. Com uma instância só, a invalidação já era imediata (o cache é limpo localmente). **Quantas instâncias rodam em produção não foi confirmado.** O tempo de vida do JWT (8 h) continua igual.

### H.6 Dados em texto puro

| Campo | Conteúdo real | Sensibilidade | Decisão |
|---|---|---|---|
| `Installment.Description` | Digitado pelo usuário ("Notebook", "Tratamento dentário") | Média; pode revelar saúde | **Cifrado** (`DescriptionEncrypted`), em duas fases: a migration adiciona a coluna e o backfill cifra |
| `RecurrenceDecision.NormalizedName` | **Nome normalizado da transação em texto puro** (achado desta etapa: anulava parte da cifragem de `Transaction.Name`) | Média | **Trocado por HMAC** (`RecurrenceKey`). Linhas antigas continuam reconhecidas; a conversão delas é opt-in (`Privacy:HashLegacyRecurrenceNames`) por ser irreversível |
| `Transaction.Description` | Id remoto da Polp; nenhuma tela grava texto livre | Baixa hoje | Mantido. Risco residual: a API aceita texto livre |
| `Category.Name` | Nomes genéricos, mas o usuário pode criar "Psicólogo", "Igreja" | Média: pode permitir inferir dado sensível (LGPD art. 5º, II) | **Não cifrado nesta etapa.** Há índice único (UserId, Name), busca por igualdade no sync e no cadastro, e ordenação no banco. Desenho proposto: `NameHash` (HMAC) para unicidade e busca + nome cifrado; ordenação em memória. Pendência técnica J.1 |
| `BankAccount.Name` | Nome do produto enviado pelo banco ("Nubank Conta") | Baixa | Mantido |
| `CreditCard.Name` | Apelido digitado ("Roxinho") | Baixa | Mantido |
| `User.Name` | Nome completo | Média | Mantido nesta etapa. Usado só em memória (exibição e detecção de transferência), então pode ser cifrado sem perda. Pendência J.1 |
| `User.Email` | Login e envio de e-mail | Média | Mantido: precisa de busca por igualdade. Cifrar exigiria hash de busca adicional |

### H.7 Transferência internacional: dados que saem

| Destino | Dados enviados hoje | Redução possível | Situação |
|---|---|---|---|
| Resend (EUA) | Endereço de e-mail; texto genérico; data do vencimento mais próximo e quantidade de contas no lembrete | E-mail de boas-vindas é dispensável (decisão de produto); o lembrete poderia omitir a data | Mínimo técnico atingido para os avisos que existem. Mecanismo do art. 33 inexistente (I.9) |
| Polp (Brasil) → subprocessadores | CPF/CNPJ na criação da conexão; recebe os dados bancários | Nenhuma do nosso lado: a Polp exige o documento | A lista pública da Polp inclui OpenAI (EUA) para categorizar transações. **Se isso se aplica ao Control Finance e se pode ser desligado não está documentado** (E.2) |
| Hospedagem | Tudo | — | Região não confirmada |
| Servidor dos logos dos bancos (URL vinda da Polp) | IP e user-agent do navegador do usuário ao carregar a imagem | **Implementado:** `referrerPolicy="no-referrer"` (a página de origem não é enviada). Hospedar os logos localmente eliminaria o envio, mas depende da licença dos logos (I.7) | Host e país não confirmados |
| Fontes, analytics, monitoramento | Nenhum (fonte local, sem scripts de terceiros) | — | OK |

Nenhum mecanismo jurídico ou contrato foi presumido.

### H.8 Registros de acesso (Marco Civil)

**Aplicação (verificado no código):**
- antes desta etapa não gravava registro de acesso;
- o nível de log `Microsoft.AspNetCore` está em `Warning`, então nem os logs de requisição saem pelo console;
- o IP só é usado no rate limit, em memória (agora como HMAC).

**Hospedagem:** plataformas de hospedagem costumam registrar requisições HTTP, mas **não foi possível verificar** se isso acontece, com que conteúdo e por quanto tempo. Nada foi apagado.

**Implementado (desligado):**
- `AccessLogEntry`, `AccessLogService`, tabela `AccessLogs` na migration nova;
- `AccessLog:Mode=Authentication` grava data e hora, IP cifrado e id do usuário a cada login e cadastro;
- `AccessLog:RetentionDays` (padrão 183) apaga automaticamente;
- os registros entram na exportação de dados do titular e sobrevivem à exclusão da conta pelo prazo de guarda;
- a Política descreve o estado real (hoje não grava) e o que acontece quando passar a gravar.

O enquadramento jurídico está na Parte I.3.

### H.9 Arquivos da etapa 2

- **Backend novos:** `Services/EncryptionKeyRotation.cs`, `RecurrenceKey.cs`, `DataRetentionService.cs`, `AccessLogService.cs`; `Models/AccessLogEntry.cs`; `Migrations/20261031120000_InstallmentEncryptionAndAccessLog.cs` (escrita à mão; reversível).
- **Backend alterados:** `EncryptionService.cs`, `RateLimitService.cs`, `SessionValidator.cs`, `TokenRevocationService.cs`, `PrivacyBackfill.cs`, `PolpSyncService.cs`, `RecurrenceDetection.cs`, `AuthService.cs`, `LegalDocuments.cs` (versões 2026-10-31); `Controllers/AuthController.cs`, `AccountController.cs`, `PolpController.cs`, `RecurrencesController.cs`; `Models/ConsentRecord.cs`; `DTOs/AuthDtos.cs` (`ConfirmsAdult`); `Data/AppDbContext.cs`; `Migrations/AppDbContextModelSnapshot.cs` (à mão); `Program.cs`; `appsettings.json`; `.env.example`; `README.md`.
- **Frontend alterados:** `api/transactions.js`, `api/account.js`, `context/AuthContext.jsx`, `context/AccountContext.jsx`, `components/ui/BankLogo/BankLogo.jsx`, `pages/conectar-banco/ConectarBanco.jsx`, `pages/cadastro/Cadastro.jsx` (declaração de maioridade), `pages/dashboard/Privacidade/Privacidade.jsx`, `pages/legal/PoliticaDePrivacidade.jsx`, `pages/legal/TermosDeUso.jsx`, `legal/versions.js`.
- **Documentação:** `AUDITORIA_JURIDICA_CONTROL_FINANCE.md` (correção da premissa), este relatório.

## I. Questões jurídicas: decisão, fundamento e incerteza restante

Sem revisão externa. Cada linha diz o que foi decidido, com base em quê e o que **continua incerto**.

| # | Questão | Fundamento consultado | Decisão e implementação | Incerteza que continua |
|---|---|---|---|---|
| I.1 | Bases legais | LGPD art. 7º, I, V, VI e IX; art. 10, §§ 1º e 2º | Execução de contrato para conta, painel, conexão (a pedido) e lembretes; consentimento para o aviso mensal; legítimo interesse para segurança e para nomes de terceiros no extrato, com necessidade estrita e transparência; exercício regular de direitos para os aceites. Na Política (sem marcador de pendência) | O legítimo interesse exige um **teste de balanceamento documentado**, que ainda não foi escrito (J.5). Os guias orientativos da ANPD sobre hipóteses legais não foram consultados nesta etapa |
| I.2 | Guardar aceites após excluir a conta | LGPD art. 16 (hipóteses de conservação: obrigação legal, pesquisa, transferência, uso anonimizado); art. 8º, § 2º (ônus da prova); CDC art. 27 (prescrição de 5 anos) | **Apagar junto com a conta** (comportamento atual), porque "prova de aceite" não está entre as hipóteses do art. 16 | **Em aberto:** sem o registro, a empresa perde a prova de consentimento numa disputa futura (até 5 anos). Não há conclusão segura sobre conservar com base em "exercício regular de direitos" após um pedido de eliminação |
| I.3 | Registros de acesso (Marco Civil) | Lei 12.965 art. 5º, VIII; art. 15; art. 7º, X; Decreto 8.771 art. 13 | A obrigação atinge pessoa jurídica com atividade organizada, profissional e com fins econômicos. Hoje não há PJ constituída nem operação comercial, então o mecanismo está **pronto e desligado**. **Deve ser ligado** quando houver CNPJ e operação | **Em aberto:** se registrar só login e cadastro basta para "registros de acesso a aplicações" (a definição fala em uso da aplicação a partir de um IP). A opção conservadora seria registrar todas as requisições autenticadas, com mais dado guardado |
| I.4 | IP e user-agent como prova de aceite | LGPD art. 6º, III | Não guardar | Prova mais fraca em disputa; aceito como compromisso de minimização |
| I.5 | Idade mínima | CC arts. 3º, 4º, I, e 104, I; LGPD art. 14; Enunciado CD/ANPD nº 1/2023 (listado na página oficial da ANPD) | 18+, com **autodeclaração obrigatória registrada** no cadastro (`adult_declaration`); sem data de nascimento | Autodeclaração não impede menor que mente. Verificação forte exigiria mais dados (contra a minimização) |
| I.6 | Foro | CDC art. 101, I | Termos citam o direito do consumidor ao foro do próprio domicílio; nenhum foro eleito para PJ (vale a regra geral) | Baixa |
| I.7 | Logos de bancos | Lei 9.279 arts. 129 e 132, IV | Mantidos só para identificar a instituição, com aviso de que não indicam parceria | **Em aberto:** uso comercial identificativo não está claramente coberto pelo art. 132, IV. Depende da licença informada pela Polp |
| I.8 | Encarregado | LGPD art. 41; Res. CD/ANPD 2/2022 arts. 3º, 4º e 11; Res. 18/2024 | Canal de privacidade publicado (suporte@…). Pelo art. 4º, o tratamento atual não preenche critério específico de alto risco | O enquadramento como pequeno porte depende de faturamento e grupo econômico (empresa ainda inexistente). Medida segura: indicar encarregado quando a PJ existir |
| I.9 | Transferência internacional | LGPD art. 33; Res. CD/ANPD 19/2024 e 32/2026 (só a UE reconhecida como adequada) | Redução técnica máxima (H.7) | **Em aberto e bloqueante:** não existe fundamento do art. 33 formalizado para a Resend nem para os subprocessadores da Polp. Depende de contrato com cláusulas-padrão (fornecedores) |
| I.10 | Texto consolidado da RC 1/2020 e da Res. CD/ANPD 15/2024 | Lidos só na publicação original e em fontes secundárias | — | Investigação pendente (J.5): ler o texto consolidado no portal de normativos do BCB e no DOU |

## J. Situação atual (31/10) e pendências

### J.0 Itens da auditoria

Categorias: **Resolvido** (implementado, não testado) · **Parcialmente resolvido** · **Não resolvido** · **Dependente de terceiros** · **Incerteza jurídica em aberto** (a norma não permite conclusão segura; o risco continua).

| Item | Situação em 31/10 | O que mudou nesta etapa |
|---|---|---|
| C01 | Parcialmente resolvido | — |
| C02 | Parcialmente resolvido / Dependente de terceiros | Cache do navegador limpo na desconexão |
| C03 | Parcialmente resolvido | Logs antigos mapeados (H.4); exclusão externa não executada |
| C04 | Parcialmente resolvido | Mapa de exclusão completo (H.3); rate limit sem PII |
| C05 | Dependente de terceiros (empresa) | — |
| C06 | Parcialmente resolvido / Dependente de terceiros | — |
| A01 | Parcialmente resolvido | Bases legais e idade sem marcador de pendência (I.1, I.5); teste de balanceamento pendente |
| A02 | Incerteza jurídica em aberto / Dependente de terceiros | Mapa de envios e no-referrer nos logos (H.7) |
| A03 | Resolvido | Declaração de maioridade registrada; retenção decidida (I.2), com incerteza registrada |
| A04 | Resolvido | — |
| A05 | Parcialmente resolvido | — |
| A06 | Dependente de terceiros (empresa) | Análise do art. 4º da Res. 2/2022 (I.8) |
| A07 | Parcialmente resolvido | Procedimento escrito (Parte K); responsáveis pendentes |
| A08 | Parcialmente resolvido | Idade e foro decididos (I.5, I.6) |
| A09 | Dependente de terceiros | — |
| M01 | Resolvido | Escrita e exportação sem cache (H.5) |
| M02 | Resolvido | — |
| M03 | Parcialmente resolvido | Prazo configurável de histórico (H.2); valor a definir |
| M04 | Incerteza jurídica em aberto | Mecanismo pronto e desligado; regra de quando ligar (I.3) |
| M05 | Resolvido | — |
| M06 | Resolvido | Autodeclaração obrigatória e registrada (I.5) |
| M07 | Parcialmente resolvido | Chave de recorrência em HMAC; teste de balanceamento pendente |
| M08 | Resolvido | — |
| M09 | Parcialmente resolvido | — |
| M10 | Resolvido | — |
| B01 | Não resolvido | — |
| B02 | Não resolvido | — |
| B03 | Dependente de terceiros (INPI) | — |
| B04 | Incerteza jurídica em aberto / Dependente de terceiros | Logos sem Referer (H.7) |
| B05 | Parcialmente resolvido | — |
| B06 | Parcialmente resolvido | — |

### J.1 Pendências técnicas

1. **Verificar a migration `20261031120000_InstallmentEncryptionAndAccessLog`** e o snapshot (mesmo procedimento da E.1.1).
2. **Migration posterior:** remover as colunas legadas `Installments.Description` e `Transactions.Name` (texto puro) e `Users.Document` depois que o backfill rodar em produção (conferir com SQL que estão vazias).
3. **Cifrar `Category.Name`** com `NameHash` para unicidade e busca (H.6).
4. **Cifrar `User.Name`** (H.6).
5. **Decidir se a API continua aceitando descrição livre em `POST /api/transactions`.** Nenhuma tela usa.
6. **Se houver mais de uma instância:** considerar invalidação distribuída (H.5).

### J.2 Pendências da Polp

Mantém as de E.2, mais:
- confirmar se a categorização por IA (OpenAI) se aplica às conexões do Control Finance e se pode ser desligada;
- confirmar a licença e o host dos logos.

### J.3 Pendências de hospedagem e operação

Nenhuma foi executada:
1. identificar o provedor, a região e quantas instâncias rodam;
2. verificar retenção e acesso a logs e backups, e se há logs de requisição HTTP;
3. apagar, pelo operador, os logs antigos com JSON da Polp, depois de decidir sobre a avaliação de incidente;
4. guardar os segredos em gerenciador de segredos, com acesso restrito;
5. configurar o anel de chaves e executar a rotação (H.1);
6. avaliar o descarte criptográfico dos backups antigos.

### J.4 Pendências da empresa

Mantém as de E.4, mais:
- valor de `DataRetention:SyncedHistoryMonths` (recomendação técnica: 24);
- manter ou não o e-mail de boas-vindas;
- ligar `AccessLog:Mode` quando a PJ existir e operar (I.3);
- indicar encarregado;
- nomear os responsáveis do procedimento de incidentes (Parte K).

### J.5 Investigações jurídicas a fazer no desenvolvimento

1. Escrever o teste de balanceamento do legítimo interesse (LGPD art. 10) para segurança e para nomes de terceiros no extrato.
2. Consultar os guias orientativos da ANPD sobre hipóteses legais e sobre o encarregado.
3. Ler os textos consolidados da RC 1/2020 (BCB) e da Res. CD/ANPD 15/2024 (DOU).
4. Reavaliar I.2 e I.3 com o que essas fontes trouxerem.

### J.6 Riscos restantes

Mantém F.1 a F.5 e F.8 a F.10. Atualizações:
- **F.6 mitigado** quando o anel de chaves estiver configurado (até lá, o comportamento é o anterior).
- **F.7 reduzido**: leituras em outra instância até 10 s; escrita e exportação imediatas.

Riscos novos:
- **Rotação de chave mal executada:** retirar uma chave antes da rotação completa torna dado ilegível de forma irreversível. O procedimento de H.1 precisa ser seguido.
- **Hash legado do CPF:** a chave de busca legada não pode ser retirada enquanto houver usuários que nunca mais digitaram o CPF.
- **Retenção opt-in:** enquanto `SyncedHistoryMonths` não for definido, o histórico importado não tem prazo.
- **Registros de acesso desligados:** se a obrigação do art. 15 passar a valer e o mecanismo não for ligado, há descumprimento.

## K. Procedimento de resposta a incidentes (medida organizacional)

Os responsáveis e contatos estão como `[PENDENTE]`, para não serem inventados. Os prazos citados vêm das fontes indicadas.

1. **Detecção e registro:** quem perceber registra o incidente com data e hora em que se teve ciência, o que foi afetado e como foi descoberto. Responsável: `[PENDENTE]`.
2. **Contenção:** revogar as credenciais envolvidas (trocar `JwtSettings:SecretKey` derruba todas as sessões; trocar as credenciais da Polp e da Resend; rotacionar as chaves de cifragem pelo H.1) e bloquear o acesso indevido.
3. **Avaliação de risco ou dano relevante aos titulares** (LGPD art. 48): quais dados, cifrados ou não, quantos titulares, se a chave também vazou. Dados cifrados com chave não comprometida contam a favor na avaliação de gravidade (art. 48, § 3º).
4. **Comunicação à Polp em até 24 h** da ciência (Termos Polp 8.10), se o incidente envolver dados obtidos por ela.
5. **Comunicação à ANPD e aos titulares:** prazo de 3 dias úteis (Res. CD/ANPD 15/2024, conforme fontes secundárias; o texto oficial ainda precisa ser lido, J.5.3), contado em dobro para agente de pequeno porte (Res. 2/2022 art. 14, II, com redação da Res. 15/2024, verificado na página oficial). O conteúdo mínimo está no LGPD art. 48, § 1º, I a VI. Canal da ANPD: `[PENDENTE: confirmar formulário vigente no site da ANPD]`.
6. **Registro do incidente por 5 anos**, inclusive dos não comunicados (Res. 15/2024, conforme fontes secundárias).
7. **Revisão:** causa, correção técnica e atualização deste relatório.
