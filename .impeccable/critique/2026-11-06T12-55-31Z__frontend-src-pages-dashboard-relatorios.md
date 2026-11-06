---
target: aba Relatórios
total_score: 19
max_score: 40
na_heuristics: 
p0_count: 0
p1_count: 4
target_identity: "file:/Users/igor/Desktop/workspace/projetos/Controlfinance/frontend/src/pages/dashboard/Relatorios"
timestamp: 2026-11-06T12-55-31Z
slug: frontend-src-pages-dashboard-relatorios
closed: true
---
# Critique — aba Relatórios
DEGRADED: single-context (usuário pediu sem agentes), código + detector, sem navegador (sem automação; aba exige login). Nota 19/40 (Ruim).

## Heurísticas
1 Visibilidade 2 (sem data de sincronização) · 2 Linguagem 2 ("Saldo (todo o período)" ≠ saldo da conta) · 3 Controle 2 (sem limpar filtros/"Todos", edição sem Esc) · 4 Consistência 2 (laranja = despesa = erro = atrasado = chip ativo; subtítulo "Histórico completo" no modo mês) · 5 Prevenção 2 (nome vazio ignora em silêncio, ✓ sem bloqueio) · 6 Reconhecimento 2 (dicas só em title) · 7 Eficiência 1 (th sem teclado, sem Enter/Esc, sem salto de página) · 8 Estética 2 (peso uniforme, linha de grupo quebrada) · 9 Erros 1 (erro de carga substitui a página sem retry; erro de salvar longe da linha, sem role=alert) · 10 Ajuda 3.

## Especificidade
Extrato genérico de admin; identidade só em agrupamento por dia, etiqueta de categoria e transferência em 2 linhas. Falta "explicar em vez de despejar". Detector: 0 achados.

## Prioridades
1. [P1] Subtotais por dia errados: groupByDate (Relatorios.jsx:74) soma só a página; dia cortado entre páginas fica parcial; ordenação ≠ data fragmenta grupos. Fix: agrupar só com sort por data; subtotal do servidor ou aviso "continua". /impeccable harden
2. [P1] Linha de grupo: td com display:flex (Relatorios.css:297) ignora colSpan, provável grupo preso na 1ª coluna (inferido, não visto). Fix: div flex dentro do td. /impeccable layout
3. [P1] Indicadores sem significado nem recência: "Saldo" ambíguo, totais misturam pendentes sem dizer, sem data de sincronização (Cartões tem), barra de totais sem peso. /impeccable clarify
4. [P1] Valor sem sinal (Relatorios.jsx:449), despesa = --color-accent (#ed4a31, mesma cor de erro/atrasado/chip ativo), coluna Tipo redundante. Fix: sinal no valor, remover Tipo, separar cor de despesa. /impeccable colorize
5. [P2] Filtros/ordenação: chips sem aria-pressed, sem "Todos"/limpar, th onClick sem button/aria-sort, busca sem label, outline none; não existe filtro por categoria/conta. /impeccable harden

## Personas
Alex: ordenar sem teclado, edição sem atalhos, paginação só ‹ ›. Sam: valor sem sinal, grupo não anunciado, botões 26px, erros sem role=alert, explicação só em title. Pessoa leiga: "Saldo" ≠ banco, subtotais repetidos ao ordenar por valor, sem recência.

## Menores
Subtítulo fixo; rodapé "Totais" só conta; estado vazio sem causa/limpar e "transação" vs "lançamento"; inputs de edição com fundo de cor de borda; 8 colunas no celular; sem gráfico (correto para extrato).

## Perguntas
Abrir com frase-resumo? Status faz sentido p/ extrato do banco? Padrão "Por mês"?
