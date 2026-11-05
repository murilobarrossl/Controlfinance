# Relatório de auditoria: migração Polp v1 → v2

Data: 2026-11-04 · Branch `polp-v2` (HEAD `d3a9971`) · Referência v1: `81d7091` (pai de `bf1d938`)

Rodada só de análise, sem nenhuma alteração no código. Cinco auditores trabalharam em paralelo, cada um numa frente:

1. **v1-vs-v2**: histórico do git.
2. **polp-docs**: documentação oficial e cerca de 130 GETs ao sandbox.
3. **contrato-api**: chamadas do frontend contra o backend.
4. **endpoints**: endpoints do backend.
5. **fluxos**: fluxos de ponta a ponta.

Os achados duplicados foram unificados. Os de maior gravidade foram conferidos de novo no código antes de entrar aqui.

**Como ler o status de cada achado**
- **CONFIRMADO**: verificado por leitura de código, e, quando indicado, também por resposta real do sandbox ou pela documentação. Nada foi executado no app. Nenhum achado foi reproduzido em runtime.
- **NÃO CONFIRMADO**: depende de dado, payload ou ambiente que não deu para observar. O motivo está indicado em cada caso.

Os caminhos citados são relativos a `backend/` (B) ou `frontend/src/` (F).

---

## 1. Resumo executivo

O contrato entre frontend e backend está íntegro: nenhuma chamada aponta para rota inexistente e nenhum campo lido deixou de chegar. A autenticação também: nenhum endpoint deixa um usuário acessar dados de outro (IDOR). Os problemas graves estão em três lugares:

1. **Números errados no dinheiro**, confirmados com dados reais do sandbox:
   - O pagamento de fatura do cartão entra como despesa, na categoria "Empréstimos", e duplica as compras do cartão.
   - A "fatura atual" exibe uma fatura já paga e vencida.
   - Transferências entre contas próprias perderam o sinal que as excluía dos totais.
2. **Dados que somem em silêncio**:
   - O backfill marca a conexão como sincronizada mesmo quando a Polp falhou numa conta.
   - O webhook responde 200 antes de processar e não tem fila nem reconciliação.
   - O app exige uma assinatura de webhook que a Polp não documenta. Se a Polp não a enviar, nenhum webhook passa e nada é atualizado depois da conexão.
3. **Funcionalidades perdidas**:
   - A tela de Investimentos deixa de reconhecer caixinhas e investimentos.
   - Não há mais "sincronizar agora".
   - Um consentimento expirado ou revogado não é avisado ao usuário.
   - O texto do aviso de consentimento ainda descreve o funcionamento da v1.

Contagem (sem duplicatas):

| Seção | Crítico | Alto | Médio | Baixo |
|---|---|---|---|---|
| Geral (seção 3) | 2 | 8 | 15 | 9 |
| Arquivos a reescrever (seção 4) | 3 | 3 | 5 | 4 |

---

## 2. Comparação v1 × v2

| Funcionalidade | Situação | Evidência | Impacto para o usuário |
|---|---|---|---|
| Listar instituições | Alterada (melhor) | v1 `PolpController.cs:21-46` @81d7091; v2 `B/Controllers/PolpController.cs:27-60` (cache de 10 min, cursor, `type` PF/PJ) | Ganhou busca por nome. Não filtra por `type` nem por `status` (ver G-A4). |
| Conectar banco | Alterada | v2 `PolpController.cs:89-182` (consentimento Guid, `avoidDuplicates`, `cliente_user_id`) | Todo usuário da v1 precisa reautorizar no banco. |
| Validação de documento e aceite do aviso | Mantida | `PolpController.cs:98-114` | Nenhum. |
| PF/PJ | Mantida, mas incompleta | `B/Services/PolpService.cs:305-311` | A conexão PJ não segue a regra da v2: CNPJ exige o CPF do operador (G-A4). |
| Contas | Alterada | v1 nome = `MarketingName ?? Name`; v2 `B/Services/PolpSyncService.cs:120` nome fixo "{Banco} · {tipo}" | Perde o nome do produto. Contas do mesmo tipo no mesmo banco ficam com o mesmo nome. Quebra a tela de Investimentos (G-C2). |
| Saldos | Alterada | v1 atualizava a cada sync manual e ao abrir o dashboard; v2 só por webhook `accounts` ou `POST /polp/accounts/{id}/balance-sync` (`PolpController.cs:317-357`) | O saldo só muda quando a Polp avisa ou quando o usuário clica em "Atualizar saldo". |
| "Sincronizar agora" (transações, faturas, limites) | **Perdida** | v1 `POST integrations/{id}/sync` (`PolpController.cs:255-283` @81d7091); ausente na v2 (`F/api/polp.js`) | O usuário não consegue forçar uma nova leitura. Se um webhook se perder, o dado fica velho sem recurso (R-A2). |
| Sync ao abrir o dashboard | **Perdida** (deliberada) | v1 `POST integrations/sync-all`, chamado em `DashboardHome.jsx` @81d7091 | Mesmo impacto da linha anterior. |
| Webhook | Nova | `B/Controllers/PolpWebhookController.cs` | Depende da assinatura, que a Polp não documenta (R-C3). |
| Cartões | Alterada | v2 `PolpSyncService.cs:138-170`. Fechamento e vencimento vêm das faturas. O usado não tem mais o fallback limite − disponível. | `ClosingDay`/`DueDay` ficam nulos até chegar uma fatura. O usado pode ficar nulo (G-M12). |
| Faturas | Alterada (com regressão) | `PolpSyncService.cs:179-213` | A "fatura atual" pode ser uma fatura já paga e vencida (R-C2). |
| Transações de conta | Alterada | `PolpSyncService.cs:219-321` (cursor, upsert por id, `counterparty`, `category_ref`) | Transação de conta nunca é "pendente". O pagamento de fatura vira despesa "Empréstimos" (G-C1). |
| Transações de cartão | Alterada | `PolpSyncService.cs:338-347` | `PAGAMENTO_FATURA` é ignorado (R-A1). Parcelas futuras ficam na data da compra (R-M4). |
| Parcelamentos | Mantida (ausência) | Nem a v1 nem a v2 importam parcelamentos. `charge_number` é ignorado. | Só existem parcelamentos manuais. |
| Categorias | Alterada | `B/Services/PolpCategoryMap.cs` (16 grupos) | Perde granularidade. Pagamento de fatura e transferências entre contas próprias ficam mal classificados (G-C1, G-A6). Os 145 `category_ref` da documentação estão mapeados. |
| Investimentos | **Perdida (parcial)** | v2 pede só `ACCOUNT` e `CREDIT_CARD_ACCOUNT` (`PolpService.cs:256`). A tela classifica pelo nome. | Só a poupança volta. Caixinha, cofrinho e investimentos somem (G-C2). |
| Empréstimos e financiamentos | Mantida (ausência) | A v1 não tinha. A v2 ignora (`PolpWebhookProcessor.cs:65`). | Nenhum. |
| Desconexão e revogação | Mantida (adaptada) | `B/Services/PolpDisconnectService.cs` + retry de hora em hora | Lacunas de concorrência (G-A2). |
| Renovação ou expiração de consentimento | **Lacuna mantida e agravada** | `EXPIRED`→`outdated` (`PolpController.cs:290-299`). Não é exposto em `GET /polp/integrations` e o frontend não trata. `recreate` nunca é chamado. | Consentimento revogado ou expirado: os números param sem aviso. Ao reconectar, o histórico pode duplicar (G-A1). |
| Status e erro da conexão | Alterada | `error` só aparece durante `AWAITING_AUTHORIZATION` (`PolpController.cs:258-287`) | Erro posterior à conexão é invisível. |
| `execution_status` / `PARTIAL_SUCCESS` | Alterada | Persistido. `enrichmentPending` é devolvido, mas o frontend não usa. | Aviso de "categorias em refinamento" nunca aparece. |
| E-mails ligados ao sync | Mantida (ausência) | `ScheduledEmailService` não usa a Polp | Nenhum. |
| Rotas `/polp/integrations/{id}/sync` e `/sync-all` | Removidas | Não existem mais | O frontend atual não as usa. |
| Dados da v1 no banco | **Apagados** (irreversível) | `B/Migrations/20261031153419_PolpV2Consents.cs:24-55`, `Down` vazio | Apaga contas e transações da Polp v1. Junto apaga decisões de recorrência, parcelamentos manuais lançados sobre cartões Polp, transações manuais lançadas nessas contas, e nomes e categorias editados. Contas manuais, cartões manuais, categorias e aceites ficam. O comentário diz "só havia conexões de teste". NÃO CONFIRMADO, sem acesso ao banco. |

**A tela de Investimentos**
- Ela nunca teve endpoint próprio, nem na v1 nem na v2.
- Ela usa `accounts` de `GET /polp/integrations` e classifica cada conta por tipo e nome (`F/utils/accountKind.js`, `F/pages/dashboard/Investimentos/Investimentos.jsx:412-413`).
- Metas e reserva de emergência ficam no `localStorage`. Taxas e simuladores são estáticos.
- O que ela ainda recebe de dados reais: só saldos de contas que o filtro reconhece. Na prática, a poupança. Ver G-C2.

---

## 3. Achados por gravidade (exceto os arquivos a reescrever)

### Crítico

**G-C1. Pagamento de fatura do cartão conta como despesa na conta corrente, na categoria "Empréstimos" (dupla contagem)** — ✅ CORRIGIDO (lado da conta corrente; lado do cartão pendente em R-A1). Ver seção 7, item 1.
- Evidência:
  - Sandbox: 23 transações de conta com `PAGAMENTO FATURA CARTAO`, `credit_debit_type=DEBITO` e `category_ref=LOAN_PAYMENTS_CREDIT_CARD_PAYMENT`.
  - `B/Services/PolpCategoryMap.cs:15` mapeia `("LOAN_", "Empréstimos")`.
  - `PolpSyncService.cs:328-336` importa a transação como Expense sem filtro.
  - As compras do cartão já entram como despesa pelas transações do cartão.
- Impacto: as despesas do mês dobram para quem paga fatura pela conta conectada. A categoria "Empréstimos" aparece inflada.
- Correção: tratar `LOAN_PAYMENTS_CREDIT_CARD_PAYMENT` como pagamento de fatura, numa categoria própria fora dos totais (como as transferências próprias). O lado do cartão está em R-A1.
- Status: CONFIRMADO (resposta real e código).

**G-C2. Tela de Investimentos deixa de mostrar caixinhas e investimentos**
- Evidência:
  - `PolpService.cs:256` só solicita `ACCOUNT` e `CREDIT_CARD_ACCOUNT`.
  - O nome da conta é gerado ("{Banco} · Conta corrente", `PolpSyncService.cs:120`), então as regexes de `F/utils/accountKind.js` (`caixinha`, `cofrinho`, `cdb|lci|…|invest`) nunca batem.
  - A migration apagou as contas da v1.
- Impacto:
  - Quem via caixinha ou investimento passa a ver "Nenhuma conta de reserva ou investimento foi reconhecida" (`Investimentos.jsx:129-132`).
  - Só a poupança (`CONTA_POUPANCA`) volta.
  - Efeito oposto: uma conta corrente de banco com "Investimentos" no nome (ex.: "XP Investimentos") seria contada como investimento.
- Correção: classificar só por `PolpAccountType` e subtipo, sem o nome. Decidir se o produto de investimentos da Polp entra no escopo. Se não entrar, dizer isso na tela.
- Status: CONFIRMADO (código). A existência de instituição com "invest" no nome é NÃO CONFIRMADO.

### Alto

**G-A1. Consentimento expirado ou rejeitado: sem aviso, sem `recreate`, e reconectar duplica o histórico**
- Evidência:
  - `GET /polp/integrations` não expõe o status (`PolpController.cs:214-243`).
  - `F/services/polpConnection.js:34-41` só trata `active` e `login_error`.
  - Nenhuma chamada a `POST /consents/{id}/recreate` no repositório.
  - A documentação (`consents/recreate`) diz: "Não use POST /consents para reconectar… criaria outro registro". Cada nova conexão também consome o limite mensal regulatório.
  - `Connect` só bloqueia AUTHORISED ou AWAITING recente (`PolpController.cs:116-125`). Um EXPIRED vira nova linha local, com novas contas.
- Impacto:
  - Dashboard, saldos e faturas continuam mostrando números velhos como atuais.
  - Ao reconectar, o histórico duplica e a cota regulatória é consumida.
- Correção: expor `status` e `lastSyncedAt` por conexão, mostrar faixa "Reautorize", e reconectar via `recreate` mantendo a linha local.
- Status: CONFIRMADO (código e documentação). As transições reais são NÃO CONFIRMADO: no sandbox o consentimento é sempre AUTHORISED. A documentação diz que o consentimento não expira sozinho, só por revogação.

**G-A2. Desconexão e exclusão de conta deixam estado inconsistente**
- (a) Desconexão:
  - Evidência: `B/Services/PolpDisconnectService.cs:31-37` marca `DISCONNECT_PENDING` só depois de pegar o lock do sync (`PolpSyncService.cs:32`). O `HttpClient` não tem timeout configurado (`B/Program.cs:167`, padrão de 100 s).
  - Impacto: o `DELETE` fica preso atrás de um backfill longo. Se o proxy cortar a requisição, nada foi marcado.
- (b) Exclusão de conta:
  - Evidência: `B/Controllers/AccountController.cs:179` revoga na Polp antes da transação local (`:181-189`). Uma `InvalidOperationException` de `ReadResourceAsync` (`PolpService.cs:~407`) não está em `PolpFailure.cs:7-9` e vira 500 no meio do loop.
  - Impacto: consentimentos revogados com a conta local ainda existente, ou a fila `PendingProviderDeletion` perdida.
- Correção:
  - Marcar `DISCONNECT_PENDING` antes do lock.
  - Na exclusão de conta, apagar localmente e enfileirar todas as revogações na mesma transação. O `PendingDisconnectRetryService` faz a revogação depois.
  - Incluir `InvalidOperationException` em `PolpFailure`.
- Status: CONFIRMADO (código).

**G-A3. Cliente HTTP da Polp sem timeout, sem retry e sem `Retry-After`, num limite global compartilhado** — ✅ CORRIGIDO em parte (timeout + retry em GET). Mapeamento 422/429 na borda e limitador global continuam abertos. Ver seção 7, item 4.
- Evidência:
  - `Program.cs:167` registra o cliente sem configurar nada.
  - `PolpService.cs:413-419` (`EnsureSuccess`) transforma qualquer status em `HttpRequestException`.
  - Limite documentado (`rate-limits`): 10 req/s e 240 req/min por `x-api-client`, compartilhado por todos os usuários. O cabeçalho real `x-ratelimit-limit: 10` confirma.
  - Os códigos 402 (plano inativo), 422 e 429 viram um 502 genérico.
- Impacto: com algumas dezenas de usuários conectando ao mesmo tempo, aparecem 429. Somado a R-C1, isso vira histórico perdido. Requisições ficam presas por até 100 s.
- Correção: timeout curto (10–15 s), retry com backoff respeitando `Retry-After`, uma fila ou limitador global, e mapear 422 → 400 e 429 → 429 na borda.
- Status: CONFIRMADO (código e documentação).

**G-A4. Conexão PJ fora da regra da v2; instituições indisponíveis ou incompatíveis aparecem no seletor**
- Evidência:
  - `PolpService.cs:305-311` envia `cnpj` sozinho.
  - A documentação (`consents/create`) diz que BUSINESS exige CPF do operador e CNPJ, e que PERSONAL proíbe CNPJ.
  - O app só aceita o documento do cadastro (`PolpController.cs:107-109`).
  - No sandbox: 22 instituições BUSINESS, 78 BOTH, 110 PERSONAL, 17 em `MAJOR_OUTAGE` e 1 `DEGRADED`, todas listadas sem filtro (`F/pages/conectar-banco/ConectarBanco.jsx`, `PolpController.cs:46-56`; o frontend ignora `status`).
- Impacto: um usuário PJ não consegue conectar e recebe "Não foi possível iniciar a conexão". Um usuário PF tenta banco fora do ar.
- Correção: filtrar por `type` compatível com o documento e por `OPERATIONAL`. Pedir o CPF do operador em PJ.
- Status: regra CONFIRMADA (documentação e código). A resposta 422 da Polp é NÃO CONFIRMADO, porque exigiria POST.

**G-A5. Texto do aviso de consentimento descreve o funcionamento da v1** — ✅ CORRIGIDO. Ver seção 7, item 3.
- Evidência: `F/pages/conectar-banco/ConectarBanco.jsx:253-254` diz "Os dados são atualizados quando você abre o painel ou pede uma sincronização (no máximo a cada 5 minutos por banco)". Nenhuma das duas coisas existe na v2.
- Impacto: o usuário aceita um texto versionado (`BankConnectionNoticeVersion` `2026-10-30`) que promete outro comportamento. É ponto de LGPD (informação clara) e gera confusão quando o dado não atualiza.
- Correção: reescrever o trecho (atualização pela agenda da Polp e botão "Atualizar saldo") e subir a versão em `F/legal/versions.js` e `B/Services/LegalDocuments.cs`.
- Status: CONFIRMADO.

**G-A6. Transferência entre contas próprias perdeu o sinal que a excluía dos totais** — ✅ CORRIGIDO. Ver seção 7, item 2.
- Evidência:
  - `B/Services/TransferDetection.cs` (~26-31) espera a categoria "Transferência mesma titularidade".
  - `PolpCategoryMap.cs:14` mapeia todo `TRANSFER_` para "Transferências", então essa categoria nunca é criada.
  - No sandbox, as 24 de 24 transações "TRANSFERENCIA MESMA TITULARIDADE" vêm com `TRANSFER_IN_ACCOUNT_TRANSFER`/`TRANSFER_OUT_ACCOUNT_TRANSFER`.
  - Sobra só a heurística por nome do titular, que não funciona em PJ (titular pessoa ≠ empresa) e pode excluir um homônimo.
- Impacto: as transferências entre contas próprias contam como receita e como despesa, inflando os dois totais.
- Correção: tratar `TRANSFER_*_ACCOUNT_TRANSFER` como transferência própria. Opcionalmente, também a contraparte com `tax_id` igual ao documento do titular (por hash).
- Status: CONFIRMADO (código e dado real).

**G-A7. Tela de conexão presa em "sincronizando"; falha do backfill nunca é mostrada**
- Evidência:
  - `PolpController.cs:264` só consulta a Polp enquanto o status é AWAITING.
  - `:294` mapeia `!SyncedLocally` ou `AWAITING_RESOURCES` para "syncing".
  - `QueueBackgroundSync` (`:391-423`) desiste após 5 tentativas ou na primeira exceção, só com log.
  - `F/services/polpConnection.js` não trata `outdated` e `unknown`, e depois de 5 min mostra "A conexão demorou" (`ConectarBanco.jsx:156`).
  - O Dashboard não tem estado de "importando" (`B/Controllers/DashboardController.cs:23-24`). Mostra a conta com R$ 0 enquanto as transações não chegam.
- Impacto: o usuário acha que falhou, tenta de novo e cai no 409 (G-M1). Uma falha real fica indistinguível de lentidão.
- Correção: estados explícitos (`syncing`, `sync_failed`, `ready`) e tratar `outdated`/`unknown` como estado final. Na tela, mostrar "importando extrato".
- Status: CONFIRMADO.

**G-A8. Produção pode subir apontando para o sandbox** — ✅ CORRIGIDO. Ver seção 7, item 5.
- Evidência: `B/appsettings.json:50` usa `BaseUrl` `.../api/v2/sandbox`. `Program.cs:173-174` só imprime um aviso. `ConfigSecrets.Validate` não confere `Polp:BaseUrl`.
- Impacto: se a variável não for sobrescrita, os usuários "conectam" bancos e veem contas e valores fabricados.
- Correção: fora de Development, recusar a inicialização se `BaseUrl` contiver `/sandbox`.
- Status: CONFIRMADO (código). O valor configurado em produção é NÃO CONFIRMADO.

### Médio

**G-M1. 409 na conexão sem retomada; login manda para "Conectar banco" quem já conectou**
- Evidência:
  - `PolpController.cs:75-87,116-125` devolve um 409 com `integrationId`, mas `F/api/client.js:63-65` só usa `message`.
  - `GetIntegrationStatus` renova `UpdatedAt` a cada consulta (`:282`), estendendo o bloqueio de 1 h.
  - `F/pages/login/Login.jsx:69-80` redireciona quando a lista de contas está vazia, o que é o caso durante o backfill.
- Impacto: o usuário fica sem saída por até 1 h depois de fechar a aba da autenticação.
- Correção: no 409, retomar com `integrationId` e reemitir a URL de autenticação via `recreate` se ela tiver expirado (ver G-M9).
- Status: CONFIRMADO.

**G-M2. `PUT /transactions/{id}` quebra a chave de upsert da Polp e duplica lançamentos**
- Evidência: `B/Controllers/TransactionsController.cs:427` (`Description = dto.Description`) e `:434` (`BankAccountId`). A chave do sync é `(BankAccountId, Description)` (`PolpSyncService.cs:243`).
- Impacto: editar ou mover uma transação sincronizada faz o próximo webhook recriá-la. Fica em dobro no saldo e nos totais. Nenhuma tela chama esse PUT hoje; é risco da API.
- Correção: rejeitar alteração de campos vindos da Polp, ou criar a coluna `PolpTransactionId` e não usar `Description` como chave.
- Status: CONFIRMADO.

**G-M3. Editar ou apagar transação sincronizada é desfeito pelo sync**
- Evidência: `PolpSyncService.cs:284-288` sobrescreve valor, tipo, status e data. `DELETE /transactions/{id}` (`TransactionsController.cs:486`) não distingue a origem, e o sync recria a transação.
- Impacto: correção do usuário volta de forma intermitente.
- Correção: bloquear com 409, ou marcar como "ignorada" ou "editada".
- Status: CONFIRMADO (sem UI hoje).

**G-M4. `PUT /bank-accounts/{id}` edita saldo e `BankCode` de conta sincronizada**
- Evidência: `B/Controllers/BankAccountsController.cs:69-71`.
- Impacto: o saldo digitado é sobrescrito no próximo webhook. `BankCode` alterado quebra marca e agrupamento.
- Status: CONFIRMADO (sem UI hoje).

**G-M5. Corridas no Connect**
- (a) Duplo clique:
  - Evidência: duas requisições passam por `FindBlockingIntegrationAsync`, e o índice único global de `PolpIntegrationId` (`B/Data/AppDbContext.cs:213`) estoura.
  - Impacto: vira 500, e o `ConsentRecord` fica duplicado.
- (b) Reconexão com revogação pendente:
  - Evidência: a linha é reaproveitada com `SyncedLocally=true` (`PolpController.cs:139-173`; `PolpDisconnectService` não zera esse campo).
  - Impacto: a conexão fica "ativa" sem histórico, e a revogação pendente deixa de ser tentada.
- Correção: capturar a violação do índice único e reler a linha. Zerar `SyncedLocally`/`LastSyncedAt` ao desconectar.
- Status: CONFIRMADO (código). No caso (b), é NÃO CONFIRMADO se a Polp devolve o mesmo consentimento.

**G-M6. "Atualizar saldo" engana**
- Evidência:
  - `F/components/.../AccountSwitcher.jsx` usa `find(a => a.canSyncBalance)`, então há um botão por conexão, não por conta.
  - `PolpController.cs:350-356` devolve o saldo antigo com 200 quando a Polp não traz `available_amount`, e a tela mostra "Saldo atualizado".
  - A janela de 1 min é consumida antes da chamada (`:337`), então até um 502 gasta o minuto.
- Impacto: a poupança no mesmo banco nunca é atualizada, e a mensagem diz sucesso sem ter atualizado nada.
- Status: CONFIRMADO.

**G-M7. Nome de conta gerado colide e esconde o produto**
- Evidência: `PolpSyncService.cs:120,365-370`.
- Impacto: duas contas correntes do mesmo banco aparecem com o mesmo nome.
- Correção: sufixar com os últimos dígitos.
- Status: CONFIRMADO.

**G-M8. Compra de cartão marcada como "Pendente" aparece como "Atrasado" em "Próximos vencimentos"**
- Evidência:
  - `PolpSyncService.cs:345` marca como Pending quem tem `bill_id` nulo e data de lançamento na fatura vazia ou `0001-01-01`, com `DueDate` = data da compra (`:270`).
  - `B/DTOs/DashboardDtos.cs:112-115` transforma Pending com data passada em `Overdue`.
  - `F/pages/dashboard/Cartoes/Cartoes.jsx:142-143` busca todas as despesas `Overdue` e `Pending`.
- Impacto: compras do cartão viram "Atrasado", duplicando a fatura na lista.
- Status: lógica CONFIRMADA. O cenário depende do payload: o sandbox não tem nenhuma transação com a data `0001-01-01` (0 de 1043), então é NÃO CONFIRMADO se acontece na prática. A parte de data está em R-M4.

**G-M9. Polling em `GET /consents/{id}` contra a orientação da documentação; URL de autenticação pode estar vencida**
- Evidência:
  - A documentação diz "Não faça polling nesta rota" (240 req/min). `PolpController.cs:264-267` consulta a cada 5 s por conexão.
  - `url_to_authenticate_expires_at` = criação + 1 h (sandbox). `Connect` não confere a expiração (`:180`).
- Impacto: cerca de 20 conexões simultâneas esgotam a cota, e o usuário pode receber um link vencido.
- Correção: depender do webhook `consents`. Fazer polling só com backoff. Se a URL tiver expirado, usar `recreate`.
- Status: CONFIRMADO (documentação e código).

**G-M10. Duplo envio e corridas em endpoints de dinheiro**
- Evidência:
  - `CategoriesController.cs:29-40,51-57` e `RecurrencesController.cs:92-151`: corrida no índice único vira 500.
  - `InstallmentsController.cs:89-95`: `UsedLimit +=` é leitura seguida de escrita, sem token de concorrência.
  - POST de transação, cartão e parcelamento não têm idempotência.
- Impacto: limite usado errado, parcelamento duplicado e 500.
- Status: CONFIRMADO (código). A ausência de `IsConcurrencyToken` não foi checada em todos os modelos.

**G-M11. Valores sem teto; overflow derruba os relatórios do usuário**
- Evidência:
  - `[Range(0.01, double.MaxValue)]` nos DTOs de valor. `TotalInstallments` vai até `int.MaxValue`.
  - `InstallmentsController.cs:46-47` multiplica valores e pode estourar.
  - As somas em `TransactionsController.cs:211-218` e `DashboardController.cs:52-58` também podem estourar.
- Correção: teto de 1e9, como já existe em `CreateBankAccountDto`, e limite de parcelas.
- Status: falta de teto CONFIRMADA. O overflow não foi executado.

**G-M12. Limite usado e dias do cartão regrediram**
- Evidência:
  - O fallback `limite − disponível` foi removido. v1: `PolpSyncService.cs:136-139` @81d7091. v2: `:166-167`. `AvailableAmount` existe no DTO e não é usado.
  - Fechamento e vencimento vêm só da fatura (`:207-210`). Quando o vencimento é adiado para dia útil, o dia gravado fica errado.
- Status: CONFIRMADO (lógica). A divergência real é NÃO CONFIRMADO.

**G-M13. Totais do relatório excluem transferências, mas a lista e a contagem não**
- Evidência: `TransactionsController.cs:150` (`totalCount`) contra `:162`/`:257` (`SumTotalsAsync`). `F/.../CardStatement.jsx` mostra os dois.
- Impacto: a soma da lista não bate com o total exibido.
- Status: CONFIRMADO.

**G-M14. Logout e "sair de todos os dispositivos" falham em silêncio**
- Evidência: `F/layouts/DashboardLayout/DashboardLayout.jsx:76-79` e `F/pages/dashboard/Privacidade/Privacidade.jsx:82-85` usam `catch {}` e limpam o estado local mesmo assim.
- Impacto: o cookie continua válido. Num computador compartilhado, um F5 restaura a sessão.
- Status: CONFIRMADO.

**G-M15. Exportação e exclusão incompletas**
- Evidência:
  - A exportação (`AccountController.cs:63-124`) não inclui `RecurrenceDecision`, `Ownership`, o vínculo parcelamento → cartão/conta, nem `TimeZone`. Metas e reserva ficam só no `localStorage`.
  - `AccessLogs` sobrevivem à exclusão até expirar a retenção (sem FK, por desenho).
  - Cartão manual "excluído" é apenas desativado (`CreditCardsController.cs:88-97`), e não há UI para excluir.
- Status: CONFIRMADO. Se isso é aceitável para a política de privacidade fica para decisão. Registrar como risco aberto no relatório de privacidade.

### Baixo

| ID | Achado | Evidência | Status |
|---|---|---|---|
| G-B1 | Conta padrão do Dashboard escolhida sem `OrderBy`; pode divergir das outras abas | `DashboardController.cs:21`, `PolpController.cs:192-195`, `F/context/AccountContext.jsx:132` | CONFIRMADO no código. A divergência real depende do Postgres. |
| G-B2 | `GET /polp/integrations` dispara backfill (efeito colateral) a cada 5 min, para sempre, se a conexão não tem contas | `PolpController.cs:204-212,388-389` | CONFIRMADO |
| G-B3 | Truncamento silencioso em 50 páginas (750 itens em listas, 25 mil transações), sem log | `PolpService.cs:254,374` | CONFIRMADO. A paginação segue `next_cursor` corretamente em todas as listas. |
| G-B4 | Campos devolvidos e nunca lidos: `enrichmentPending`, `rawStatus`, `retryAfterSeconds`, `status`/`textColor`/`initials` do conector; `color` da instituição nunca vem da API | `PolpController.cs:46-56,233,304,334` | CONFIRMADO |
| G-B5 | Endpoints sem nenhuma tela: CRUD de categorias, `POST/PUT/DELETE /transactions`, `PUT /transactions/{id}/fixed`, CRUD de `bank-accounts` (exceto DELETE), `GET /credit-cards`, `DELETE /credit-cards/{id}`, `/recurrences` (Radar de Recorrências sem UI) | Contrato-api, seção B | CONFIRMADO. Não é regressão da migração: a UI de lançamento manual já não existia em 81d7091. |
| G-B6 | Diálogo de desconexão diz que apaga "parcelamentos que vieram desse banco"; na verdade apaga também parcelamentos e transações manuais lançados nessas contas | `AccountSwitcher.jsx`, `PolpDisconnectService.cs:142-147` | CONFIRMADO |
| G-B7 | Saldo exibido = só `available_amount`; ignora o `automatically_invested_amount` e saldos reservados | `PolpSyncService.cs:131`; sandbox com `has_reserved_balance=true` | CONFIRMADO (decisão de produto) |
| G-B8 | Falhas engolidas no frontend: `/auth/me` com 500 vira "deslogado" (`AuthContext.jsx:29-33`); skeleton eterno em `Privacidade.jsx:33-37`; `investmentStorage.js:21-23` sem try/catch | indicados | CONFIRMADO |
| G-B9 | O CLAUDE.md diz que busca e ordenação do relatório em memória têm teto, mas não têm: `ToListAsync()` sem `Take` (`TransactionsController.cs:147`). Não corta dado; o custo é memória e tempo. | indicado | CONFIRMADO |

---

## 4. Achados em PolpWebhookController, PolpWebhookProcessor e PolpSyncService (para a reescrita)

### Crítico

**R-C1. Backfill marca a conexão como sincronizada mesmo quando a Polp falhou** — ✅ CORRIGIDO na FASE 1 (seção 10).
- Evidência:
  - `PolpSyncService.cs:262-265`: `catch (HttpRequestException) { continue; }`.
  - `:188-191`: o mesmo em faturas, com `return`.
  - `:83`: `SyncedLocally = true` logo em seguida.
  - Nenhum caminho refaz o backfill depois disso.
- Impacto: um 429, 5xx ou timeout numa conta deixa essa conta sem histórico para sempre. Depois só chegam as janelas dos webhooks seguintes. Saldo e totais ficam errados sem aviso. Ver também G-A3.
- Correção: marcar `SyncedLocally` só se todas as contas e faturas forem lidas, ou persistir o estado por conta e tentar de novo.
- Status: CONFIRMADO (código, conferido pela líder).

**R-C2. "Fatura atual" exibe fatura já paga e vencida** — ✅ CORRIGIDO na FASE 1 (seção 10). Nota: a regra "payments[] soma o total" usada aqui como evidência estava errada; ver seção 10.
- Evidência:
  - `PolpSyncService.cs:193-210` escolhe o vencimento mais próximo no futuro ou, se não houver, a última fatura.
  - A rota `/bills` só traz faturas fechadas. `payments[]` não é usado.
  - No sandbox, em 15 de 20 cartões a última fatura está 100% paga e vencida. Exemplo: o app exibiria 6152,07 com vencimento em 2026-10-23.
  - A fatura aberta equivale à soma das transações com `bill_id` nulo. Num cartão, 6823,09 = `used_amount`.
- Impacto: valor e vencimento errados no card do cartão e nos lembretes.
- Correção: descartar faturas quitadas (pelos `payments`) e calcular a fatura aberta pelas transações sem `bill_id`, ou por `used_amount`.
- Status: CONFIRMADO (dado real e código).

**R-C3. O app exige assinatura HMAC no webhook, mas a Polp não documenta assinatura** — ✅ MITIGADO na FASE 1: `Polp:WebhookRequireSignature` (seção 10). A confirmação no Dashboard da Polp continua aberta.
- Evidência:
  - `PolpWebhookController.cs:26-27,111-134` exige `X-Webhook-Signature`: 401 sem ela, 503 sem secret.
  - As páginas `webhooks` e `webhooks/events` não mencionam assinatura. O comentário em `PolpWebhookProcessor.cs:17-18` diz "a doc não prevê assinatura".
  - `appsettings.Local.json` não tem `WebhookSigningSecret`.
- Impacto: se a Polp não enviar esse header, todo webhook é rejeitado. Depois do backfill, nada mais atualiza.
- Ação antes da reescrita: confirmar no Dashboard da Polp (Webhooks, provider Celcoin) se existe assinatura, com qual header e algoritmo.
- Status: NÃO CONFIRMADO. Potencialmente crítico.

### Alto

**R-A1. `PAGAMENTO_FATURA` no cartão vira receita** — ✅ CORRIGIDO na FASE 1 (seção 10).
- Evidência: `PolpService.cs:180-181` declara `transaction_type` e comenta que é pagamento de fatura. `RemoteTransaction.From` (`PolpSyncService.cs:338-347`) ignora o campo e usa só `CREDITO`.
- Impacto: "Receitas" infladas na conta-cartão. Complementa G-C1.
- Status: CONFIRMADO que o campo é ignorado. O sandbox só tem `PAGAMENTO/DEBITO` (1043 de 1043), então o efeito é NÃO CONFIRMADO.

**R-A2. Webhook responde 200 e processa em `Task.Run`, sem fila; dedupe marca o aviso antes de processar; não há reconciliação** — ✅ CORRIGIDO na FASE 1 (seção 10).
- Evidência: `PolpWebhookController.cs:62-81`, `PolpWebhookProcessor.cs:28-36` (janela de 5 s). Não há "sincronizar agora" nem job periódico.
- Impacto: reinício do app, banco fora ou 429 fazem o aviso se perder, e o dado fica velho sem recurso.
- Correção: persistir o evento antes de responder e processar a partir de uma fila no Postgres. Dedupe só após sucesso. Reconciliação periódica por `LastSyncedAt` ou endpoint de re-sync com limite de frequência.
- Status: CONFIRMADO (código). A política de reenvio da Polp não está documentada (NÃO CONFIRMADO).

**R-A3. `ToDictionary` sem índice único pode travar para sempre o sync de uma conta** — ✅ CORRIGIDO na FASE 1 (seção 10).
- Evidência: `PolpSyncService.cs:240-243` indexa por `(BankAccountId, Description)` todas as transações da conta, inclusive manuais. Não existe índice único nesse par.
- Impacto: duas transações com a mesma chave lançam `ArgumentException`, e todo sync da conta passa a falhar, só com log. Exemplos de chave repetida: duas manuais com `Description` nula, ou duplicata gerada por corrida (R-M1).
- Correção: `GroupBy` ou considerar só ids da Polp. Índice único parcial. Ver também G-M2 e G-M3.
- Status: CONFIRMADO (código, conferido pela líder).

### Médio

| ID | Achado | Evidência | Status |
|---|---|---|---|
| ✅ R-M1 | Locks, dedupe e retentativas só em memória (`SyncLocks`, `Recent`, cache). Com mais de uma instância, há sync concorrente e transações duplicadas. `SyncLocks` cresce sem limpeza. | `PolpSyncService.cs:27-40`, `PolpWebhookProcessor.cs:26` | Código CONFIRMADO. O número de instâncias é NÃO CONFIRMADO. |
| ✅ R-M2 | Webhook de transações de recurso ainda desconhecido é descartado. `AccountsAsync` não puxa faturas nem transações de cartões novos. | `PolpWebhookProcessor.cs:84-110` | CONFIRMADO (lógica) |
| ✅ R-M3 | Janela de datas: a Polp interpreta valor sem `Z` como horário de Brasília. Se vier `Z` ou offset, a janela desloca 3 h e perde dados. | Teste real: `fromCreatedAt=…T17:32:00` sem Z → 0 resultados; com Z → 1. `PolpService.cs:200-223` | CONFIRMADO no sandbox |
| ✅ R-M4 | Parcelas futuras ficam na data da compra. `bill_forecast_date` (mês previsto) e `charge_number` são ignorados. | `PolpSyncService.cs:270,287,345` | NÃO CONFIRMADO: não há amostra no sandbox |
| ✅ R-M5 | `account` lido antes do lock. O webhook `consents` pode reescrever `DISCONNECT_PENDING` para `AUTHORISED` e "ressuscitar" uma conexão desconectada. | `PolpWebhookProcessor.cs:69-82,102-121`, `PolpSyncService.cs:46-58` | Código CONFIRMADO. Janela estreita. |

### Baixo

| ID | Achado | Evidência | Status |
|---|---|---|---|
| ✅ R-B1 | Data inválida vira `DateTime.UtcNow` em silêncio, e muda a cada sync | `PolpSyncService.cs:384` | CONFIRMADO |
| ✅ R-B2 | Datas em UTC: transação depois das 21h de Brasília no último dia do mês cai no mês seguinte nos filtros por UTC | `PolpSyncService.cs:375-382`, `TransactionsController.cs:120-122` | NÃO CONFIRMADO: depende do agrupamento por dia |
| ✅ R-B3 | Cartão com vários plásticos usa só `payment_methods.FirstOrDefault()` | `PolpSyncService.cs:160` | NÃO CONFIRMADO: o sandbox tem 1 plástico por cartão |
| ✅ R-B4 | `enrichmentPending` (`PARTIAL_SUCCESS`) mistura enriquecimento pendente com recurso indisponível | `PolpController.cs:233` | CONFIRMADO |

---

## 5. O que não foi possível confirmar, e por quê

1. **Assinatura do webhook (R-C3).** A documentação pública não descreve. Só o Dashboard da Polp pode responder. É o item mais urgente a verificar antes da reescrita.
2. **Política de reenvio e timeout do webhook (R-A2).** Não está documentada.
3. **Pagamento de fatura, estorno, cashback e parcelas futuras do lado do cartão (R-A1, R-M4, G-M8).** O sandbox só devolve `PAGAMENTO/DEBITO` com fatura real.
4. **Transições de consentimento (EXPIRED, REJECTED, revogado no banco), formato do objeto `error` e se `avoidDuplicates` devolve o mesmo consentimento após uma revogação pendente (G-A1, G-M5b).** No sandbox o consentimento está sempre AUTHORISED.
5. **Respostas 422 e 429 reais e PJ em instituição BUSINESS (G-A3, G-A4).** Exigiriam POST ou provocar o limite de requisições, o que estava fora das regras desta rodada.
6. **Paginação com mais de uma página.** O sandbox tem no máximo 59 itens por recurso e ignora `per_page`. O código segue o cursor corretamente, mas isso não foi exercitado.
7. **Se havia usuários reais com dados da v1** apagados pela migration. Sem acesso ao banco.
8. **Número de instâncias em produção e o `Polp:BaseUrl` configurado lá (R-M1, G-A8).** Não há arquivos de deploy no repositório.
9. **Nada foi executado.** Overflow (G-M11), corridas (G-M5, G-M10) e ordem sem `OrderBy` (G-B1) foram concluídos por leitura de código.

## 6. Notas sobre a condução da auditoria

- **Credenciais expostas na sessão.** O auditor polp-docs leu `appsettings.Local.json` com `grep`, e a redação falhou. O `ClientSecret` e o `WebhookToken` do sandbox apareceram na saída da ferramenta, dentro do transcript desta sessão. Eles não estão neste relatório. Se o transcript for compartilhado ou guardado, rotacione essas credenciais.
- **Requisições externas.** Nenhum POST, PUT, PATCH ou DELETE foi feito. A autenticação da Polp v2 é por cabeçalhos estáticos, então não foi preciso pedir token. Foram 28 GETs na documentação e cerca de 130 GETs no sandbox, mais 2 na raiz da v2 (fora do sandbox):
  - `GET /institutions`: rota pública.
  - `GET /consents`: rota de produção, consultada com as mesmas credenciais. Respondeu `data: []`. Isso **excedeu a regra "GET só no sandbox"**. Foi só leitura, sem nenhum efeito.

---

## 7. Rodada de correções (2026-11-04)

Itens 1 a 5 aprovados para correção. Houve um dono para o backend e outro para o frontend, e a líder revisou os diffs. `PolpWebhookController`, `PolpWebhookProcessor` e `PolpSyncService` **não foram alterados**. Também não mudou nenhum texto além do item 3, nenhum layout e nenhum formato de API.

### Item 1. Pagamento de fatura contado em dobro (G-C1)

- **Identificação:** só pelo `category_ref` que a Polp manda. O dado real do sandbox (23 de 23 "PAGAMENTO FATURA CARTAO" de conta) vem com `LOAN_PAYMENTS_CREDIT_CARD_PAYMENT`, que a documentação define como "Pagamento de fatura de cartão de crédito". Não há heurística por nome.
- **Arquivos:**
  - `backend/Services/PolpCategoryMap.cs`: `LOAN_PAYMENTS_CREDIT_CARD_PAYMENT` vira a categoria **"Pagamento de fatura"**. A regra fica antes de `LOAN_`; os outros `LOAN_` continuam "Empréstimos".
  - `backend/Services/TransferDetection.cs`: "Pagamento de fatura" entra no conjunto de categorias de movimentação própria. Fica fora dos totais de receita, despesa e categorias, mesmo sem o nome do titular.
- **O que continua igual:** a transação segue listada nos lançamentos e no relatório, com `isTransfer=true`. O `PolpSyncService` já usava `PolpCategoryMap.ToCategoryName`, então a correção vale para o sync sem tocar nele.
- **Testes** (`backend.Tests/PolpCategoryTotalsTests.cs`):
  - `Pagamento_de_fatura_tem_categoria_propria_e_outros_LOAN_continuam_Emprestimos`
  - `Pagamento_de_fatura_da_conta_corrente_e_movimentacao_propria_sem_nome_do_titular`: o caso do relatório, "PAGAMENTO FATURA CARTAO".
  - `Despesa_do_mes_conta_as_compras_do_cartao_uma_vez_so_e_o_pagamento_da_fatura_continua_no_relatorio`:
    - Cenário: compras de 320,45 e 179,55 no cartão, padaria de 25,10 e pagamento da fatura de 500 na conta corrente.
    - Despesa do mês em todas as contas: 525,10, sem a categoria "Pagamento de fatura" nem "Empréstimos".
    - Dashboard da conta corrente: 25,10. Dashboard do cartão: 500.
    - O relatório lista o pagamento com `isTransfer=true`.

### Item 2. Transferências entre contas próprias (G-A6)

- **Como funcionava antes:** em `81d7091`, o nome da categoria vinha direto da Polp v1 (`rt.Category?.Description`, `PolpSyncService.cs:253`). A Polp mandava "Transferência mesma titularidade", que o `TransferDetection` reconhece. A v2 manda o código `TRANSFER_IN_ACCOUNT_TRANSFER` / `TRANSFER_OUT_ACCOUNT_TRANSFER` (pela documentação, "Transferência recebida/enviada entre contas próprias"; 24 de 24 no sandbox), mas o mapa o transformava em "Transferências", e o sinal se perdia.
- **Arquivos:**
  - `backend/Services/PolpCategoryMap.cs`: esses dois códigos voltam a virar **"Transferência mesma titularidade"**. A regra fica antes de `TRANSFER_`; as outras transferências continuam "Transferências".
  - `backend/Services/TransferDetection.cs`: o nome virou constante, sem mudança de comportamento.
- **Testes:**
  - `Transferencias_entre_contas_proprias_viram_mesma_titularidade_e_as_demais_continuam_Transferencias`
  - `Transferencia_entre_contas_do_mesmo_titular_nao_altera_receita_nem_despesa`:
    - Cenário: saída de 2000 no banco A, entrada de 2000 no banco B, salário de 3000 e despesas de 120,30 e 45,70.
    - Todas as contas: receita 3000 e despesa 166,00.
    - Banco A: 3000 / 120,30. Banco B: 0 / 45,70.

### Item 3. Texto do aviso de consentimento (G-A5)

- **Arquivos:**
  - `frontend/src/pages/conectar-banco/ConectarBanco.jsx`: só o `<dd>` de "Como a sincronização funciona" mudou.
  - A versão do aviso subiu de `2026-10-30` para `2026-11-04` em `frontend/src/legal/versions.js` e `backend/Services/LegalDocuments.cs`. As duas precisam bater: o backend recusa outra versão, e o `ConsentRecord` passa a registrar qual texto o usuário aceitou.
- **Texto anterior:**
  > Os dados são atualizados quando você abre o painel ou pede uma sincronização (no máximo a cada 5 minutos por banco). Não é em tempo real.
- **Texto novo:**
  > Depois da primeira importação, a Polp atualiza os dados periodicamente, conforme a agenda de cada banco, e nos avisa quando há novidades. Você também pode pedir a atualização do saldo de uma conta no seletor de contas. Não é em tempo real.
- **Teste:** não há teste automatizado para texto. A cobertura veio de `npm run build` e `npm run lint`. O backend só compara a versão pela constante (`PolpController.cs:98,165`), e não existe valor fixo antigo em nenhum outro lugar.

### Item 4. Timeout e retry no cliente HTTP da Polp (G-A3)

- **Arquivos:**
  - `backend/Services/PolpRetryHandler.cs`: novo `DelegatingHandler`, sem dependência nova.
  - `backend/Program.cs`: registra o handler no `AddHttpClient<IPolpService, PolpService>` e define `HttpClient.Timeout = 90 s`.
  - `backend/ControlFinance.API.csproj`: `InternalsVisibleTo` para os testes injetarem a espera e o relógio.
- **O que o handler faz:**
  - Aplica timeout de **15 s por tentativa** a qualquer método.
  - Cancelamento de quem chamou sobe na hora, sem nova tentativa.
  - Repete **só GET e HEAD**, em 5xx, 429 ou timeout, com **no máximo 3 tentativas**.
  - Espera de forma exponencial com jitter aleatório (0–500 ms, depois 0–1000 ms).
  - `Retry-After` (em segundos ou como data HTTP) substitui o backoff. Acima de 10 s, devolve a resposta sem esperar, para não segurar a requisição do usuário.
  - POST, PUT, PATCH e DELETE nunca se repetem.
  - O log registra só o status e o número da tentativa, nunca o corpo da resposta.
  - O `HttpClient.Timeout` de 90 s cobre o pior caso: 3 × 15 s mais 2 esperas de até 10 s.
- **Testes** (`backend.Tests/PolpRetryHandlerTests.cs`, com handler HTTP falso):
  - `Get_com_503_e_depois_200_tenta_de_novo_e_devolve_200`
  - `Post_com_503_faz_uma_chamada_so`
  - `Delete_com_503_faz_uma_chamada_so`
  - `Retry_After_de_2_segundos_no_429_e_a_espera_usada`
  - `Retry_After_acima_do_teto_devolve_o_429_sem_esperar`
  - `Retry_After_como_data_http_vira_a_espera_ate_essa_data`
  - `Get_com_503_sempre_desiste_apos_3_chamadas_e_devolve_503`
  - `Erro_400_nao_e_repetido`
  - `Timeout_da_tentativa_em_get_repete`
  - `Timeout_em_todas_as_tentativas_lanca_TaskCanceledException_tratada_como_falha_do_provedor`
  - `Timeout_em_post_nao_repete`
  - `Cancelamento_do_chamador_propaga_sem_repetir`
- **Continua aberto (G-A3):** mapear 422 → 400 e 429 → 429 na borda dos controllers, e um limitador global de requisições à Polp.

### Item 5. Produção apontando para o sandbox (G-A8)

- **Arquivos:**
  - `backend/Services/ConfigSecrets.cs`: `Validate(config, isDevelopment)`. Fora de Development e com a Polp ligada, um `Polp:BaseUrl` contendo "/sandbox" gera o erro "Polp:BaseUrl aponta para o Sandbox da Polp", sem o valor. O `Program.cs` já recusa subir quando `Validate` devolve erro fora de Development.
  - `backend/Program.cs`: o aviso antigo no console foi removido.
- **Testes** (`backend.Tests/ConfigSecretsTests.cs`):
  - `Development_com_sandbox_sobe`
  - `Producao_com_sandbox_e_Polp_ligada_recusa_citando_so_a_chave`
  - `Producao_com_url_de_producao_sobe`
  - `Producao_com_sandbox_e_Polp_desligada_sobe`
- **Efeito no deploy:** o `appsettings.json` versionado ainda aponta para o sandbox. Produção com a Polp ligada precisa definir `Polp__BaseUrl`, como já mostra o `.env.example`.

### Registrado para a reescrita de PolpSyncService, PolpWebhookProcessor e PolpWebhookController

1. **Lado do cartão do pagamento de fatura (R-A1):** `RemoteTransaction.From(PolpCardTransactionDto)` ignora `transaction_type=PAGAMENTO_FATURA` e importa o crédito como receita no cartão. A reescrita deve dar a esse lançamento a categoria `TransferDetection.CardBillPaymentCategoryName`, ou não importá-lo. O sandbox não tem nenhuma amostra (1043 de 1043 transações são `PAGAMENTO/DEBITO`), então isso precisa ser validado com dado real.
2. **Transações já gravadas com a categoria antiga** ("Empréstimos" ou "Transferências", vindas desses códigos): o upsert só recategoriza quando a categoria é nula ou "Outros", e o `category_ref` não é gravado, então elas não se corrigem sozinhas. Na reescrita:
   - Opção 1: guardar o `category_ref`.
   - Opção 2: permitir reclassificar quando a categoria atual é a que o mapa antigo produziria e o usuário não a editou.

   Impacto atual: a auditoria encontrou `GET /consents` de produção vazio, e a v2 só tem conexões de teste. Reconectar resolve nesses ambientes.
3. **Falha definitiva no backfill (R-C1):** o retry já vale para todas as chamadas do `PolpService`, inclusive do sync e do webhook, sem mudar esses arquivos. Mas uma falha que persiste depois das 3 tentativas ainda cai no `catch … continue`, e a conexão é marcada como `SyncedLocally`. A correção é da reescrita.

### Riscos conhecidos desta correção

- **Reconhecimento pelo nome da categoria:** ✅ resolvido na seção 8. "Pagamento de fatura" e "Transferência mesma titularidade" saem dos totais pelo nome. Se o usuário renomear essa categoria, o lançamento volta a contar. Um lançamento manual posto numa categoria com esse nome sai dos totais. É o mesmo comportamento que já existia para "Transferência mesma titularidade" na v1.
- **Próximos vencimentos:** ✅ resolvido na seção 8. a lista de `Cartoes.jsx:142-148` não filtra `isTransfer`. Um "Pagamento de fatura" pendente apareceria ali como conta a pagar. Nada é somado, então não afeta totais. Fica registrado, sem correção nesta rodada.
- **Frontend:** a única soma de receita e despesa feita a partir de lista é em `Relatorios.jsx:84-87`, e ela já respeita `isTransfer`. As demais telas usam totais e categorias do backend, que agora excluem os dois casos.

### Resultado das verificações

| Comando | Resultado |
|---|---|
| `dotnet build backend/ControlFinance.API.csproj` | 0 erros, 0 avisos |
| `dotnet test backend.Tests/ControlFinance.API.Tests.csproj` | **448 aprovados, 0 falhas** (21 testes novos) |
| `npm run build` (frontend) | ok |
| `npm run lint` (frontend) | sem erros nem avisos |

`dotnet build` na raiz (`Controlfinance.sln`) não compila nada: a solução tem só uma pasta "backend", sem projetos dentro. O comando termina com "êxito", mas não testa nada. Por isso o build foi feito pelos `.csproj`. Vale incluir os dois projetos no `.sln`, o que não foi feito porque estava fora do escopo.

---

## 8. Segunda rodada de correções (2026-11-04)

Itens aprovados: decidir os totais pelo `category_ref`, filtrar "Próximos vencimentos" e incluir os projetos na `.sln`. Houve um dono para o backend e outro para o frontend, e a líder revisou os diffs. `PolpWebhookController` e `PolpWebhookProcessor` não foram alterados. O `PolpSyncService` ganhou só as duas atribuições do novo campo, explicadas no item 1. Não mudou nenhum texto, layout nem formato de API. A migração **não foi aplicada**.

### Item 1. Transferências e pagamento de fatura decididos pelo `category_ref`

Resolve o risco "reconhecimento pelo nome da categoria" registrado na seção 7.

**Coluna nova**
- `Transaction.PolpCategoryRef` (`string?`, até 100 caracteres), em `backend/Models/Transaction.cs` e `backend/Data/AppDbContext.cs`.
- Migração `backend/Migrations/20261104191248_AddTransactionPolpCategoryRef.cs`, junto com `.Designer.cs` e o snapshot. Foi gerada com `dotnet ef migrations add` em Development. Antes, conferimos que a ferramenta não conecta ao banco: não existe `IDesignTimeDbContextFactory`, e nada antes de `builder.Build()` abre conexão. Ela só adiciona a coluna anulável:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<string>(
        name: "PolpCategoryRef",
        table: "Transactions",
        type: "character varying(100)",
        maxLength: 100,
        nullable: true);
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropColumn(
        name: "PolpCategoryRef",
        table: "Transactions");
}
```

**Gravação no sync**

O `PolpSyncService` ganhou exatamente duas atribuições. A segunda é necessária porque a Polp manda o `category_ref` depois, num webhook de enriquecimento. Sem ela, um pagamento de fatura importado antes do enriquecimento continuaria contando como despesa.

```diff
                     existing.PaidAt = paidAt;
+                    existing.PolpCategoryRef = rt.CategoryRef ?? existing.PolpCategoryRef;
 ...
                     DueDate = dueDate,
+                    PolpCategoryRef = rt.CategoryRef,
```

**Regra de exclusão** (`backend/Services/TransferDetection.cs`)
- Só sai dos totais a transação cujo `PolpCategoryRef` é exatamente `LOAN_PAYMENTS_CREDIT_CARD_PAYMENT`, `TRANSFER_IN_ACCOUNT_TRANSFER` ou `TRANSFER_OUT_ACCOUNT_TRANSFER`. A comparação é ordinal.
- **Único sinal que ficou:** o `category_ref`. É gravado pelo sync, e o usuário não tem como editá-lo.
- **Sinais removidos**, todos dependentes de algo que o usuário edita:
  - o nome da categoria ("Transferência mesma titularidade", "Pagamento de fatura", "Transferências", "TED", "DOC"), que pode ser renomeada ou trocada;
  - o nome da transação e a regex "transferência/TED/DOC enviada/recebida". O nome é editável em `TransactionsController.cs:426,474`;
  - a comparação do nome da contraparte com o nome do titular (`NamesMatch`), que dependia do nome da transação.
- Outro sinal não editável que existe: `transaction_type=PAGAMENTO_FATURA`, do lado do cartão. Ele não é gravado, e usá-lo exige mudar o sync. Continua no R-A1, para a reescrita.
- **Usos ajustados:**
  - `DashboardController`, `TransactionsController` (GetAll, GetReport, GetAggregates, GetDetails, SumTotalsAsync) e `TransactionDto.FromEntity` passam o `PolpCategoryRef`.
  - Saíram as cinco buscas do nome do titular.
  - `IsTransfer` continua no DTO com o mesmo significado. A exportação de dados não mudou.

**Diferenças em relação à regra antiga**
- **Lançamentos manuais não têm `category_ref` e sempre contam nos totais**, em qualquer categoria. Na regra antiga, um lançamento manual posto numa categoria chamada "Transferência mesma titularidade" ou "Pagamento de fatura" saía dos totais. Também saía quando o nome seguia o padrão "Transferência enviada|<nome do titular>".
- **Transações sincronizadas antes da migração** ficam com `PolpCategoryRef` nulo. Os pagamentos de fatura e as transferências próprias entre elas voltam a contar até a Polp reenviar a transação e o upsert gravar o código, ou até a conexão ser refeita. A migração não faz backfill, como foi pedido. Impacto atual: a v2 só tem conexões de teste. **Antes de produção:** reconectar as contas de teste depois de aplicar a migração.
- **Transferências comuns para terceiros** (`TRANSFER_OUT_WITHDRAWAL`, `TRANSFER_IN_DEPOSIT` etc.) contam normalmente, como antes.

**Nota de privacidade** ✅ resolvida na seção 9: o código não é mais guardado.
- `PolpCategoryRef` é gravado **sem cifra**. É um código da taxonomia fixa da Polp, não texto livre, e não precisa ser consultado em SQL.
- Mas é mais granular que a categoria do app. Por exemplo, `MEDICAL_PHARMACIES_AND_SUPPLEMENTS` contra "Saúde".
- Se o relatório de privacidade exigir minimização, há duas saídas: cifrar a coluna por value converter, ou gravar só os três códigos que a regra usa. Fica registrado como decisão em aberto.

**Testes** (`backend.Tests`)
- Novos, com os controllers reais, em `PolpCategoryTotalsTests.cs`:
  - `Pagamento_de_fatura_e_transferencia_propria_ficam_fora_de_todos_os_totais_e_continuam_listados_como_transferencia`. Cobre agregados de todas as contas e por conta (incluindo categorias e estabelecimentos), Dashboard por conta, relatório (caminho SQL e em memória), GetAll e GetDetails.
  - `Renomear_a_categoria_e_recategorizar_a_transacao_nao_muda_os_totais`. Renomeia "Pagamento de fatura" para "Cartão" e recategoriza por `SetDetails` e por `PUT`.
  - `Lancamento_manual_em_categoria_com_nome_de_pagamento_de_fatura_ou_mesma_titularidade_conta_normalmente`
  - `Transferencia_comum_para_terceiros_conta_normalmente`
  - `Proximos_vencimentos_recebem_pagamento_de_fatura_pendente_marcado_como_transferencia`
- `TransferDetectionTests.cs`: os 7 testes da regra por nome foram substituídos por 2 Theories.
  - Os 3 códigos são reconhecidos como movimentação própria.
  - Contam normalmente: null, vazio, `TRANSFER_OUT_WITHDRAWAL`, `TRANSFER_IN_DEPOSIT`, código com sufixo, código em minúsculas, outros `LOAN_`/`FOOD_` e os nomes de categoria.
- Removidos:
  - `PolpCategoryTotalsTests.Pagamento_de_fatura_da_conta_corrente_e_movimentacao_propria_sem_nome_do_titular`, coberto pelas Theories.
  - `DashboardTests.Categoria_de_mesma_titularidade_tambem_e_excluida_dos_totais`, porque a regra se inverteu.
- Ajustados para gravar o `PolpCategoryRef`, sem mudar nenhum valor esperado: `DashboardTests` (2 testes), `AggregatesTests` (cenário `Big`), `TransactionReportTests` e os dois cenários da rodada anterior em `PolpCategoryTotalsTests`. `TestDb.AddTransaction` ganhou um parâmetro opcional.

### Item 2. Próximos vencimentos na aba Cartões

- **Arquivos:**
  - `frontend/src/utils/upcomingDues.js` (novo): `buildUpcoming` foi movida de `Cartoes.jsx` sem mudar a lógica. A única mudança é o filtro `.filter((t) => !t.isTransfer)` nas transações, aplicado antes da ordenação e do corte em 8 itens.
  - `frontend/src/pages/dashboard/Cartoes/Cartoes.jsx`: passa a importar a função. Textos, layout e chamadas de API não mudaram.
- **Efeito:** pagamento de fatura e transferência própria, pendentes ou vencidos, não aparecem mais. Faturas de cartão e parcelamentos continuam aparecendo.
- **Verificação** (o frontend não tem test runner e não se adicionou dependência):
  - O lado do backend é coberto pelo teste `Proximos_vencimentos_recebem_pagamento_de_fatura_pendente_marcado_como_transferencia`. Ele garante que `GET /transactions?status=Pending&type=Expense` marca o pagamento com `isTransfer=true`.
  - No frontend, um script fora do repositório empacota a função real com o rolldown, que já está em `node_modules`, e roda 5 asserções. Todas passaram: Pending e Overdue com `isTransfer` omitidos; despesa comum aparece com o objeto completo; faturas e parcelamentos continuam; ordem por data e limite de 8 preservados.
  - Contraprova: o mesmo script contra a função sem o filtro falha.

### Item 3. `Controlfinance.sln`

- A pasta de solução "backend", que estava vazia, foi removida.
- `backend/ControlFinance.API.csproj` e `backend.Tests/ControlFinance.API.Tests.csproj` foram incluídos na raiz da solução.
- `dotnet build` e `dotnet test` na raiz agora compilam e testam os dois projetos.

### Resultado das verificações (na raiz, pela `.sln`)

| Comando | Resultado |
|---|---|
| `dotnet build` | 2 projetos compilados, 0 erros, 0 avisos |
| `dotnet test` | **457 aprovados, 0 falhas**, 0 ignorados |
| `npm run build` (frontend) | ok |
| `npm run lint` (frontend) | sem erros nem avisos |

---

## 9. Terceira rodada: marcação mínima no lugar do `category_ref` (2026-11-04)

O objetivo é não guardar o código detalhado da Polp. Um dono do backend fez a troca e a líder revisou o diff. O frontend não foi alterado: ele não referencia o campo e só lê `isTransfer`, que não mudou (conferido por grep). A migração **não foi aplicada**. Não mudou nenhum texto, layout nem formato de API.

### Modelo

- `backend/Models/Transaction.cs`:
  - Enum novo `PolpMovementKind { None, CardBillPayment, OwnTransfer }`.
  - A propriedade `string? PolpCategoryRef` foi trocada por `PolpMovementKind PolpMovementKind`.
- `backend/Data/AppDbContext.cs`:
  - O enum é gravado como texto (`HasConversion<string>().HasMaxLength(20)`), não anulável, seguindo o padrão de `Type` e `Status`.
  - O padrão `None` das linhas existentes vem da migração. O modelo não usa `HasDefaultValue` porque, com o sentinela `None`, o EF deixaria de enviar o valor no insert.

### Conversão: ponto único

Ela fica em `backend/Services/TransferDetection.cs`, junto da regra que consome o enum.

- `ToMovementKind(string? categoryRef)` compara de forma ordinal:

  | Código da Polp | Valor do enum |
  |---|---|
  | `LOAN_PAYMENTS_CREDIT_CARD_PAYMENT` | `CardBillPayment` |
  | `TRANSFER_IN_ACCOUNT_TRANSFER` | `OwnTransfer` |
  | `TRANSFER_OUT_ACCOUNT_TRANSFER` | `OwnTransfer` |
  | Qualquer outro, inclusive null, vazio, minúsculas e sufixo | `None` |

- `IsSelfTransfer(PolpMovementKind kind)` devolve `kind != None`. O comportamento dos totais é idêntico ao da seção 8.
- As constantes dos códigos são privadas. O código completo só existe em memória durante o sync; não é gravado nem logado.
- `PolpCategoryMap` continua usando os códigos, só para escolher o nome de exibição da categoria.

### Sync

As mesmas duas linhas do `PolpSyncService` passam a gravar o enum. Nada mais mudou no arquivo:

```diff
-                    existing.PolpCategoryRef = rt.CategoryRef ?? existing.PolpCategoryRef;
+                    existing.PolpMovementKind = rt.CategoryRef is null ? existing.PolpMovementKind : TransferDetection.ToMovementKind(rt.CategoryRef);
 ...
-                    PolpCategoryRef = rt.CategoryRef,
+                    PolpMovementKind = TransferDetection.ToMovementKind(rt.CategoryRef),
```

Quando o enriquecimento ainda não trouxe o `category_ref`, a marcação existente é mantida, com a mesma semântica de antes.

### Migração

Arquivo `backend/Migrations/20261104192237_ReplacePolpCategoryRefWithMovementKind.cs`, com `.Designer.cs` e snapshot.

- Foi gerada com `dotnet ef migrations add`, e o `Up`/`Down` foi ajustado à mão.
- A migração anterior não foi editada.
- `dotnet ef migrations has-pending-model-changes`, que roda offline, respondeu "No changes have been made to the model since the last migration".

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<string>(
        name: "PolpMovementKind",
        table: "Transactions",
        type: "character varying(20)",
        maxLength: 20,
        nullable: false,
        defaultValue: "None");

    migrationBuilder.Sql(
        """UPDATE "Transactions" SET "PolpMovementKind" = 'CardBillPayment' WHERE "PolpCategoryRef" = 'LOAN_PAYMENTS_CREDIT_CARD_PAYMENT';""");
    migrationBuilder.Sql(
        """UPDATE "Transactions" SET "PolpMovementKind" = 'OwnTransfer' WHERE "PolpCategoryRef" IN ('TRANSFER_IN_ACCOUNT_TRANSFER', 'TRANSFER_OUT_ACCOUNT_TRANSFER');""");

    migrationBuilder.DropColumn(
        name: "PolpCategoryRef",
        table: "Transactions");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<string>(
        name: "PolpCategoryRef",
        table: "Transactions",
        type: "character varying(100)",
        maxLength: 100,
        nullable: true);

    // A distinção IN/OUT se perde; a regra dos totais só precisa saber que é transferência própria.
    migrationBuilder.Sql(
        """UPDATE "Transactions" SET "PolpCategoryRef" = 'LOAN_PAYMENTS_CREDIT_CARD_PAYMENT' WHERE "PolpMovementKind" = 'CardBillPayment';""");
    migrationBuilder.Sql(
        """UPDATE "Transactions" SET "PolpCategoryRef" = 'TRANSFER_OUT_ACCOUNT_TRANSFER' WHERE "PolpMovementKind" = 'OwnTransfer';""");

    migrationBuilder.DropColumn(
        name: "PolpMovementKind",
        table: "Transactions");
}
```

**Por que funciona nos dois cenários**
- **Se a migração anterior já foi aplicada:** a nova converte os três códigos para o enum e apaga a coluna com o código completo. As transações já marcadas continuam fora dos totais.
- **Se não foi:** o EF aplica a anterior antes, cria a coluna vazia, a conversão não tem efeito e a coluna é removida.
- **Down:** restaura a coluna com um código representativo de cada caso. O comportamento dos totais é preservado, mas perde-se a distinção entre entrada e saída da transferência.

**Resíduos do código antigo**

`PolpCategoryRef` agora só aparece nas duas migrações e nos `.Designer.cs` delas. Não aparece em modelo, serviço, controller, DTO, snapshot, teste nem frontend.

### Testes

- **`TransferDetectionTests`:**
  - Teste da conversão: os três códigos viram o valor certo.
  - Viram `None`: `MEDICAL_PHARMACIES_AND_SUPPLEMENTS` (farmácia), null, vazio, minúsculas, sufixo, `TRANSFER_OUT_WITHDRAWAL`, `TRANSFER_IN_DEPOSIT`, outros `LOAN_` e `FOOD_`, e os nomes de categoria.
  - `IsSelfTransfer` é verdadeiro para `CardBillPayment` e `OwnTransfer`, e falso para `None`.
- **Testes existentes adaptados ao enum**, com os mesmos cenários e valores esperados: `TestDb.AddTransaction` e `AggregatesTests.Add` recebem `PolpMovementKind`. Os ajustes ficaram em `DashboardTests`, `AggregatesTests`, `TransactionReportTests` e `PolpCategoryTotalsTests`. Este último grava `ToMovementKind(categoryRef)`, como o sync. Nenhum teste foi removido.

### Arquivos alterados nesta rodada

- `backend/Models/Transaction.cs`
- `backend/Data/AppDbContext.cs`
- `backend/Services/TransferDetection.cs`
- `backend/Services/PolpSyncService.cs`: as 2 linhas
- `backend/DTOs/DashboardDtos.cs`
- `backend/Controllers/DashboardController.cs`
- `backend/Controllers/TransactionsController.cs`
- `backend/Migrations/20261104192237_ReplacePolpCategoryRefWithMovementKind.cs`, com o `.Designer.cs`
- `backend/Migrations/AppDbContextModelSnapshot.cs`
- `backend.Tests/Support/TestDb.cs`, `AggregatesTests.cs`, `DashboardTests.cs`, `TransactionReportTests.cs`, `PolpCategoryTotalsTests.cs` e `TransferDetectionTests.cs`

### Resultado das verificações

| Comando | Resultado |
|---|---|
| `dotnet build` (raiz) | 0 erros, 0 avisos |
| `dotnet test` (raiz) | **463 aprovados, 0 falhas** |
| `npm run build` (frontend) | ok |
| `npm run lint` (frontend) | sem erros nem avisos |

---

## 10. FASE 1: reescrita do webhook, do processor e do sync (2026-11-05)

### Como a rodada foi conduzida

- **Equipe:**
  - 2 investigadores, só leitura e sem rede.
  - 2 donos: **INBOX** (controller, inbox, worker, limpeza, assinatura, `PolpController`, migração) e **SYNC** (`PolpSyncService`, `PolpWebhookProcessor`, lock por integração, desconexão, `PolpService`, reconciliação).
  - A líder escreveu o contrato entre os donos (`backend/Services/PolpInboxContracts.cs`) e o desenho, e revisou os diffs. Depois pediu duas correções ao INBOX: R-M5 no `PolpController` e lease vencido contando tentativa.
- **Fonte dos fatos da Polp:** o scratchpad com as respostas do sandbox tinha sido apagado. Os trechos da documentação oficial foram recuperados do transcript local da 1ª rodada, sem nenhuma chamada de rede. Os pontos que decidiram o desenho estão abaixo, todos **DOCUMENTADOS** e sem amostra real no sandbox:
  - `payments[]` de uma fatura são os pagamentos feitos durante o ciclo dela, que quitam a anterior. "Fatura paga" não pode ser deduzido dos `payments` da própria fatura. **A evidência usada em R-C2 na seção 4 estava errada nesse ponto.**
  - `bill_forecast_date` (AAAA-MM) vem sempre. Uma transação na fatura aberta pode vir com `bill_id` preenchido.
  - O `resource_id` muda por evento: em `accounts`/`credit_cards`/`consents` é o consentimento; em `*.transactions`/`bills` é a conta ou o cartão.
  - A cota regulatória é consumida pelos syncs que a Polp agenda com o banco. GETs à API da Polp só contam no rate limit global, de 240/min por cliente.
  - O consentimento tem `flags` (`ERROR_PROCESSING_CATEGORIES`, `ERROR_PROCESSING_COUNTERPARTIES`, `PARTIALLY_UNAVAILABLE_RESOURCES`).
  - `payment_methods` só tem `identification_number` e `is_multiple_credit_card`. Nada indica qual plástico é o do titular.

### O que foi feito, item a item

**1. Inbox persistente (R-A2)**
- **Entidade e índices:** entidade `PolpWebhookEvent`, com os campos pedidos, em `backend/Models/PolpWebhookEvent.cs`. Índice único parcial em `DedupKey WHERE "Status" IN ('Pending','Processing')` e índice `(Status, NextAttemptAt)`. Não há FK.
- **Controller (`PolpWebhookController`):**
  - Valida o token (404) e a assinatura conforme a configuração (401/503).
  - Valida o corpo (400) e grava pela `IPolpInbox`.
  - **Só responde 200 depois do commit.** Se a gravação falha, responde 500 e o log leva só o tipo da exceção.
  - Sem `Task.Run` e sem dedupe em memória.
- **Duplicata:**
  - Um aviso igual a outro ainda pendente ou em processamento atualiza o `ReceivedAt` dessa linha e responde 200.
  - O worker só marca Done se o `ReceivedAt` não mudou desde o claim; se mudou, devolve o evento para Pending. Assim, um aviso que chega durante o processamento do mesmo aviso nunca se perde.
- **Worker (`PolpInboxWorker`):**
  - Claim com `FOR UPDATE SKIP LOCKED` no Postgres. Nos testes, em SQLite, um caminho equivalente com update condicional.
  - Lease de 5 min, renovado a cada minuto.
  - Processa em série: 1 evento por vez por instância, por causa do rate limit global da Polp.
  - Espera crescente entre tentativas: 30 s, 2 min, 10 min, 1 h e depois 6 h. Na **8ª** falha o evento vira Dead, com `LogError`.
  - **Lease vencido conta como tentativa** e também chega a Dead. Um evento que derrube o processo não volta em loop.
  - No desligamento, o evento volta para a fila sem contar tentativa.
- **Erros e privacidade:**
  - `LastError` guarda só o tipo, o status HTTP e as mensagens próprias do app, truncado em 500 caracteres.
  - Sem corpo da Polp, nomes ou valores no log.
- **Limpeza diária (`PolpInboxCleanupService`, `IJobLock` 0x…0004):** apaga eventos Done com mais de 30 dias. Por decisão do dono, também apaga **Dead com mais de 90 dias**: as linhas guardam ids ligados a um usuário, e o `LogError` já ficou registrado.

**2. Falhas da Polp (R-C1)**
- **Sem catch que engole erro.**
  - 5xx, 429 e timeout (o `PolpRetryHandler` já tenta de novo no GET) e `PolpResourceNotReadyException` viram retentativa do evento.
  - 404 e `PolpPermanentException` viram Dead.
- **Backfill:** lê tudo da Polp antes. `SyncedLocally` e `LastSyncedAt` só são gravados no mesmo commit que os dados, depois de um sucesso completo. Se uma conta falha, a exceção sobe, nada parcial fica gravado e a conexão não é marcada como sincronizada.
- **Ainda sem dados:** contas e cartões vazios, ou Polp desligada (`NullPolpService`), viram NotReady, ou seja, retentativa.
- **`MaxPages` esgotado com cursor:** vira `PolpPermanentException` com `LogError`, em vez de truncar em silêncio.

**3. Concorrência e desconexão (R-M1, R-M5)**
- **Lock por integração (`IIntegrationLock`, `backend/Services/IntegrationLock.cs`):**
  - Em Postgres: transação na conexão do próprio `DbContext`, `SET LOCAL lock_timeout = '60s'` e `pg_advisory_xact_lock(classid, hash do Guid)`.
  - Em testes: um semáforo por chave.
  - `SyncLocks` e `IsDuplicate` foram removidos.
- **Padrão de cada operação:**
  - Antes do lock: chamadas HTTP, criação de categorias e leitura do fuso.
  - No lock, numa transação curta: a integração é **recarregada**. Se ela está em `DISCONNECT_PENDING` ou foi removida, nada é gravado.
  - Gravações de status são updates condicionais (`WHERE Status <> 'DISCONNECT_PENDING'`).
- **Desconexão:** marca o pendente antes e fora do lock. Apaga os dados sob o lock. `RetryPendingAsync` também roda sob o lock.
- **`PolpController`:**
  - `GetIntegrationStatus` e o `Connect` com linha reaproveitada usam update condicional.
  - Se a desconexão acontece durante a consulta de status, a resposta é 404. É o formato que a rota já usava para integração pendente.
  - `Connect` sobre uma linha `DISCONNECT_PENDING` responde 409 `{ message: "Não foi possível iniciar a conexão com o banco." }`, resposta e texto que já existiam.

**4. Ordem e reconciliação (R-M2)**
- **Recurso ainda inexistente localmente nunca é descartado.** Consentimento, conta ou cartão desconhecido vira `PolpResourceNotReadyException`, ou seja, retentativa.
- **Conta ou cartão novo** num evento `accounts` ou `credit_cards` já vem com as faturas e as transações completas dele.
- **`Task.Run` e `QueueBackgroundSync` do `PolpController` foram removidos:**
  - `Connect` e `GetIntegrationStatus` passam a enfileirar `internal.backfill`.
  - O evento `consents` também enfileira `internal.backfill`, e o dedupe evita fazer o backfill em dobro.
  - `GetIntegrations` não dispara mais sync (G-B2).
- **Reconciliação (`PolpReconciliationService`, `IJobLock` 0x…0005):**
  - Intervalo em `Polp:ReconciliationIntervalMinutes`: 1440 em `appsettings.json` e 1 em `appsettings.Development.json`. **Esse arquivo é gitignored**, então o valor 1 vale só nesta máquina.
  - Cada rodada enfileira `internal.reconcile` para toda integração AUTHORISED já sincronizada e `internal.backfill` para as incompletas. Não enfileira nada para as pendentes.
  - O `internal.reconcile` reprocessa os últimos 7 dias: `fromCreatedAt` e `fromUpdatedAt` = agora − 7 dias, em horário de Brasília e sem "Z". A API combina os dois filtros com OR.

**5. Dados**
- **Fatura atual (R-C2)**, com "hoje" no fuso do usuário (UserClock):
  1. Percorre as faturas fechadas com vencimento a partir de hoje, da mais próxima para a mais distante. A primeira **não paga** vira a atual, com valor e vencimento dela. Uma fatura conta como paga quando os `PAGAMENTO_FATURA` com data posterior ao fechamento dela somam pelo menos o total.
  2. Se não sobrar nenhuma, a atual é a **fatura em aberto**:
     - Valor: compras não futuras depois do último fechamento, menos créditos e estornos, sem pagamentos.
     - Vencimento: o da última fatura mais 1 mês, avançando mês a mês até ficar a partir de hoje.
  3. Sem nenhuma fatura fechada: soma das compras não faturadas, com vencimento nulo.
  4. Se a Polp devolve a lista de faturas vazia para um cartão que já teve fatura, a fatura gravada é mantida.
  5. **Nunca grava como atual uma fatura paga ou vencida.**
- **`PAGAMENTO_FATURA` no cartão (R-A1):** vira `PolpMovementKind.CardBillPayment` e fica fora dos totais. Um `category_ref` que chegue depois nunca rebaixa essa marcação.
- **Mantidos:** `PolpMovementKind` (regra por `category_ref`), `ProductType`, `PolpAccountType = CREDIT_CARD` e o `CreditCardSummaryBuilder` com o `.Include`, que não foi alterado.
- **Upsert (R-A3):** atua só sobre linhas com id da Polp, usa `GroupBy` em vez de `ToDictionary` e atualiza a duplicata mais antiga. Lançamentos manuais não são tocados.
- **Datas (R-B2, R-B1):**
  - `DueDate` passa a ser o dia civil no fuso do usuário, gravado à meia-noite UTC, como no lançamento manual.
  - Data inválida tenta, nesta ordem: `bill_post_date`, depois a data já gravada. Se nenhuma servir, vira `PolpPermanentException` só com o id técnico. Nunca usa `UtcNow`.
- **Parcelas futuras (R-M4):** marcadas como Pending no `DueDay` do cartão, no mês de `bill_forecast_date`.
- **Janela de datas (R-M3):** `PolpQueryWindow` converte valores com Z ou offset para Brasília e emite sem sufixo.
- **Número do cartão (R-B3):** passa a ser o `identification_number` mais frequente nas transações, e só quando o histórico está completo. Isso coincide com o plástico do titular apenas se ele for o mais usado; a documentação não permite confirmar.
- **`enrichmentPending` (R-B4):** coluna nova `PolpIntegration.EnrichmentPending`, verdadeira só quando as `flags` têm `ERROR_PROCESSING_CATEGORIES` ou `ERROR_PROCESSING_COUNTERPARTIES`. O formato da API não muda.

**6. Assinatura configurável (R-C3)**
- `Polp:WebhookRequireSignature`, com padrão true.
- Com false:
  - a assinatura é ignorada;
  - `WebhookSigningSecret` deixa de ser exigido em `ConfigSecrets.Validate`;
  - o token continua obrigatório;
  - a inicialização registra um `LogWarning`, sem valores.
- **Atenção ao ambiente local:** o `appsettings.Local.json` não tem `WebhookSigningSecret`. Para receber webhooks localmente, defina `Polp:WebhookRequireSignature=false` ou configure o secret.

### Migração nova (só adiciona; não aplicada)

`backend/Migrations/20261105020124_AddPolpWebhookInbox.cs`, com `.Designer.cs`. Foi gerada com `dotnet ef migrations add`. `has-pending-model-changes` responde "No changes".

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<bool>(name: "EnrichmentPending", table: "PolpIntegrations", type: "boolean", nullable: false, defaultValue: false);

    migrationBuilder.CreateTable(
        name: "PolpWebhookEvents",
        columns: table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            Event = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
            Resource = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
            ResourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
            QueryParameters = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
            DedupKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
            Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
            Attempts = table.Column<int>(type: "integer", nullable: false),
            NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            LockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
            ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
        },
        constraints: table => { table.PrimaryKey("PK_PolpWebhookEvents", x => x.Id); });

    migrationBuilder.CreateIndex(name: "IX_PolpWebhookEvents_DedupKey", table: "PolpWebhookEvents", column: "DedupKey", unique: true, filter: "\"Status\" IN ('Pending', 'Processing')");
    migrationBuilder.CreateIndex(name: "IX_PolpWebhookEvents_Status_NextAttemptAt", table: "PolpWebhookEvents", columns: new[] { "Status", "NextAttemptAt" });
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropTable(name: "PolpWebhookEvents");
    migrationBuilder.DropColumn(name: "EnrichmentPending", table: "PolpIntegrations");
}
```

**Migrações pendentes na árvore, nenhuma aplicada:**
- `20261104191248_AddTransactionPolpCategoryRef`
- `20261104192237_ReplacePolpCategoryRefWithMovementKind`
- `20261105020124_AddPolpWebhookInbox`

### Arquivos

- **Reescritos:**
  - `backend/Controllers/PolpWebhookController.cs`
  - `backend/Services/PolpWebhookProcessor.cs`
  - `backend/Services/PolpSyncService.cs`
- **Novos:**
  - `backend/Models/PolpWebhookEvent.cs`
  - `backend/Services/PolpInboxContracts.cs` (contrato, escrito pela líder)
  - `backend/Services/PolpInbox.cs`
  - `backend/Services/PolpInboxWorker.cs`
  - `backend/Services/PolpWebhookProcessorHandler.cs`
  - `backend/Services/PolpInboxCleanupService.cs`
  - `backend/Services/IntegrationLock.cs`
  - `backend/Services/PolpReconciliationService.cs`
  - a migração acima
- **Alterados:**
  - `backend/Controllers/PolpController.cs`
  - `backend/Services/PolpDisconnectService.cs`
  - `backend/Services/PolpService.cs` (DTOs `bill_forecast_date`, `identification_number` e `flags`; `PolpQueryWindow`; `MaxPages`)
  - `backend/Services/ConfigSecrets.cs`
  - `backend/Services/JobLock.cs` (chaves 0004 e 0005)
  - `backend/Models/PolpIntegration.cs`
  - `backend/Data/AppDbContext.cs`
  - `backend/Migrations/AppDbContextModelSnapshot.cs`
  - `backend/Program.cs`
  - `backend/appsettings.json`
  - `backend/appsettings.Development.json` (gitignored)
- **Testes novos:**
  - `PolpWebhookInboxTests`
  - `PolpInboxWorkerTests`
  - `PolpControllerInboxTests`
  - `PolpSyncTests`
  - `PolpSyncDataTests`
  - `Support/InboxHarness.cs` e `Support/SyncHarness.cs`
  - Os nomes dos testes por achado estão nos relatórios dos donos e nos próprios arquivos.
- **Teste existente alterado de propósito:** `CardBillTimeZoneTests` (em `BackgroundJobsTimeZoneTests.cs`). Agora passa pelo evento `bills` e espera `ClosingDay`/`DueDay` da última fatura fechada. Os valores e vencimentos esperados da fatura atual não mudaram.
- **Frontend:** nenhuma alteração nesta fase. O formato da API não mudou.

### Resultado das verificações

| Comando | Resultado |
|---|---|
| `dotnet build` (raiz) | 0 erros, 0 avisos |
| `dotnet test` (raiz) | **574 aprovados, 0 falhas** |
| `npm run build` (frontend) | ok |
| `npm run lint` (frontend) | sem erros nem avisos |

O `dotnet build --no-incremental` mostra 24 avisos xUnit1026. Já existiam e estão em `BackgroundJobsTimeZoneTests.cs` e `UserTimeZoneTests.cs`.

### Riscos e pontos não confirmados

- **Sem amostra real no sandbox**, estes pontos seguem só a documentação:
  - `PAGAMENTO_FATURA`, `ESTORNO` e as parcelas futuras com `bill_forecast_date`;
  - se `bill_forecast_date` é o mês do vencimento;
  - compra feita no dia do fechamento (a regra usa data maior que o fechamento);
  - paginação com mais de uma página;
  - consentimento só com cartão.

  A regra da fatura atual inteira depende da semântica documentada de `payments[]` e `PAGAMENTO_FATURA`. **Validar com um cartão real antes de produção.**
- **O SQL específico de Postgres não foi executado.** O claim com SKIP LOCKED e o advisory lock foram verificados pelo SQL gerado (`ToQueryString`) e por leitura; todos os testes rodam em SQLite. Validar contra Postgres num ambiente de homologação.
- **Ruído previsível no log:** avisos de um consentimento já removido viram NotReady e, depois de cerca de 25 h, Dead com `LogError`. Evitar isso exigiria guardar um marcador de integração removida, o que é schema novo e não foi feito.
- **Assinatura do webhook (R-C3):** continua aberta a confirmação, no Dashboard da Polp, de que a assinatura existe e de qual é o formato.
- **Capacidade da reconciliação:** com o rate limit global de 240/min, cada integração custa cerca de 5 a 10 GETs. Com milhares de usuários, a rodada diária leva horas, processada em série pelo worker. Funciona, mas os webhooks esperam na mesma fila. Avaliar prioridade de eventos externos sobre internos se a base crescer.
