# Cores semânticas — Control Finance

Refinamento realizado com a skill Impeccable, no modo Operate. A arquitetura de CSS por componente e os tokens globais em `frontend/src/index.css` foram mantidos. CPF e CNPJ compartilham os mesmos significados; não houve redesenho de layouts.

## Hierarquia cromática da identidade

- Preto e grafite predominam nos fundos e superfícies existentes: `--color-bg`, `--color-bg-alt`, `--color-surface`, `--color-surface-alt` e `--color-surface-hover`.
- Branco é a referência das informações principais e da distribuição de categorias. `--color-text-primary` permanece branco; `--color-text-secondary` passa a `#c6c6c6` e `--color-text-tertiary` a `#9b9b9b`, melhorando a leitura dos níveis secundários sem equipará-los aos valores principais.
- Divisórias e contornos usam `--color-border: #303030` e `--color-border-strong: #424242`, dando definição à estrutura escura sem criar novas superfícies, sombras ou seções.
- Laranja permanece restrito aos usos existentes de marca, navegação ativa, ações principais e foco. Nenhum novo destaque laranja foi acrescentado.
- Verde, vermelho, âmbar e todos os tokens semânticos, incluindo fundos de estado e a escala de despesas, são preservados integralmente. A identidade preta/branca/laranja não substitui informação financeira funcional nem as cores identificadoras de bancos e estabelecimentos.

## Tokens

| Papel | Tokens | Uso |
| --- | --- | --- |
| Marca | `--color-accent`, `--color-accent-strong`, `--color-accent-soft` | Laranja existente preservado para navegação, seleção, foco, links e ações principais. |
| Favorável | `--color-positive: #82c6a4`, `--color-income`, `--color-success` | Receitas, rendimentos positivos, comparações favoráveis e confirmações. |
| Desfavorável | `--color-negative: #e58a8a`, `--color-expense`, `--color-error` | Despesas, perdas, custos, atrasos, erros e limites excedidos. |
| Atenção | `--color-warning: #d6b16d` | Pendências que exigem atenção e alertas preventivos. Faturas pendentes dentro do prazo usam apresentação neutra. `--color-amber` e `--color-amber-soft` são aliases de compatibilidade. |
| Neutro | `--color-neutral: #a4a9b2` | Dados sem direção financeira, modalidades de investimentos e agrupamento “Outros”. Texto branco/cinza existente preserva a hierarquia dos valores neutros. |
| Fundos | `--color-positive-bg`, `--color-negative-bg`, `--color-warning-bg`, `--color-neutral-bg` | Fundos discretos para estados. O fundo negativo usa 12% para preservar contraste do texto. |
| Ação destrutiva | `--color-danger-strong: #a83e49` | Variante `danger` de Button e confirmações de remoção/exclusão. |
| Distribuição de despesas | `--color-chart-expense-1` a `--color-chart-expense-6` | Roscas em vermelhos de luminosidades diferentes, com nomes, valores, percentuais e legendas preservados. |
| Identificação de categorias | `--color-category-1` a `--color-category-8` | Branco e cinzas acromáticos, alternando luminosidade; identificar um cadastro não significa receita ou despesa. |

`chartTheme.js` referencia as variáveis CSS para séries, eixos, grades, cursores, tooltips e paletas. Os gráficos não mantêm cópias hex dos tokens. Cores de bancos, cartões e marcas de estabelecimentos continuam identificando essas instituições, sem representar resultado financeiro.

## Inconsistências encontradas e correções

| Área | Problema anterior | Correção |
| --- | --- | --- |
| Dashboard inteligente CPF/CNPJ | Despesas e tendências negativas em laranja; séries com hex próprios; resultado zero positivo. | Tokens financeiros, saldo e resultado por sinal, zero neutro, taxa de poupança por sinal e despesas com traço distinto. Percentual de receita gasta acima de 100% em vermelho. |
| Cartões | Limites excedidos e atrasos em laranja; faturas e saídas sem distinção consistente. | Fatura atual e limite usado dentro do limite neutros. Faturas individualmente atrasadas, vencimentos atrasados e limites excedidos vermelhos. O total agregado das faturas fica neutro: o atraso de um cartão não qualifica a soma de todos. Próximos vencimentos dentro do prazo e seus selos “Pendente” neutros. Compras realizadas continuam seguindo a semântica de despesa no extrato; créditos e estornos seguem a de receita. Padrão hachurado, rótulos e critérios existentes preservados. |
| Receitas e despesas / Fluxo de Caixa | Laranja nas barras, despesas e resultado negativo. | Verde para entradas, vermelho para saídas e resultados por sinal; mesma regra nos recortes de CPF e CNPJ. |
| Relatórios e transações | Despesas neutras em linhas/detalhes, totais negativos em laranja e zero positivo. | Valores por tipo e resultado por sinal; transferências próprias e valores zero neutros. Sinais e identificação das transferências preservados. O selo “Pago” continua discreto no extrato. |
| Categorias | Roscas exclusivamente na cor da marca; barras e gastos de estabelecimentos em laranja. | Distribuição de despesas em vermelhos; valores e barras de despesas corrigidos. Dados de cor persistidos não são alterados. |
| Investimentos | Verde usado também para capital, posição e controles; juros de financiamento em laranja; rendimento sem sinal semântico. | Posição/capital neutros, rendimentos por sinal, impostos/taxas e juros pagos em vermelho; foco/controles na cor da marca. Projeções mantêm legendas e traçados distintos. |
| Folha de pagamento | “Ativo” reutilizava o estado de pagamento; “Desligado” reutilizava pendência. | Situações cadastrais neutras. Salários brutos e total contratual em vermelho como custos, mantendo seus rótulos e sem indicar pagamento realizado. |
| Fornecedores | Erros e ações de exclusão usavam a cor da marca. | Erros vermelhos e variante destrutiva. Contagens, dados cadastrais e dados de pagamento continuam neutros. |
| Estados compartilhados | Atrasado em âmbar; erro associado à marca; comparações inalteradas pareciam favoráveis/desfavoráveis. | Atrasado vermelho, pendente âmbar por padrão e neutro no contexto de cartões, estado desconhecido neutro, erro vermelho, variação exibida como zero neutra. |
| Login, cadastro, conexão, privacidade | Mensagens de erro e exclusões compartilhavam laranja. | Erros e exclusões com tokens próprios, mantendo ações normais e foco na marca. |

## Regras de implementação

- `financialTone` escolhe somente a apresentação do sinal do valor exibido. Não modifica dados, arredondamentos financeiros ou cálculos existentes.
- `transactionTone` recebe o tipo e a indicação de transferência já existentes. Não classifica transações; dados sem direção conhecida permanecem neutros.
- O estado neutro de tendências usa a precisão já exibida (uma casa decimal). Percentuais, direção das setas e inversão favorável de despesas não foram recalculados.
- Nenhuma alteração em backend, endpoints, banco de dados ou integração Polp v2. Nenhum commit ou push.
- Alterações locais anteriores foram preservadas. A comparação da tarefa foi feita contra uma cópia do código no início do trabalho.

## Validação técnica

- `npm run lint` e `npm run build`.
- `node --test scripts/tests/*.test.mjs`: sete testes, incluindo os dois testes de períodos existentes.
- Cálculo automatizado de contraste: textos semânticos acima de 4,5:1 nas superfícies escuras e nos fundos de estado; séries de despesas acima de 3:1; texto branco da ação destrutiva acima de 4,5:1.
- Cobertura de zero exibido, valores ausentes, transferências próprias, tipos desconhecidos, comparação de despesas e compatibilidade de apresentação de categorias antigas.
- A conferência visual final é responsabilidade exclusiva do usuário, conforme preferência permanente em `AGENTS.md`. As verificações finais usam somente código, lint, build e testes sem navegador.

## Refinamento pontual de tonalidade e hierarquia

- Verde de `#76b99a` para `#82c6a4` e vermelho de `#df7c82` para `#e58a8a`: discretamente mais claros para destacar os valores no tema escuro. Aliases, fundos, semântica e tokens laranja preservados; nenhum token novo.
- Cartoes, CardDetail e CreditCardVisual aplicam vermelho à fatura somente quando `isInvoiceOverdue` confirma atraso. Limites continuam seguindo `cardLimits`, sem novos limiares ou critérios financeiros.
- StatusPill aceita `pendingTone="neutral"` em Cartoes, CardDetail, CardStatement e CardOverview. O rótulo “Pendente” permanece; pendência dentro do prazo não recebe aparência de alerta. Outros contextos preservam o padrão de atenção existente.
- O resumo narrativo de Receitas e Despesas CPF e Fluxo de Caixa CNPJ deixa de repetir receitas, despesas e resultado em reais. Mantém o percentual não gasto (informação complementar), a interpretação do resultado negativo e o estado sem movimentações.
- Dashboard e Relatórios recebem apenas o refinamento dos tokens financeiros compartilhados: hierarquia, valores, legendas, sinais e avisos de escopo/recência permanecem. Não foram identificadas no código razões para reorganizar essas telas.
- Verificações não visuais: lint, build e os sete testes existentes passaram, inclusive o cálculo de contraste nas superfícies e fundos semânticos. Nenhum navegador, servidor ou teste visual foi executado neste refinamento.

## Principais categorias — Dashboard CPF e CNPJ

O Dashboard usa uma escala de branco e cinzas própria: `--color-chart-category-neutral-1` a `--color-chart-category-neutral-4` (`var(--color-text-primary)`, `#8a8a8a`, `#d8d8d8`, `#bdbdbd`). A primeira fatia usa branco para recuperar presença visual; as demais alternam luminosidade para separar fatias vizinhas, sem destaque de alerta. O agrupamento “Outros” preserva `--color-neutral`, e o espaçamento existente entre as fatias continua demarcando seus limites.

`NEUTRAL_DONUT_SHADES` é passada como `palette` somente à rosca “Principais categorias” do DashboardHome, compartilhado por CPF e CNPJ. CategoryDonutChart usa o mesmo resultado de `donutSliceColors` nas fatias, nos marcadores da legenda e nas barras. Os demais consumidores preservam a paleta anterior por padrão.

O valor central continua em `--color-text-primary`, com peso e tamanho preservados. Nomes, valores, percentuais, agrupamento, filtros e cliques permanecem iguais. Foco e hover existentes fornecem o destaque de interação; nenhuma categoria recebe uma cor de alerta. Tokens globais de receitas/despesas e suas aplicações não foram alterados. Os demais gráficos mantêm suas séries e ganham apenas a legibilidade dos tokens compartilhados de texto e grade.

Validação deste ajuste: revisão de código, lint, build e cálculo de contraste dos tons neutros (mínimo 3:1 sobre as superfícies escuras). Sem navegador ou testes visuais.

A identificação neutra de categorias no restante do produto também usa `--color-category-1` a `--color-category-8` em branco e cinzas sem matiz. Esses tokens continuam sendo somente de apresentação: nomes, cores persistidas, valores, percentuais e classificação não mudam. O ajuste da identidade foi centralizado nos tokens existentes, sem mudança de componentes, layouts ou regras financeiras.
