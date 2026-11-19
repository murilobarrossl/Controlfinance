# Auditoria técnica, financeira, jurídica e de segurança — Control Finance

Data: 18/11/2026 · Branch `polp-v2` · Somente leitura (nenhum arquivo do projeto, banco, migration, build ou teste foi tocado durante a auditoria).
Método: 6 agentes em paralelo (backend, segurança, integridade financeira/Polp, frontend, LGPD, migrations) + análise de custo + revisão cruzada e verificação direta dos itens críticos.

> Os números de linha referem-se ao estado do código em 18/11/2026; alterações posteriores (ex.: Despesas recorrentes) podem deslocá-los.

---

## A. Resumo executivo

O código das alterações pendentes é, em geral, de boa qualidade: autorização e isolamento por `UserId` e CPF/CNPJ estão corretos em todos os endpoints novos (nenhum IDOR encontrado), CSRF, JWT, webhook, criptografia por `ValueConverter`, regra única de agregados (`PolpMovementKind.None`), aritmética em `decimal` e fechamento de caixa imutável estão bem construídos. Os testes, porém, nunca exercitam as migrations (SQLite + `EnsureCreated`).

Os bloqueadores não estão na lógica de negócio, e sim em quatro frentes:

1. **Implantação destrutiva:** a migration `LoanContracts` apaga todas as conexões, contas, extratos e investimentos e revoga todos os consentimentos, e roda sozinha no startup, sem guarda de ambiente.
2. **Segredos expostos:** o repositório no GitHub é **público** e o histórico contém 9 valores reais de `SecretKey`/`Key` e senhas de banco. A configuração local atual não os reutiliza; a de produção não pôde ser verificada.
3. **Base jurídica do Open Finance:** a cadeia instituição → Polp → Control Finance pode não ser admitida pelas regras de parceria da RC 1/2020 com redação recente. Depende do contrato com a Polp, que não foi visto.
4. **Transparência (LGPD):** a Política de Privacidade não descreve os módulos CNPJ (funcionários, fornecedores, folha, fechamentos, impostos), descreve o casamento de pagamentos de forma diferente do código, promete apagar "tudo" na desconexão (o que não acontece) e há pendências formais (controlador, encarregado, transferência internacional).

| Dimensão | Avaliação |
|---|---|
| Segurança de aplicação | Boa. Risco principal é operacional (segredos no histórico público, proxy). |
| Integridade financeira | Boa nas regras centrais; inconsistências pontuais (Impostos com conta oculta, vínculos de encargos que somem, "variação" em investimentos). |
| Desempenho/escala | Adequado para centenas de usuários; gargalos claros para milhares (reprocessamento v6, folha O(N), cota da Polp). |
| Migrations | Coerentes com o modelo; uma destrutiva, IDs com data futura, sem lock multi-instância. |
| LGPD/regulatório | Não pronta para comercialização: textos incompletos e enquadramento Open Finance indefinido. |

## B. Escopo analisado

| Item | Valor |
|---|---|
| Branch local / remota | `polp-v2` / `origin/polp-v2` (última busca 18/11 16:36; sem `fetch` por ser proibido alterar refs) |
| Commits locais não enviados | 0 (local = `origin/polp-v2`); 32 commits à frente de `origin/main` |
| Arquivos rastreados modificados | 115 (+5.454 / −1.845), 2 excluídos (`InvestmentFinancialService.cs`, `Patrimonio.jsx`) |
| Renomeados (staged) | 1 (`ImportedInvestmentDetail.jsx` → `InvestmentDetail.jsx`) |
| Não rastreados | 65, incluindo 20 migrations, 4 controllers (CashClosings, Loans, Payroll, Taxes), 4 modelos, 13 serviços, 4 APIs e 14 telas do frontend |
| Áreas | Sync Polp v2, empréstimos, fechamento de caixa, folha/encargos, impostos, conta padrão, métodos de pagamento, reserva de emergência automática, aviso de privacidade |

> Atenção ao commitar: `Program.cs:45` referencia `ConnectedNpgsqlDatabaseCreator.cs` (não rastreado), assim como as migrations e os modelos novos. Um commit parcial gera um repositório que não compila.

## C. Problemas encontrados

Legenda: **C** Crítico · **A** Alto · **M** Médio · **B** Baixo. "Status": ✔ comprovado no código · ? depende de confirmação.

| ID | Grav. | Área | Problema | Local | Prior. | Status |
|---|---|---|---|---|---|---|
| C1 | C | Banco | Migration `LoanContracts` apaga todos os dados bancários e roda no startup | `Migrations/20261117200000_LoanContracts.cs:12-55`, `Program.cs:311` | P0 | ✔ (dano depende do ambiente) |
| C2 | C | Segurança | Segredos reais no histórico de repositório público | commits `9025189`, `5ac6978`, `200e1ba`, `ceaa9c8`, `c7ddf6e` | P0 | ✔ presença / ? uso em produção |
| C3 | C | Jurídico | Repasse de dados de Open Finance via Polp pode ser vedado pelas regras de parceria | `PolpService.cs:426-431`, Termos §1 | P0 | ? contrato |
| A1 | A | LGPD | Módulos CNPJ ausentes da Política, da tela Privacidade e dos Termos | `PoliticaDePrivacidade.jsx:170-175`, `Privacidade.jsx` | P0 | ✔ |
| A2 | A | LGPD | Política descreve casamento de pagamentos diferente do código | `PoliticaDePrivacidade.jsx:52-58` × `SupplierPaymentMatcher.cs:28-50` | P0 | ✔ |
| A3 | A | LGPD | Papéis controlador/operador sobre dados de funcionários e fornecedores indefinidos; sem DPA | Termos | P0 | ✔ ausência |
| A4 | A | LGPD | Pendências formais: controlador, encarregado, transferência internacional (Resend; OpenAI via Polp) | `RELATORIO_ADEQUACAO_PRIVACIDADE…md` | P0 | ✔ / ? |
| A5 | A | Financeiro/Front | Impostos soma só a conta do escopo salvo, sem seletor nem rótulo | `Impostos.jsx:110`, `DashboardLayout.jsx:21` | P1 | ✔ |
| A6 | A | LGPD | Legítimo interesse (inclui hash de terceiros) sem teste de balanceamento | Política l.225 | P1 | ✔ |
| M1 | M | Privacidade/Financeiro | Desconexão não apaga fechamentos, itens e eventos com valores; textos prometem apagar tudo | `PolpDisconnectService.cs:83-93, 275-305`; `Privacidade.jsx:229` | P1 | ✔ |
| M2 | M | Financeiro | Vínculos de pagamento de encargos apagados em cascata, sem evento | `AppDbContext.cs:335` | P1 | ✔ |
| M3 | M | Escala | `TransactionDetailsVersion` 2→6 relê o histórico completo de todas as conexões | `PolpSyncService.cs:24, 140-152` | P1 | ✔ |
| M4 | M | Banco | IDs de migration com data futura (19–20/11) | `Migrations/20261119*`, `20261120*` | P1 | ✔ |
| M5 | M | Segurança | `ForwardedHeaders` confia em qualquer origem | `Program.cs:354-356` | P1 | ? topologia |
| M6 | M | Financeiro | "Variação do saldo" calculada em Investimentos (viola a decisão) | `InvestmentDetail.jsx:94-102, 303-307` | P1 | ✔ |
| M7 | M | LGPD | Hash de CPF/conta de contraparte gerado também para entradas | `PolpSyncService.cs:584-600, 661, 690` | P1 | ✔ |
| M8 | M | LGPD | Retenção indefinida por padrão | `appsettings.json:16`, `DataRetentionService.cs` | P1 | ✔ |
| M9 | M | LGPD/OF | Nova finalidade (identificação de pagamentos) sobre dados de OF com aviso genérico | `PrivacyUpdateNotice/` | P1 | ? |
| M10 | M | LGPD | Registros de acesso desligados (Marco Civil art. 15) | `appsettings.json:19` | P1 | ? constituição da PJ |
| M11 | M | Custo/OF | Atualização de saldo sob demanda pode esgotar a cota da Polp/OF | `PolpController.cs:450, 585-597` | P1 | ? limites Polp |
| M12 | M | Concorrência | `FOR UPDATE` na linha do usuário conflita com o sync (espera/deadlock) | `DefaultBankAccounts.cs:15` | P2 | ✔ conflito / ? deadlock |
| M13 | M | Desempenho | Status de 1 funcionário recalcula a empresa inteira, O(tx × func) | `EmployeesController.cs:110-139`, `PayeePaymentStatus.cs:224` | P2 | ✔ |
| M14 | M | Desempenho | `/closings/adjusted` lê intervalo de datas sem limite | `CashClosingsController.cs:114-124` | P2 | ✔ |
| M15 | M | Financeiro | Valor pago de encargo não acompanha correção do banco | `PayrollController.cs` (ViewsAsync) | P2 | ✔ / ? Polp |
| M16 | M | Financeiro | Comparação mês/ano: período parcial × período inteiro | `periods.js`, `CashAnalysis.jsx:28-32, 74-83` | P2 | ✔ |
| M17 | M | Financeiro | Lançamentos removidos pelo banco nunca saem (pré-existente) | `PolpSyncService.cs:602-696` | P2 | ? Polp |
| M18 | M | Frontend | Detalhe do mês sem rótulo de conta; lampejo de todas as contas | `CashMonth.jsx:37-65, 120-144` | P2 | ✔ |
| M19 | M | Frontend | Valor pago identificado pelo banco não aparece no detalhe do funcionário | `EmployeeDetail.jsx:184-193` | P2 | ✔ |
| M20 | M | Banco | 18/20 migrations sem `.Designer.cs` (quebra `migrations remove`) | `Migrations/` | P2 | ✔ / ? EF 8.0.4 |
| M21 | M | Banco/LGPD | Migrations apagam trilhas "append-only" e escolhas do usuário | `RemoveSupplierPaymentStatus`, `EmployeeCosts`, `AutomaticEmergencyReserve`, `RemoveEmployeeCostLinks` | P2 | ✔ |
| M22 | M | Banco | `MigrateAsync` sem lock entre instâncias; timeout de 30 s com DELETE em massa | `Program.cs:308-322` | P2 | ? nº instâncias |
| M23 | M | Escala | Inbox serial × volume diário de reconciliação | `PolpInboxWorker.cs` | P2 | ? medição |
| M24 | M | LGPD/OF | Escopo `CREDIT_OPERATIONS` maior que o uso | `PolpService.cs:427-428` | P2 | ? Polp |
| M25 | M | LGPD | Custo de funcionário em texto livre pode receber dado sensível | `EmployeeCosts.jsx:37` | P2 | ✔ |
| B1–B30 | B | Diversos | Ver seção D.4 | — | P2/P3 | — |

## D. Análise detalhada

### D.1 Críticos

**C1 — Migration destrutiva automática**
- *Evidência:* `LoanContracts.cs` executa `DELETE` em `Transactions` (contas Polp), `BankAccounts`, `PolpIntegrations`, `PolpWebhookEvents` e `PolpInvestmentAuthorizations`, e enfileira todos os consentimentos em `PendingProviderDeletions`. O próprio comentário diz "Revisar antes de aplicar em produção". `Program.cs:311` chama `MigrateAsync` em todo startup, sem condição.
- *Consequência:* as cascatas levam também `BankBalanceObservations`, investimentos e caixinhas, `PlanningAllocations` (metas do usuário), `InvestmentRateProfiles` (taxas digitadas), `RecurrenceDecisions` e personalizações de transação. Todos os consentimentos são revogados na Polp, e reconectar consome os limites regulatórios mensais por CPF/instituição.
- *Cenário:* qualquer ambiente com dados reais que suba esta branch.
- *Correção:* antes do deploy, confirmar em quais bancos ela já foi aplicada. Para bancos com usuários reais, reescrever sem DML destrutivo (reimportação controlada) ou condicionar o DELETE a uma flag explícita. Separar a aplicação de migrations do startup (ver M22). Fazer backup.

**C2 — Segredos no histórico de repositório público**
- *Evidência:* `gh repo view` → `PUBLIC`. Há 9 valores distintos de `SecretKey`/`Key` (≥ 20 caracteres) no histórico de `appsettings*.json`, além de senhas de banco em connection strings. Comparação por hash: `appsettings.Local.json` atual não reutiliza nenhum.
- *Consequência:* se produção ou staging usar algum deles, qualquer pessoa forja JWT HS256 para qualquer `sub` (o `tv` é um inteiro pequeno e o CSRF é dispensado com header `Authorization`), ou seja, toma qualquer conta. Com a chave de criptografia, decifra colunas sensíveis de um dump.
- *Correção:* confirmar que nenhum ambiente usa esses valores; se algum usar, rotacionar JWT, senha do banco e `Encryption:Key` (key ring + `EncryptionKeyRotation`, sem remover a chave antiga antes de concluir). Incluir os valores vazados numa lista negra em `ConfigSecrets.Validate`. Avaliar tornar o repositório privado. Reescrever o histórico não substitui a rotação.

**C3 — Enquadramento no Open Finance** (classe b + c)
- *Evidência:* os dados seguem instituição participante → Polp → Control Finance. Na RC 1/2020 consolidada (redação da RC 21/2026), o art. 36 trata repasse de dados de cadastro/transação como parceria; o art. 38, XII e XIII veda substabelecer e repassar a terceiros; o art. 36 §8º, II exige consentimento por entidade parceira; o art. 36 §11 exclui quem atua só como operador (LGPD art. 5º, VII).
- *Consequência:* se a Polp for parceira, o repasse ao Control Finance pode ser vedado; se for operadora, o Control Finance precisa constar nominalmente como parceiro no consentimento.
- *Correção:* obter da Polp/integradora, por escrito, o enquadramento (integradora, parceira, operadora) e o cronograma de adequação. A vigência e a transição da RC 21/2026 só foram vistas em imprensa (lista de parceiras até 01/12/2026): confirmar no BCB.

### D.2 Altos

**A1 — Módulos CNPJ fora dos textos.** Funcionários (nome, CPF, salário, cargo, vínculo, desligamento), chaves Pix de terceiros, agência/conta, documento do titular, fornecedores, custos, encargos, marcações e histórico, fechamentos, flags de imposto e conta padrão são armazenados e exportados (`AccountController.cs:176-257`), mas não aparecem na Política nem na tela Privacidade. *Fundamento:* LGPD art. 6º, VI; art. 9º, I, II e V. *Correção:* completar os textos e incrementar `PRIVACY_VERSION` nos dois arquivos.

**A2 — Casamento de pagamentos descrito errado.** A Política fala em "possível pagamento" por conta e nome idênticos e inclui fornecedores; o código nunca usa nome, só tem o nível "identificado", identifica por conta sozinha e casa apenas funcionários. *Fundamento:* art. 6º, VI; art. 9º. *Correção:* reescrever a seção.

**A3 — Papéis e DPA.** A empresa usuária decide sobre os dados de funcionários e fornecedores (tende a ser controladora; Control Finance, operador — art. 5º, VI/VII; art. 39). Os Termos não têm cláusulas de operador. Para o hash de contrapartes, o Control Finance age como controlador por legítimo interesse. *Correção:* anexo de tratamento de dados nos Termos e registro das operações (art. 37).

**A4 — Pendências formais.** Ainda marcados como `[PENDENTE]`: identificação do controlador, encarregado ou dispensa de agente de pequeno porte (arts. 9º, III–IV e 41; Res. CD/ANPD 2/2022, art. 11), mecanismo de transferência internacional para Resend e para OpenAI via Polp (art. 33; Res. CD/ANPD 19/2024 — prazo de adequação de cláusulas já vencido), hospedagem e backups.

**A5 — Impostos com conta oculta.** `buildScopes` só gera escopos de conta única, então `scopeQuery` sempre leva `bankAccountId`; `/dashboard/impostos` não está em `ACCOUNT_SCOPED_PATHS`, logo não há seletor nem rótulo. Com contas A e B e o escopo B salvo em outra aba, a tela mostra "Nenhum identificado" para DARFs pagos por A. *Correção:* não enviar `bankAccountId` (o backend aceita null) ou mostrar o seletor.

**A6 — Legítimo interesse sem LIA.** A base agora cobre hash de CPF/conta de terceiros, e o teste de balanceamento consta como pendente. *Fundamento:* art. 10, §§2º–3º; art. 37; art. 38 (RIPD). *Correção:* teste por finalidade e avaliação de RIPD.

### D.3 Médios (síntese)

- **M1 + M2 (revisão cruzada: desconexão, retenção e cascatas).** A desconexão (`PolpDisconnectService`) e a purga de retenção apagam `Transactions`. Isso (i) deixa `CashClosings`/`CashClosingItems` com valores e ids de transação, mais `PayeePaymentMarkEvents.BankPaidAmount` e `PayrollObligationEvents.PaymentAmount`, contrariando "apagamos na hora tudo que veio dele" (`Privacidade.jsx:229`, Política l.275 — LGPD arts. 6º, VI, 15 e 16); (ii) apaga em cascata `PayrollObligationPayments` sem gerar evento, e um encargo "Identificado" volta a "não pago" em silêncio (reconectar gera novo id de transação). *Correção:* decidir uma regra única (apagar fechamentos e itens do escopo, gravar evento antes de remoções em massa, FK `Restrict`/`SetNull` com snapshot do valor) e alinhar o texto.
- **M3 (revisão cruzada: escala + integridade + cota).** A versão 6 faz a reconciliação enfileirar o refresh completo do histórico de todas as integrações, fora do bucket; cada uma regrava tudo numa transação longa (agrava M12), disputa o limite global de 240 req/min com webhooks reais e com o saldo sob demanda (M11), e altera `PolpMovementKind` de linhas antigas, o que faz fechamentos já gravados exibirem "ajustes" em massa. Contas acima de `MaxPages = 50` viram `Dead` e nunca recebem a versão (impostos e "pago no mês" vazios no passado). *Correção:* teto por rodada, leitura por janelas com checkpoint e medição antes de liberar.
- **M4.** O EF ordena IDs de migration como texto; uma migration criada até 20/11 recebe ID anterior às já existentes, roda no meio da cadeia em banco novo e pode falhar no startup; `migrations remove` apagaria a errada. *Correção:* renomear para timestamps ≤ agora antes de qualquer aplicação fora do ambiente de desenvolvimento, ajustando `__EFMigrationsHistory` onde já foram aplicadas.
- **M5.** `KnownNetworks/KnownProxies.Clear()`: se a origem for alcançável diretamente, `X-Forwarded-For` forjado burla o rate limit por IP (login, cadastro, exclusão) e grava IP falso no AccessLog; com dois proxies, todos compartilham o IP do edge. *Correção:* restringir aos IPs do proxy e fechar a origem.
- **M6.** `balanceVariation` = último − primeiro ponto já carregado, com cor de ganho/perda; muda ao clicar "Carregar mais" e inclui aportes/resgates. Contraria a decisão de não calcular valorização. *Correção:* remover o bloco. Também há código morto de projeção e taxa pré-preenchida no simulador (B20).
- **M7.** Hash de contraparte gerado para toda transação de CNPJ, inclusive entradas; os textos falam só do "destinatário de cada saída" (art. 6º, III e VI). *Correção:* restringir a débitos ou corrigir o texto.
- **M8.** `SyncedHistoryMonths: null` → extratos, eventos de folha, marcações e funcionários desligados ficam sem prazo (arts. 15–16). *Correção:* definir prazos (o próprio relatório interno sugere 24 meses).
- **M9.** Identificar pagamentos é uma finalidade nova sobre dados de OF já compartilhados (RC 1/2020, art. 10 §§7º–8º; LGPD art. 8º §6º); o aviso atual é um "Entendi" genérico. *Correção:* confirmar a cobertura do consentimento com a integradora e mostrar o resumo da mudança no próprio aviso.
- **M10.** `AccessLog:Mode = Off`; o Marco Civil art. 15 exige guarda por 6 meses ao provedor PJ com fins econômicos. A Política diz "pelo menos 6 meses" (a lei diz "pelo prazo de 6 meses"). *Correção:* ligar quando a PJ operar.
- **M11.** Saldo sob demanda: janela de 1 min por conta, throttle em memória local (multiplica por instância), chamada direta fora da inbox serial. Um CNPJ com 10 contas pode gerar ~14 mil chamadas/dia, consumindo o rate limit global de todos e possíveis limites mensais por consentimento. *Correção:* janela de 15–30 min, limite diário por usuário, throttle persistido e passagem pela inbox.
- **M12.** `SELECT … FOR UPDATE` na linha de `Users` conflita com o `FOR KEY SHARE` dos INSERTs do sync; escritas de folha e conta padrão esperam até 30 s (503), com deadlock possível em `SetDefault`. *Correção:* `FOR NO KEY UPDATE` + `SET LOCAL lock_timeout`.
- **M13.** Cada consulta/PUT de mês de um funcionário carrega e decifra até 5.000 funcionários e calcula HMACs; casamento linear por transação; PUT chama duas vezes. *Correção:* índice invertido hash → funcionário e filtro por funcionário.
- **M14.** `Take(60)` limita a quantidade de fechamentos, mas não o período lido (pode cobrir anos de transações decifradas). *Correção:* ler só os dias fechados ou limitar a janela.
- **M15.** "Pago" de encargo = soma dos vínculos gravados; se a Polp corrigir o valor ou a classificação, nada é revalidado. *Correção:* limitar a soma ao valor atual da transação e sinalizar a divergência.
- **M16.** O CLAUDE.md diz "same span"; o código compara com o período anterior inteiro (−12% falso em novembro). Decidir e alinhar código, rótulo e documentação (o CLAUDE.md também ainda cita "Dia | Mês | Ano").
- **M17.** O upsert não remove lançamentos que o banco deixou de enviar (pré-autorização cancelada continua contando). Depende do comportamento da Polp v2.
- **M18–M19.** Correções diretas: rótulo de conta e guarda de `accountsStatus` em `CashMonth`; exibir `bankPaidAmount` no detalhe do funcionário.
- **M20–M22.** Designers ausentes (todas as migrations são descobertas; risco só para ferramentas); DELETEs de trilhas de auditoria e escolhas com `Down` vazio; `MigrateAsync` concorrente sem `pg_advisory_lock` e com timeout padrão. *Correção:* migrations num passo de deploy separado com lock e timeout maior; arquivar antes de apagar auditoria.
- **M23–M25.** Medir a vazão da inbox; confirmar com a Polp um produto de crédito mais restrito; aviso ou lista fechada no campo de custo de funcionário.

### D.4 Baixos (P2/P3, resumidos)

| ID | Problema | Local |
|---|---|---|
| B1 | Assinatura do webhook desligável fora de Development | `ConfigSecrets.cs:26-27, 43-44` |
| B2 | Sem rate limit por usuário em Connect, export, pix-key | `PolpController:90`, `Employees:227`, `Suppliers:144` |
| B3 | CORS com localhost e `AllowedHosts: "*"` sem validação | `appsettings.json` |
| B4 | `appsettings.Development.json` rastreado com chave/senha de dev | — |
| B5 | Cache de sessão: corrida no `bypassCache` regrava estado antigo (até 10 s de GET); `logout-all` invalida só a instância local; CLAUDE.md diz 30 s, o código usa 10 s | `SessionValidator.cs:17, 27-31` |
| B6 | `DatabaseFailure.IsTransient` ignora `DbUpdateException` (500 em vez de 503, sem backoff) | `DatabaseFailure.cs` |
| B7 | Qualquer `DbUpdateException` vira "registro igual" (409) | `PayrollController.cs:204-208`, `PayeePaymentStatus.cs:172` |
| B8 | `.Single` após verificação → 500 em concorrência; tetos verificados sem lock | `EmployeesController.cs:145, 204-222` |
| B9 | `GET payroll/employees/{id}` grava (seed) | `PayrollController` |
| B10 | Falta índice `LoanContracts(PolpLoanId)`; webhook `loans` relê a lista por contrato | `PolpWebhookProcessor.cs` |
| B11 | `BuildDayAsync` com ~10 consultas duplicadas | `CashClosingsController.cs:136-196` |
| B12 | Reconciliação varre todas as integrações a cada 15 min com subconsultas correlacionadas | `PolpReconciliationService.cs` |
| B13 | Polling de 60 s em 3 telas (≈83 req/s com 5 mil abas) | `AccountContext.jsx:117` etc. |
| B14 | Valor ausente vira 0 e tipo ausente vira débito, sobrescrevendo dado bom | `PolpSyncService.cs:830-853` |
| B15 | `CounterpartyCnpjFromBank` cai em releitura sem documento → imposto some | `PolpSyncService.cs:584-600, 786-794` |
| B16 | Lista vazia transitória apaga contratos de empréstimo | `PolpLoanImportService.cs` |
| B17 | "Hoje" do navegador × `User.TimeZone`; ledger agrupa por dia UTC | `periods.js`, `CashClosingLedger.cs` |
| B18 | Filtros de "saída bancária" diferentes entre Impostos, folha e encargos | `TaxesController`, `PayeePaymentStatus`, `PayrollObligations` |
| B19 | Base do fechamento × agregados (linhas manuais legadas) | `CashClosingLedger`, `TransactionsController:44` |
| B20 | Código morto de projeção; taxa 1,5% pré-preenchida no simulador | `Investimentos/*`, `FinancingSimulator.jsx:15` |
| B21 | Erro vira lista vazia em dias ajustados e contratos; contratos sem `dataRefreshKey` | `CashClosingPanel.jsx:131`, `LoanContracts.jsx:30-40` |
| B22 | `setCsrfToken` aborta leituras de todos → skeleton eterno | `client.js:13-16` |
| B23 | Diálogo de pagamento fecha durante envio; "remover padrão" sem UI; erro duplicado | `PaymentDialogs.jsx:37`, `AccountContext.jsx:160-179` |
| B24 | Lista do mês (só transferências próprias excluídas) × totais (`None`) | `CashMonth.jsx:55` |
| B25 | `ConnectedNpgsqlDatabaseCreator` usa API interna (EF1001) | `Data/` |
| B26 | Cópia de métodos de pagamento descarta Pix sem tipo/conta incompleta | `20261118120000_*` |
| B27 | `now() AT TIME ZONE 'utc'` em `timestamptz`; `Down` de `EmployeeCosts` sempre falha | migrations |
| B28 | Export sem `CashClosingItems`, `RecurrenceDecisions`, `IsDefault/Ownership` (art. 18, II e V) | `AccountController.cs:231-241` |
| B29 | `PRIVACY_VERSION` e migrations datadas no futuro | `versions.js`, `LegalDocuments.cs` |
| B30 | `LogError(ex)` global com stack completo; CET de empréstimo possivelmente arredondado a 2 casas | `Program.cs:385`, `PolpLoanImportService` |

## E. Riscos jurídicos e LGPD

Classes: (a) violação ou inconsistência comprovada · (b) risco jurídico relevante · (c) depende de informação não disponível · (d) recomendação de melhoria.

| Achado | Classe | Fundamento |
|---|---|---|
| C3 enquadramento OF | (b)+(c) | RC 1/2020 (consolidada), arts. 36, 38 (XII, XIII); LGPD art. 5º, VII |
| A1 textos sem módulos CNPJ | (a) | LGPD arts. 6º, VI; 9º, I, II, V |
| A2 casamento descrito errado | (a) | LGPD arts. 6º, VI; 9º |
| A3 papéis/DPA | (b) | LGPD arts. 5º, VI–VII; 37; 39 |
| A4 controlador, encarregado, transferência | (a)/(c) | LGPD arts. 9º, III–IV; 33; 41; Res. CD/ANPD 2/2022 art. 11; Res. CD/ANPD 19/2024 |
| A6 LIA ausente | (b) | LGPD arts. 10, §§2º–3º; 37; 38 |
| M1 desconexão × promessa | (a) | LGPD arts. 6º, VI; 15; 16 |
| M7 hash de entradas | (a)+(b) | LGPD art. 6º, III e VI |
| M8 retenção indefinida | (b) | LGPD arts. 15–16 |
| M9 nova finalidade | (b)+(c) | RC 1/2020 art. 10, §§7º–8º; LGPD art. 8º, §6º |
| M10 registros de acesso | (c) | Marco Civil art. 15 |
| M21 exclusão de trilhas | (d) | LGPD art. 37 (registro das operações) |
| M24 escopo amplo | (b) | LGPD art. 6º, III; RC 1/2020 art. 10, §1º, II e V |
| M25 dado sensível em texto livre | (b) | LGPD arts. 5º, II; 11 |
| B28 export incompleto | (a) | LGPD art. 18, II e V |

**Consistente:** a exclusão de conta apaga todas as tabelas novas (`AccountController.cs:356-377`); o AccessLog sem FK fica só pelo prazo legal (art. 16, I); ciência e consentimento estão separados em `ConsentRecord`; os e-mails não levam nomes nem valores; as colunas novas são cifradas; o app não inicia pagamentos (RC 1/2020 art. 2º, VI–VII); a sincronização bancária não depende de consentimento LGPD como única base (execução de contrato, art. 7º, V, é aplicável ao serviço contratado).

**Fontes oficiais consultadas (abertas):**
- LGPD — https://www.planalto.gov.br/ccivil_03/_ato2015-2018/2018/lei/l13709compilado.htm
- Marco Civil — https://www.planalto.gov.br/ccivil_03/_ato2011-2014/2014/lei/l12965.htm
- LC 105/2001 — https://www.planalto.gov.br/ccivil_03/leis/lcp/lcp105.htm
- RC 1/2020 consolidada — https://normativos.bcb.gov.br/Lists/Normativos/Attachments/51028/Res_Conj_0001_v10_P.pdf
- Res. CD/ANPD 19/2024 e 2/2022 — páginas em https://www.gov.br/anpd

**Não confirmados em fonte oficial:** Res. CD/ANPD 15/2024 (incidentes; página 404), Guia de Legítimo Interesse (PDF não aberto), Res. CD/ANPD 18/2024, vigência e transição da RC 21/2026 (só imprensa), CDC e CLT/eSocial (não consultados). O repositório `ctresb/leis` não foi usado como autoridade.

## F. Desempenho e escalabilidade

Gargalos para milhares de usuários, em ordem de impacto:
1. **Reprocessamento v6 (M3):** horas ou dias de fila na inbox serial após o deploy; webhooks reais atrasados.
2. **Cota global da Polp (240 req/min)** disputada por inbox, saldo sob demanda (M11), Connect sem limite (B2) e webhooks forjáveis se a assinatura for desligada (B1).
3. **Folha O(func × tx) com decifração em massa (M13)** por clique, até 5.000 funcionários.
4. **Locks:** `FOR UPDATE` no usuário × transação longa do sync (M12); `MigrateAsync` concorrente (M22).
5. **Leituras ilimitadas:** `/closings/adjusted` (M14); busca/ordenação em memória por campos cifrados (limitadas por teto, aceitável).
6. **Carga de fundo:** polling de 60 s (B13), reconciliação a cada 15 min sobre todas as integrações (B12), inbox serial (M23).

Bem resolvido: claim da inbox com índice `(Status, Priority, NextAttemptAt)` + `SKIP LOCKED`; índices `UserId + DueDate` cobrem os filtros novos; caches de catálogo, instituições e taxas do BCB; frontend com rotas `lazy`, imagens WebP/AVIF e `recharts` fora do bundle inicial. Custo de infraestrutura não verificável (não há configuração de implantação no repositório).

## G. Integridade financeira

- **Valores incorretos exibidos:** Impostos de conta oculta (A5); "variação" de investimento (M6); comparação de períodos desiguais (M16); encargo pago acima da saída real (M15); lista do mês ≠ totais (B24).
- **Perdas silenciosas:** vínculos de encargos via cascata (M2); flag de imposto que cai em releitura (B15); contratos apagados por lista vazia (B16); valor ausente que zera um valor bom (B14); migração destrutiva (C1).
- **Duplicação:** não encontrada. O upsert por (conta, id Polp) é serializado por `pg_advisory_xact_lock`, a dedupe da inbox funciona e os fechamentos têm índice único parcial. Não há índice único `(BankAccountId, Description)` no banco (pré-existente): a proteção depende do lock.
- **Divergência banco × app:** lançamentos removidos pelo banco permanecem (M17); fronteira de dia UTC × fuso do usuário (B17).
- **Regras preservadas:** agregados com `PolpMovementKind.None` em Dashboard, Relatórios, Receitas e despesas, fechamento, Impostos e folha; empréstimo recebido e pagamento de fatura fora dos totais; `decimal` + `InvariantCulture`; Investimentos sem cálculo de rendimento no backend (`/financials` removido), exceto M6 e o código morto de B20.

## H. Plano de correção

**P0 — antes de qualquer disponibilização**
1. Confirmar os ambientes e bancos de destino; neutralizar ou reescrever `LoanContracts` (C1); fazer backup.
2. Verificar os segredos em uso em todos os ambientes; rotacionar o que coincidir com o histórico; lista negra em `ConfigSecrets`; avaliar tornar o repositório privado (C2).
3. Obter da Polp, por escrito, o enquadramento Open Finance e a cobertura de finalidades (C3, M9, M24).
4. Commitar todos os arquivos não rastreados junto com os modificados (o build depende deles).
5. Reescrever a Política, a tela Privacidade e os Termos: módulos CNPJ, casamento real, desconexão, papéis/DPA, controlador, encarregado, transferência internacional; incrementar `PRIVACY_VERSION` com a data real (A1–A4, B29). *Depende de 3 e das decisões de M1/M7/M8.*

**P1 — antes de produção**
6. Renomear os IDs de migration futuros (M4) — antes de qualquer nova migration e antes de aplicar em staging.
7. Separar `MigrateAsync` do startup, com lock e timeout (M22).
8. Decidir a regra de desconexão/retenção e implementá-la: fechamentos, itens e eventos com valores; cascata de vínculos com evento (M1, M2, M8) → depois atualizar o texto (passo 5).
9. Planejar o rollout da v6: teto por rodada, janelas com checkpoint, medição (M3).
10. Corrigir Impostos (A5) e remover a "variação" de Investimentos (M6).
11. Restringir o hash de contraparte a débitos ou corrigir o texto (M7); escrever a LIA (A6).
12. `ForwardedHeaders` restrito ao proxy (M5); throttle de saldo persistido e mais longo (M11).
13. Ligar o AccessLog quando a PJ operar (M10).

**P2 — melhorias importantes:** M12–M21, M23, M25, B1–B3, B10, B12–B13.

**P3 — futuras:** demais itens B (B4–B9, B11, B14–B30) e alinhamento do CLAUDE.md (10 s, "same span", "Dia | Mês | Ano").

**Ordem recomendada:** 1 → 2 → 4 → 6 → 7 (deploy seguro) · 3 em paralelo → 8 → 11 → 5 (textos por último, refletindo o código final) · 9, 10 e 12 independentes.

## I. Pontos não verificados

| Ponto | Evidência necessária |
|---|---|
| Segredos de produção e desde quando o repositório é público | Variáveis dos ambientes; histórico de visibilidade no GitHub |
| Em quais bancos as 20 migrations já foram aplicadas | `__EFMigrationsHistory` de cada ambiente |
| Topologia de proxy e nº de instâncias | Configuração de deploy (inexistente no repositório) |
| Contrato Polp: papel, preço por chamada, limites, finalidades | Contrato e documentação comercial |
| Comportamento da Polp v2 (remoção, correção de valor, campos omitidos, tamanho de página) | Documentação da API ou testes em sandbox |
| Deadlock exato em M12 | Ordem de comandos do `SaveChanges` / logs do Postgres |
| Comportamento de `migrations remove` no EF 8.0.4 | Teste em cópia do repositório |
| Desempenho real | `EXPLAIN`, `pg_stat_statements`, métricas de produção |
| Dependências vulneráveis | `npm audit` / `dotnet list package --vulnerable` (não executados por restrição) |
| Execução real de migrations no Postgres | Nenhum teste cobre; exige ambiente descartável |
| Renderização, contraste, 400 px | Inspeção visual (vedada nesta etapa) |
| Normas da ANPD e RC 21/2026 listadas em E | Abrir fontes oficiais |
| Estado do remoto após 18/11 16:36 | `git fetch` (não executado) |

## J. Conclusão

**Há bloqueadores para produção.** Tecnicamente, a branch pode ir ao Git (commitada por inteiro), mas **não** pode ser implantada em ambiente com dados reais até resolver:

1. **C1** — a migration `LoanContracts` apagaria todos os dados bancários e revogaria todos os consentimentos no primeiro startup.
2. **C2** — segredos reais em histórico público, enquanto não houver confirmação de que nenhum ambiente os usa.
3. **C3 / A1–A4** — o enquadramento Open Finance e a Política de Privacidade não refletem o produto atual (módulos CNPJ, casamento de pagamentos, desconexão, papéis, encarregado, transferência internacional).

Em seguida, antes de abrir para milhares de usuários: IDs de migration futuros e migrations fora do startup (M4, M22), rollout controlado da v6 (M3), regra única de desconexão/retenção (M1, M2, M8) e as duas exibições financeiras incorretas (A5, M6). Não foi encontrada nenhuma falha de autorização, IDOR, injeção ou quebra de isolamento CPF/CNPJ nas alterações pendentes.
