# Relatório de qualidade do Control Finance

Data: 2026-10-31. Branch: `polp-v2`. Equipe de 5 teammates (Sonnet):
- **Donos**, que editam código: backend e frontend.
- **Investigadores**, que não editam nada: bugs, duplicacao e medicao.

A líder revisou os diffs e rodou a verificação final. Nada foi commitado, ninguém rodou nada que conectasse ao banco e nenhuma migração foi criada.

Os achados de bugs vêm da leitura do código e não foram reproduzidos num app rodando, porque isso exigiria `dotnet run` contra o banco, que é o de produção.

## Verificação final

| Comando | Resultado |
|---|---|
| `dotnet build --no-incremental` | 0 avisos, 0 erros |
| `npm run lint` | limpo |
| `npm run build` | OK. Entry de 236,4 KB (75,7 KB gzip). Maior chunk: `CategoricalChart` (recharts), com 302 KB (90 KB gzip), carregado só no dashboard. |

## Desempenho (Lighthouse 13.6.0, mobile, mediana de 3 execuções)

| Página | Performance | LCP | CLS | TBT | Transferido |
|---|---|---|---|---|---|
| home `/` | 88 → 92 | 3908 → 3311 ms | 0,042 → 0 | 0 → 0 | 517 → 394 KB |
| login `/loginemail` | 100 → 100 | 756 → 758 ms | 0,058 → 0 | 0 → 0 | 319 → 195 KB |
| cadastro `/register` | 99 → 100 | 757 → 758 ms | 0,078 → 0 | 0 → 0 | 326 → 202 KB |

"Planos" é uma seção da home, não uma rota, então está coberto pela medição da home.

A medição "depois" foi feita após as mudanças de desempenho. As correções de bugs posteriores não mexeram em assets, e o entry variou só 0,1 KB.

| Bundle (bruto / gzip) | Antes | Depois |
|---|---|---|
| Entry `index-*.js` | 236,2 / 75,7 KB | 236,4 / 75,7 KB |
| Chunk do recharts | 320,9 / 94,7 KB (`CategoryDonutChart`) | 302,3 / 90,2 KB (`CategoricalChart`, compartilhado) |
| Fonte Prompt | ttf, 171 KB | woff2, 52 KB, com preload |
| `cflogobranco.svg` | 56,8 KB | 46,2 KB |
| `cflogopreto.svg` | 31,8 KB | 28,2 KB |

O ganho nas páginas públicas vem quase todo da fonte em woff2. O LCP da home continua em 3,3 s. Preload e `fetchpriority` no hero foram testados e não ajudaram (ver "Aprovados e aplicados"). O gargalo é o tamanho da imagem.

Metodologia:
- Build e preview numa pasta de rascunho, fora de `frontend/dist`.
- `lighthouse <url> --only-categories=performance --chrome-flags="--headless=new --no-sandbox"`.

## Bugs corrigidos

### Backend

| # | Arquivo | Causa | Correção |
|---|---|---|---|
| B1 (aprovado) | `Services/InstallmentProgress.cs` | O avanço mensal da parcela não liberava `UsedLimit`, e na quitação ficava limite preso para sempre (num 12x de 100, 500 presos) | Ao avançar, subtrai `InstallmentAmount × meses avançados` do limite usado do cartão manual e da conta-cartão, com piso em 0. Não corrige limites que já vazaram no passado. |
| B2 (aprovado) | `Controllers/TransactionsController.cs` (totais do `GetReport`) | Os totais cobriam só as 2000 linhas mais recentes | Novo `SumTotalsAsync` soma o filtro inteiro em streaming (`AsAsyncEnumerable`), respeitando busca e transferências. A listagem em memória mantém o teto de 2000. |
| B10 (aprovado) | `Controllers/PolpController.cs` (`GetConnectors`) | A resposta não tinha `type`, e bancos PJ sem "Empresas" no nome caíam na lista de PF | Campo `type` adicionado ao JSON. É aditivo: o `bankSorting.js` já lia esse campo. |
| B3 | `TransactionsController.Update` | Regravava `PaidAt` a cada edição de transação já paga e não zerava `ReminderSentAt` quando o vencimento mudava | `PaidAt` preservado, e `ReminderSentAt` zerado quando a `DueDate` muda |
| B4 | `TransactionsController`, `BankAccountsController`, `RecurrencesController` | Ano ou mês inválido dava 500, e `Enum.TryParse` aceitava números de enum inexistentes | 400 "Ano ou mês inválido." e `Enum.IsDefined` (mesma mensagem 400 de antes) |
| B5 | `Services/CreditCardSummaryBuilder.cs` | No dia do vencimento, o cartão manual já pulava para o mês seguinte | Comparação com `today.Date` |
| B6 | `Controllers/CategoriesController.cs` | Nome duplicado estourava o índice único (500) | 409 "Já existe uma categoria com esse nome." |
| B9 | `Services/InstallmentProgress.cs` | Duas requisições em paralelo, vindas da tela de Cartões, quitavam a mesma parcela, e a segunda dava 500 | `DbUpdateConcurrencyException` capturada. O `SaveChanges` é transacional, então não há liberação de limite em dobro. |
| B12 | `PolpController`, `PolpDisconnectService`, `InstitutionNameResolver`, mais o novo `Services/PolpFailure.cs` | Só `HttpRequestException` era tratada. Timeout ou JSON inválido davam 500 depois de apagar dados locais e abortavam o retry. | Timeout e JSON inválido passam a ser tratados como falha do provedor, onde o fluxo já tratava falha de rede |

### Frontend

| # | Arquivo | Causa | Correção |
|---|---|---|---|
| F1 | `pages/conectar-banco/ConectarBanco.jsx` | Consentimento já autorizado vem sem `urlToAuthenticate`. A tela mostrava "A conexão demorou" mesmo com o banco conectado. | Status `syncing`/`active` segue o caminho de sucesso (aguarda a sincronização e vai ao dashboard) |
| F2 | `ConectarBanco.jsx` | O CPF/CNPJ era apagado antes de a autenticação terminar, e a nova tentativa mandava documento vazio (400) | Documento mantido em memória até sair da tela |
| F3 | `DashboardHome`, `ReceitasDespesas`, `Categorias`, `Relatorios`, `Cartoes` | O erro nunca era limpo e substituía a tela inteira para sempre | Erro limpo a cada carga bem-sucedida. Em Relatórios, o erro ao salvar uma edição aparece inline sem esconder a tabela. |
| F4 | `utils/financeMath.js`, `Relatorios.jsx` | Datas UTC exibidas no fuso local: vencimentos apareciam um dia antes no Brasil | `timeZone: "UTC"` e agrupamento por `dueDate.slice(0,10)` |
| F5 | `utils/trend.js` | Tendência com mês anterior negativo invertia o sinal | Divisão por `Math.abs(previous)` |
| F6 | `hooks/usePolpConnectionWatcher.js`, `services/polpConnection.js`, `ConectarBanco.jsx` | O watcher rodava sem sessão, e um id pendente antigo redirecionava o cadastro e as páginas legais para o login. O polling continuava depois de sair da tela. | Watcher só com sessão ativa, e polling cancelado ao desmontar (`cancelledRef`) |
| F7 | `components/layout/Navbar/Navbar.jsx`, `Footer/Footer.jsx` | Links de seção (`#planos` etc.) não funcionavam fora da home | `/#secao` |
| F9 (parte) | `pages/dashboard/Investimentos/Investimentos.jsx` | Meta com alvo 0 era aceita e já nascia "atingida" | Alvo menor ou igual a 0 rejeitado |

### Outras tarefas pedidas

- **`CreditCardSummaryBuilder`:** o `.Include(i => i.BankAccount)` voltou à consulta de parcelas.
- **`PolpController.LastBalanceSync`:** trocado por `IMemoryCache` com expiração de 1 minuto. O `lock` foi mantido, e o 429, a mensagem e o `retryAfterSeconds` são os mesmos.
- **`AuthController.Register`:** o `AuthService` agora devolve o enum `RegisterError`, e o 409 vem do código, não do texto. Status e mensagens são idênticos.

## Duplicação e código morto removidos

**Backend**
- `BankAccountDto.FromEntity`, que substitui 5 construções repetidas, e `CategoryDto.FromEntity`, que substitui 2. O JSON continua idêntico.

**Frontend**
- **Funções de API sem chamador:**
  - `createCategory`, `updateCategory`, `deleteCategory`;
  - `createTransaction`, `updateTransaction`, `deleteTransaction`, `setTransactionFixed`;
  - `getCreditCards`.
- **CSS e utilitários sem uso:**
  - a classe `.login__forgot`;
  - `getPhoneMask` e `PHONE_MASK_*`;
  - `export`s usados só no próprio arquivo: `CPF_MASK`, `CURRENT_USER_ID_KEY`, `BASE_MONTHLY_PRICE`, `clearPendingIntegrationId`.
- **Arquivos sem referência em `frontend/` e `backend/`:**
  - `golden-gate.jpg` (1,0 MB) e `tennis.jpg` (603 KB);
  - os 4 `control-finance-*.svg`;
  - `public/favicon.svg` e `public/icons.svg`.
- **Dependências:** removidas `@types/react` e `@types/react-dom`, porque o projeto é JS puro. Nenhuma dependência foi adicionada.
- **Duplicação consolidada:**
  - o hook `hooks/useModalBehavior.js` (trava de rolagem, foco e Esc), usado em `ConfirmDialog`, `DeleteAccountDialog` e `TermsDialog`;
  - `formatDate` em `ReceitasDespesas` e `Categorias`.

## Arquivos alterados por teammate

**backend**
- `Controllers/PolpController.cs`: `IMemoryCache` e catch ampliado.
- `Services/PolpFailure.cs` (novo).
- `Services/PolpDisconnectService.cs` e `Services/InstitutionNameResolver.cs`: B12.
- `Services/AuthService.cs` e `Controllers/AuthController.cs`: `RegisterError`.
- `DTOs/DashboardDtos.cs`, `Controllers/BankAccountsController.cs`, `Controllers/DashboardController.cs` e `Controllers/CategoriesController.cs`: `FromEntity`, B4 e B6.
- `Controllers/TransactionsController.cs`: B2, B3 e B4.
- `Services/InstallmentProgress.cs` e `Controllers/PolpController.cs`: B1 e B10, aplicados pela líder depois da aprovação.
- `Controllers/RecurrencesController.cs`: B4.
- `Services/CreditCardSummaryBuilder.cs`: `Include` e B5.
- `Services/InstallmentProgress.cs`: B9.

**frontend**
- `index.html` e `src/index.css`: fonte woff2 com preload.
- `src/assets/fonts/Prompt-Regular.woff2`: novo, substitui o `.ttf`.
- `src/assets/images/cflogobranco.svg` e `cflogopreto.svg`: svgo.
  - Comparação feita com `sharp` sobre fundo sólido, diferença máxima por canal de 8/255.
  - As imagens foram abertas e conferidas visualmente.
- `src/layouts/DashboardLayout/DashboardLayout.jsx`: prefetch dos gráficos em idle.
- `src/hooks/useModalBehavior.js` (novo), mais `ConfirmDialog.jsx`, `DeleteAccountDialog.jsx` e `TermsDialog.jsx`.
- `src/api/categories.js`, `transactions.js`, `creditCards.js`, `src/utils/masks.js`, `authToken.js`, `pricing.js`, `src/services/polpConnection.js` e `src/pages/login/Login.css`: código morto.
- `package.json` e `package-lock.json`: dependências `@types` removidas.
- `ConectarBanco.jsx`, `DashboardHome.jsx`, `ReceitasDespesas.jsx`, `Categorias.jsx`, `Relatorios.jsx`, `Cartoes.jsx`, `Investimentos.jsx`, `utils/financeMath.js`, `utils/trend.js`, `hooks/usePolpConnectionWatcher.js`, `Navbar.jsx` e `Footer.jsx`: bugs F1 a F9.
- Arquivos sem uso removidos, listados acima.

**bugs, duplicacao e medicao:** nenhum arquivo do projeto, porque são investigadores.

### Mudança fora da equipe

`Footer.jsx` e `Footer.css` (sem o `ShieldIcon` e com o logo em 30×30) foram alterados pelo usuário, não pela equipe.

### Aprovados e aplicados depois da primeira versão deste relatório

- **B1, B2 e B10:** estão na tabela de bugs do backend.
- **Mês atual em UTC:** das 21h à meia-noite do último dia do mês, o dashboard e as telas de receitas e despesas já mostravam o mês seguinte, zerado. O bug já existia antes desta rodada. O mês atual agora segue o calendário local: `America/Sao_Paulo` no `DashboardController` e a data local do navegador em `utils/monthlyTrend.js` e `Categorias.jsx`.
- **LCP da home:** testado em duas versões, as duas sem ganho, e por isso revertidas. O hero continua idêntico ao original.
  1. `<img fetchpriority="high">` no lugar do `background-image`: LCP de 3462 ms, contra 3311 ms antes.
  2. O mesmo, mais um preload só na rota `/` via script inline no `index.html`, com a imagem em `public/`: LCP de 3313 ms. Login e cadastro também ficaram iguais.
  3. Motivo: no Lighthouse mobile (4G simulado), o gargalo não é a descoberta da imagem. É o tamanho dela (205 KB) disputando banda com o JS e a fonte, e o hero só pinta depois que o React renderiza. O que reduz o LCP é diminuir a imagem: ver a lista abaixo.

## Precisa de aprovação

| Item | Recomendação |
|---|---|
| **B11:** `UseExceptionHandler` antes de `UseCors`, então todo 500 chega sem CORS e o front vê "Failed to fetch" | Investigar com um 500 real. Só mover o `UseCors` provavelmente não basta, porque o handler limpa a resposta. |
| **B14:** o e-mail de boas-vindas é aguardado no request (até 100 s), e uma exceção no `AccessLog` dá 500 com o usuário já criado | `try/catch` no `AccessLog` e timeout curto no e-mail |
| **B16:** a janela de 1 minuto do saldo é gravada antes da chamada à Polp, então uma falha bloqueia 1 minuto | Manter. Parece intencional, para proteger a cota da Polp. |
| **B18:** a exclusão de conta revoga o consentimento na Polp antes da transação local | Apagar os dados locais primeiro e enfileirar `PendingProviderDeletion` na mesma transação |
| **B7:** a conta padrão é escolhida sem `OrderBy` | Ordenar por `CreatedAt`, como o resto do app |
| **B8:** assinatura cancelada continua listada como ativa | Confirmar se é intencional |
| **B13:** se o backfill falhar sempre, a conexão fica em "syncing" para sempre e o `Connect` devolve 409 | Tempo limite para sair do estado "syncing" |
| **B15:** o resumo mensal depende de o processo estar no ar no dia 1º, e o lembrete ignora contas que vencem no próprio dia | Guardar a data do último envio e comparar só a data no lembrete |
| **B17:** conta da Polp pode ser editada à mão, e o `GetById` devolve contas inativas | Bloquear a edição de saldo de conta sincronizada e filtrar `IsActive` |
| **F7:** o link "Time" do rodapé aponta para `#time`, que não existe | Remover o link |
| **F8:** recarregar desmonta o dashboard, e um `catch` vazio mantém uma conta obsoleta | Corrigir junto com uma revisão do loading do dashboard |
| **F9 (parte):** `localStorage.setItem` sem `try/catch` | `try/catch` com mensagem ao usuário |
| **F10:** o texto "todas as contas conectadas" em Categorias não bate com o filtro, que é por conta | Corrigir o texto |
| **F11:** o aviso de consentimento ainda fala em sincronização a cada 5 min, mas hoje é por webhook | Atualizar o texto e subir `BANK_CONNECTION_NOTICE_VERSION` no front e no back |
| **F12:** o parcelamento é removido sem confirmação | Usar o `ConfirmDialog` existente |
| **F13:** a regex de parcela trata qualquer nome terminado em `dd/mm` como parcela | Revisar junto com dados reais |
| Endpoints sem chamador no frontend: CRUD de categorias e transações, `GET`/`DELETE` de cartões manuais, CRUD de contas exceto `DELETE`, e o controller de recorrências inteiro | Manter. São API pública, e remover recorrências exigiria migração. |
| Campos de DTO do `PolpService` nunca lidos | Manter até a reescrita da Polp |
| Negrito real da Prompt (pesos 500, 600 e 700, hoje sintetizados) | Não fazer, a menos que queira o visual novo |
| LCP da home (3,3 s), segunda tentativa: recorte do hero para celular | Gerar uma versão recortada no centro (cerca de 1024×1080, a parte que aparece na tela em pé) e servir com `<picture>` só em telas estreitas. Fica visualmente igual no celular e a imagem cai quase pela metade. Precisa de comparação visual antes de trocar. |
| Unificar a formatação de data e hora (`Cartoes` e `Privacidade`) | Não fazer. Muda levemente o formato exibido. |
| `img.footer__logo` sem `width`/`height` no HTML | Seu CSS novo fixa 30×30, então o CLS já é 0. Nada a fazer. |

## Fora de escopo (arquivos de webhook e sync da Polp, para a reescrita)

- `PolpSyncService`: o crédito do pagamento de fatura (`PAGAMENTO_FATURA`) entra como receita na conta do cartão.
- `PolpSyncService`: data ausente vira `UtcNow`, e a transação muda de data a cada sync.
- `PolpSyncService`: os semáforos por integração nunca são removidos.
- `PolpWebhookProcessor.IsDuplicate`: descarta por 5 s eventos idênticos mesmo se o primeiro falhou.
- `PolpWebhookController`: falhas no `Task.Run` são descartadas depois de responder 200.

---

# Rodada de correção dos números (2026-11-01)

Princípio desta rodada: nunca mostrar um número errado, nem como estado intermediário. "R$ 0,00" exibido enquanto carrega também é número errado.

## Verificação final

| Comando | Resultado |
|---|---|
| `dotnet build --no-incremental` (backend) | 0 avisos, 0 erros |
| `dotnet test` (backend.Tests) | 144 testes: 142 passam, 2 falham de propósito (revelam cálculos errados, ver abaixo) |
| `npm run lint` | limpo |
| `npm run build` | OK |

O stage do git não foi alterado: continua com os mesmos 74 arquivos.

## Prova de equivalência: NÃO EXECUTADA

O harness (`scratchpad/equiv/harness`) está escrito e compila contra as duas versões: a do stage e a do working tree. Ele aborta se a conexão não estiver em modo somente leitura, e foi o que aconteceu. O banco (provavelmente via pooler) ignorou `default_transaction_read_only` passado na conexão, e o harness parou antes de ler qualquer dado. Uma consulta de leitura avulsa com `psql` foi bloqueada pelas regras de permissão da sessão.

Por isso, as reduções de consulta da rodada anterior continuam no código, mas **não estão provadas**:
- contas carregadas uma vez no Dashboard;
- projeções em vez de entidades com `Include`;
- nome da categoria via navegação;
- `COUNT` condicional no relatório;
- consultas de integrações e parcelas puladas sem cartão sincronizado.

Pela regra desta rodada, elas só podem ficar com a prova. Decisão pendente: liberar a execução, com transação `READ ONLY` por consulta em vez da opção de conexão, ou reverter essas otimizações sem prova.

## Removido e mantido

- **Cache de abas do frontend:** removido por completo (`api/cache.js`, `apiGet`, `peek*`, `clearCache`). Toda aba busca no servidor ao abrir.
- **Cache de nome do usuário e cartão principal no backend:** removido. Os dois são lidos do banco a cada requisição. O cache do `SessionValidator` voltou a guardar só `IsActive` e `TokenVersion`.
- **Cache de 10 min de `GET /api/polp/connectors`:** mantido. Guarda só o catálogo global de instituições (`PolpService.GetInstitutionsAsync`, sem parâmetro de usuário). O DTO tem id, nome, logo, cor, tipo e status do banco, sem saldo, conexão nem consentimento.
- **`.Include(i => i.BankAccount)` no `CreditCardSummaryBuilder`:** mantido.
- **Pré-carregamento do JS das abas:** mantido.

## Migração dos índices (`20261101130000_AddPerformanceIndexes`)

```
Up:   CreateIndex IX_Transactions_UserId_BankAccountId_DueDate (Transactions: UserId, BankAccountId, DueDate)
      CreateIndex IX_Installments_UserId (Installments: UserId)
      CreateIndex IX_BankAccounts_UserId_IsActive (BankAccounts: UserId, IsActive)
Down: DropIndex dos três
```

- `IX_Installments_BankAccountId` já existia e não foi duplicado.
- O EF queria apagar `IX_BankAccounts_UserId`, que o composto novo cobriria. Foi mantido com um `HasIndex` explícito, para a migração só criar índices.
- **Não aplicada.** `dotnet ef database update` falhou porque `IX_Transactions_UserId_BankAccountId_DueDate` **já existe no banco**, criado fora do histórico de migrações. O Postgres desfez a transação inteira e nada mudou. A migração segue pendente.
- Recomendação: usar `CREATE INDEX IF NOT EXISTS` nos três, para a migração funcionar com ou sem os índices.

## Frontend: esqueletos e estados iniciais

- **Esqueletos:** o componente `components/ui/Skeleton` (respeita `prefers-reduced-motion`) substitui o "Carregando..." em Dashboard, Receitas e despesas, Categorias, Relatórios, Cartões, Investimentos e Privacidade.
- **Estados iniciais corrigidos:**
  - `AccountContext` começava com `accounts=[]`, então a primeira requisição saía sem filtro de conta e somava todas. Agora há `accountsStatus`: nenhuma tela busca nem mostra valor antes de as contas carregarem, e uma falha mostra erro.
  - Resultados ficam presos à chave da requisição (conta, refresh, filtros e página). Valores da conta anterior somem na hora da troca.
  - Cartões com falha mostravam StatCards zerados, e Investimentos mostrava "nenhuma conta" enquanto carregava.
  - Privacidade afirmava "sem prazo máximo" antes da resposta.
  - `formatCurrency` e `formatPercentage` transformavam `null`/`undefined`/`NaN` em "R$ 0,00"/"0%". Agora devolvem "—", e `formatDate` também.
  - `formatCurrency` arredonda para centavos e normaliza -0, para resíduo de float nunca virar "-R$ 0,00". A líder corrigiu o `roundMoney`, que devolvia -0.
  - `% da receita gasta` e `Taxa de poupança` mostravam 0% com receita zero. Agora mostram "—".
  - Limite usado ou fatura nulos viram "—", e um total com algum cartão sem o dado também.
  - Limite estourado não é mais escondido por `Math.max(0, …)`.
  - "Próximo vencimento" não mostra data passada e compara por data, não por instante.
  - `todayIso` do formulário de Cartões usava UTC.
  - O subtotal diário do relatório somava transferências.
- **AbortController:** cada efeito aborta a requisição anterior ao trocar conta, filtro ou página e ao sair da tela. O `AbortError` é ignorado. Além disso, o resultado só é exibido se a chave dele for a atual. O resumo e as transações do Dashboard continuam em paralelo.

## Backend: correções de data

- **`Services/BrazilClock`:** é a única definição de "agora" e "hoje" em America/Sao_Paulo.
- **Status Overdue e filtro de status:** usavam o dia UTC (`DashboardDtos.EffectiveStatus` e `TransactionsController`). Uma conta que vence hoje virava "Atrasado" às 21h de Brasília.
- **Vencimento do cartão manual:** usava o dia UTC (`CreditCardSummaryBuilder`). Rolava para o mês seguinte uma noite antes.
- **Avanço de parcelas:** usava o dia UTC (`InstallmentProgress`). Avançava, mexendo em `UsedLimit`, às 21h do dia do vencimento.

## Testes (`backend.Tests`)

- **Stack:** xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1 e EF Core Sqlite 8.0.4 (só no projeto de testes). Usa o `AppDbContext` e o `EncryptionService` reais.
- **Cobertura, 144 testes:**
  - B1, parcelas e limite (20);
  - controller de parcelas (22);
  - resumo de cartões e fatura (15);
  - dashboard (20);
  - status e datas (11);
  - fronteira do dia de Brasília (6);
  - B2, relatório (20);
  - criptografia e centavos (15);
  - transferências (7).
- **Cálculos errados revelados (testes falhando, aguardam decisão):**
  1. **Contagem do relatório com busca ou ordenação por valor/nome:** fica em 2000 quando há mais linhas. Exemplo: 2300 que casam com a busca, mas `TotalCount` = 2000. Os totais somam tudo, então contagem e totais divergem na tela (`TransactionsController.GetReport`, ramo em memória).
  2. **Parcela no dia 31:** depois de cair em fevereiro, fica presa no dia 28/29 para sempre (`InstallmentProgress`, `AddMonths` sobre a data já ajustada). A correção exige guardar o dia original, ou seja, uma coluna nova e uma migração.
- **Diferenças SQLite × Postgres que podem afetar:**
  - ordenação de texto no SQL (BINARY × collation; o caminho em memória não é afetado);
  - `DateTime` devolvido como `Unspecified`;
  - o SQLite não reproduz as exceções de concorrência e de unicidade do Npgsql.
- **Limitação:** não há ponto de injeção de relógio no `BrazilClock`, então a janela 21h–23h59 não é simulada de forma determinística.

## Investigador: encontrado e não corrigido (precisa de aprovação)

- **Cartão sincronizado:** o app soma e subtrai parcelas no `UsedLimit` que vem do banco (Polp). Isso conta em dobro entre um sync e outro, e a correção B1 também atua nesses cartões. **Recomendação:** não ajustar o `UsedLimit` de contas sincronizadas.
- **`Balance` não nulo:** conta sem saldo e cartão da Polp mostram "R$ 0,00", e o Dashboard rotula isso como "Saldo do cartão (informado pelo banco)". **Recomendação:** `Balance` anulável (migração) e "—" no front.
- **`GET /transactions` com teto de 2000:** corta os lançamentos mais antigos sem aviso, e a tendência de 6 meses, Receitas e despesas e Categorias (ano) somam menos do que deveriam. **Recomendação:** endpoints de agregado no servidor, ou pelo menos um campo `truncated`.
- **Fatura atual do cartão manual:** é a soma de todas as parcelas ativas, sem olhar vencimento. **Recomendação:** definir a regra da fatura.
- **Tendência e taxa de poupança do Dashboard:** vêm da lista parcial e comparam o mês inteiro com o anterior até o mesmo dia. **Recomendação:** o servidor devolver o comparativo.
- **"vs início do mês":** é um número reconstruído, não um saldo real. **Recomendação:** remover.
- **Subtotal diário do relatório:** calculado só com a página atual.
- **Escala decimal:** valores com 3 ou mais casas são arredondados em silêncio ao gravar ("F2"). **Recomendação:** validar 2 casas no DTO.
- **Parcelas sem validação:** não há conferência de Total × Parcela × N.
- **`PUT` de conta:** permite sobrescrever o saldo de conta sincronizada.
- **Próximos vencimentos:** fatura e parcela aparecem sempre "Pendente", e a mesma dívida pode aparecer até 3 vezes.
- **Percentuais:** as fatias somam 99–101%.
- **Categorias:** o texto "todas as contas conectadas" não bate com o filtro, e a regex de parcela trata datas como parcela.
- **Polp (para a reescrita):**
  - `DueDate` com horário pode cair no dia ou mês seguinte;
  - data inválida vira `UtcNow` e "anda" a cada sync;
  - `CurrentInvoiceAmount` de uma fatura gravado com a data de outra;
  - pagamento de fatura e estorno entram como receita.

## Totais calculados no frontend (pré-requisito para paginação)

Fonte parcial = `GET /transactions` (teto de 2000).

| Onde | O que soma | Fonte |
|---|---|---|
| `utils/monthlyTrend.js` (`buildMonthlyTrend`, `buildPreviousMonthSamePeriod`) | receita e despesa por mês (6 meses) e mês anterior até hoje | parcial |
| `DashboardHome.jsx` | saldo do mês, % gasto, taxa de poupança, tendências, "início do mês", "Outros" do donut | misto: `/dashboard` completo e `/transactions` parcial |
| `ReceitasDespesas.jsx` | totais do mês e do anterior, por categoria (valor, contagem, %), concentração, resultado | parcial |
| `Categorias.jsx`, `utils/cutImpact.js` | por estabelecimento, por categoria no ano, total, %, simulação de corte | parcial |
| `Relatorios.jsx` | subtotal diário (só a página), total de páginas, saldo | totais do servidor; subtotal parcial |
| `utils/cardGroups.js` | limite usado, disponível, faturas abertas, próximo vencimento | `/credit-cards/summary` completo |
| `Cartoes.jsx` (`buildUpcoming`) | próximos vencimentos | parcial |
| `Investimentos.jsx` | total por instituição e geral, progresso de metas | `/polp/integrations` completo e localStorage |
| `CategoryDonutChart`, `CreditCardVisual`, `BreakdownRows` | % da legenda, % de uso, contagem de parcelas | derivado do que recebem |

---

# Rodada de decisões (2026-11-01, segunda parte)

## Verificação final

| Comando | Resultado |
|---|---|
| `dotnet build --no-incremental` | 0 avisos, 0 erros |
| `dotnet test` (backend.Tests) | 183 testes, 183 passam |
| `npm run lint` | limpo |
| `npm run build` | OK |

O stage do git não foi alterado. O harness de equivalência foi apagado: estava fora do repositório e não gravou dados, porque abortou antes de ler o banco. Nenhum comando conectou ao banco nesta rodada.

## 1. Revertido e mantido

**Revertido para a versão do stage:** as reduções de consulta da rodada de troca de abas.
- **Dashboard:** contas voltam a ser carregadas duas vezes, e as transações voltam a vir como entidade com `Include` no lugar das projeções. As categorias voltam a vir de consulta própria, e a conta padrão volta a ser escolhida sem `OrderBy`.
- **Resumo de cartões:** volta a assinatura sem `accounts` e as consultas mesmo sem cartão sincronizado.
- **Relatório:** volta o `COUNT` direto.
- **`CreditCardsController`:** idêntico ao stage.

**Mantido:**
- todas as correções de bugs (inclusive B1 e B2);
- a remoção dos caches;
- o `.Include(i => i.BankAccount)` no `CreditCardSummaryBuilder`;
- os esqueletos e o cancelamento de requisições;
- `BrazilClock` no lugar de UTC;
- o cache de 10 min do catálogo global de bancos.

## 2. Migrações novas

**`20261101130000_AddPerformanceIndexes`** (SQL bruto)
```
Up:   CREATE INDEX IF NOT EXISTS "IX_Transactions_UserId_BankAccountId_DueDate" ON "Transactions" ("UserId","BankAccountId","DueDate");
      CREATE INDEX IF NOT EXISTS "IX_BankAccounts_UserId_IsActive" ON "BankAccounts" ("UserId","IsActive");
      CREATE INDEX IF NOT EXISTS "IX_Installments_UserId" ON "Installments" ("UserId");
      CREATE INDEX IF NOT EXISTS "IX_Installments_BankAccountId" ON "Installments" ("BankAccountId");
Down: DROP INDEX IF EXISTS dos mesmos quatro.
```
- O `IX_BankAccounts_UserId` foi mantido (`HasIndex` explícito). O snapshot e o Designer declaram todos os índices, e `dotnet ef migrations has-pending-model-changes` não acusa nenhuma diferença.
- **Atenção:** `IX_Installments_BankAccountId` já existia (migração `BankAccountIsTheCard`). No `Up` ele é inofensivo por causa do `IF NOT EXISTS`, mas um rollback desta migração o removeria. Manter como foi pedido ou tirar essa linha do `Down`: decisão sua.
- `CREATE INDEX` sem `CONCURRENTLY` bloqueia escritas nessas tabelas durante a criação, no startup.

**`20261101140000_AddInstallmentDueDay`**
```
Up:   AddColumn<int>("DueDay", "Installments", type: "integer", nullable: true)   (sem default, sem UPDATE)
Down: DropColumn("DueDay", "Installments")
```

## 3. Correções

| Correção | Arquivo | O que mudou | Testes |
|---|---|---|---|
| Contagem do relatório | `Controllers/TransactionsController.cs` (`GetReport`) | O `totalCount` é calculado sobre o filtro inteiro. No ramo em memória (busca, ordenação por valor ou nome), o teto de 2000 saiu: todas as linhas são materializadas, contadas, ordenadas, paginadas e somadas sobre o mesmo conjunto. Custo: memória proporcional ao filtro (centenas de bytes por linha). | `Quantidade_total_com_busca_conta_todas_as_2300_linhas_que_casam_acima_do_teto`, `Busca_com_2300_linhas_pagina_21_alem_de_2000_retorna_100_itens`, `Busca_com_2300_linhas_ultima_pagina_23_tem_100_e_pagina_24_vazia_sem_repetir_ids`, `Ordenar_por_valor_com_2300_linhas_fica_crescente_entre_todas_as_paginas_e_total_28738_50`, `Ordenar_por_valor_decrescente_com_2300_linhas_a_pagina_1_traz_os_100_maiores` |
| Parcela no dia 31 | `Models/Installment.cs`, `Controllers/InstallmentsController.cs`, `Services/InstallmentProgress.cs` | `DueDay` gravado na criação. O avanço usa `min(DueDay, dias do mês)` e volta ao dia original. Com `DueDay` nulo (linhas antigas), nada muda. | `DueDay_avanca_para_min_do_dia_original_e_dias_do_mes_voltando_ao_dia_original`, `Sequencia_31_jan_29_fev_31_mar_30_abr_31_mai_de_2024_calculada_a_mao`, `DueDay_31_com_data_gravada_clampada_volta_ao_fim_de_mes_correto`, `DueDay_30_em_fevereiro_cai_no_ultimo_dia_e_volta_a_30_em_marco`, `DueDay_nulo_mantem_o_comportamento_antigo_sem_recuperar_o_dia_31`, `Parcela_dia_31_gravada_como_fim_de_fevereiro_volta_a_ser_fim_de_mes_nos_meses_de_31`, `Criar_grava_DueDay_igual_ao_dia_da_primeira_data_de_vencimento` |
| Limite de cartão sincronizado | `Controllers/InstallmentsController.cs`, `Services/InstallmentProgress.cs` | Conta com `PolpAccountId`: o `UsedLimit` não é alterado na criação, exclusão, avanço ou quitação, e a validação de limite excedido é pulada na criação (o valor do banco já inclui as compras). A B1 vale só para cartão manual e para conta sem `PolpAccountId`. | `Cartao_sincronizado_Polp_avanca_a_parcela_mas_nao_altera_o_usado_1200`, `Cartao_sincronizado_Polp_quitado_remove_a_parcela_e_nao_altera_o_usado_700`, `Lado_a_lado_manual_e_conta_sem_PolpAccountId_aplicam_B1_e_sincronizado_nao`, `Cartao_sincronizado_Polp_criar_nao_altera_o_usado_e_pula_validacao_de_limite_excedido`, `Cartao_sincronizado_Polp_com_usado_nulo_continua_nulo_apos_criar`, `Cartao_sincronizado_Polp_excluir_nao_altera_o_usado`, `Lado_a_lado_criar_12x100_manual_soma_1200_e_sincronizado_fica_igual` |
| Saldo de cartão da Polp | `DTOs/DashboardDtos.cs` (`BankAccountDto.Balance` → `decimal?`), `Controllers/BankAccountsController.cs`, `Controllers/PolpController.cs`; frontend `formatCurrency` | Cartão sincronizado devolve `balance: null` (a Polp nunca informa saldo de cartão), sem migração e sem alterar dados. Conta manual com 0 continua 0. A tela mostra "—". | `Saldo_de_conta_manual_com_zero_e_zero_e_nao_nulo`, `Saldo_de_conta_manual_com_valor_e_o_valor_real`, `Saldo_de_cartao_sincronizado_Polp_e_nulo_mesmo_gravado_como_zero`, `BankAccountDto_FromEntity_manual_devolve_saldo_e_sincronizado_devolve_nulo` |
| Agregados no servidor | `Controllers/TransactionsController.cs` (novo `GET /api/transactions/aggregates`), `DTOs/DashboardDtos.cs`; frontend `DashboardHome`, `ReceitasDespesas`, `Categorias`, `utils/monthlyTrend.js`, `utils/trend.js`, `api/transactions.js` | Tendência de 6 meses, mês anterior até o mesmo dia, totais do mês e do ano, categorias (valor, contagem, %), estabelecimentos (port exato do agrupamento do frontend) e categoria de maior gasto são calculados no servidor sobre o período inteiro, em streaming e sem teto. A lista de `/transactions` mantém o teto e mostra "Mostrando os 2.000 lançamentos mais recentes." em Receitas e despesas, Categorias e Cartões quando o teto é atingido. | `AggregatesTests` (13 testes): `Totais_da_conta_A_com_mais_de_2000_lancamentos_...` (2500 despesas de 1,10 = 2750,00), `Meses_do_periodo_incluem_fevereiro_zerado_...`, `From_e_inclusivo_e_to_e_exclusivo`, `Transferencias_proprias_ficam_fora_dos_totais_e_dos_meses`, `Filtro_pela_conta_B_traz_so_os_77_de_fevereiro`, `Categorias_somam_valor_e_contam_lancamentos_por_tipo`, `Estabelecimentos_agrupam_parcelas_com_sufixo_dd_mm_como_o_frontend` e outros |

O investigador comparou a lógica dos agregados com o cálculo antigo do frontend. Os filtros, a exclusão de transferências, o "Sem categoria", os limites de mês e de ano e a regex de parcela são iguais. A única diferença numérica é o fim do teto de 2000, que era o objetivo, e a soma agora é em `decimal` em vez de `double`.

## Pendências (precisam de decisão)

- **Fuso por usuário (item 8, não implementado):** hoje "hoje" e "mês atual" usam Brasília (`BrazilClock`). O correto é usar o fuso de cada usuário, porque o Brasil tem quatro fusos e Brasília não serve para todos.
- **Listas de detalhe:** os lançamentos de uma fonte de receita e as visitas de um estabelecimento ainda vêm de `/transactions` (teto de 2000). Em mês ou ano antigo de uma conta grande, a lista pode vir menor do que a contagem do agregado, mas o aviso aprovado aparece nesse caso. **Recomendação:** buscar o detalhe pelo relatório paginado, filtrado por período.
- **Parcelamentos antigos:** ficam com `DueDay` nulo e mantêm o problema do dia 31. Não há como saber com segurança quais já foram ajustados para 28/29.
- **Conta corrente da Polp sem saldo no primeiro sync:** grava 0 e mostra "R$ 0,00". **Recomendação:** `Balance` anulável na coluna (migração só de nulabilidade) e o sync gravar nulo quando o banco não informar.
- **"vs início do mês":** é um saldo derivado, não real. **Recomendação:** remover.
- **`Down` da migração dos índices:** remove `IX_Installments_BankAccountId`, que já existia antes dela.
- **Seguem abertos da rodada anterior:**
  - fatura atual do cartão manual (soma todas as parcelas ativas);
  - validação de Total × Parcela × N;
  - escala de 2 casas;
  - heurística de transferência própria;
  - `PUT` sobrescrevendo saldo de conta sincronizada;
  - pontos da Polp para a reescrita.

# Correções finais (2026-11-01, terceira parte)

- Contas da Polp já gravadas com saldo 0 antes da migração `20261101150000_MakeBankAccountBalanceNullable` continuam mostrando 0, porque não dá para distinguir "0 real" de "não informado" sem alterar dados. Só as contas criadas a partir de agora sem saldo ficam com "—".
- O `Down` dessa migração recoloca `NOT NULL` com default vazio. Um rollback deixaria vazio o saldo de contas sem saldo informado.
