---
target: painel logado (exceto Investimentos)
total_score: 24
max_score: 40
na_heuristics: 
p0_count: 0
p1_count: 4
target_identity: "file:/Users/igor/Desktop/workspace/projetos/Controlfinance/frontend/src/pages/dashboard"
timestamp: 2026-11-05T11-53-40Z
slug: frontend-src-pages-dashboard
---
# Critique — painel logado (exceto Investimentos)
DEGRADED: single-context, code + detector only, no browser. Score 24/40 (estimated).

## Data/compliance
1. [P1] No data recency on Dashboard inteligente and Receitas e despesas (balance DashboardHome.jsx:292, month totals).
2. [P1] Charts without text alternative (AreaTrendChart no role/label/table; donut bars aria-hidden).
3. [P1] Errors not announced (no role=alert/aria-live in 7 screens).
4. [P2] Three "saldo" meanings side by side on home (conta corrente, saldo do mês, vs início do mês).
5. [P2] Row amounts without sign (Relatorios.jsx:450, CardStatement.jsx:188) rely on color/type column.
6. [P2] tabular-nums missing in StatCard and BreakdownRows.
7. [P3] CardStatement.jsx:141 `?? 0` shows R$ 0,00 on empty non-error report.
8. PF/PJ same-login open decision.

## Aesthetic
1. [P1] SectionHeading rewritten for landing silently drops dashboard kickers and restyles 6 tab headings (unintended side effect).
2. [P2] Generic: side-tab borders (Categorias.css:361, Cartoes.css:338, Cartoes.css:44), icon+label+value stat grid, greeting title, uppercase in 6 components, ✓ ✎ glyph icons.
3. [P2] Inconsistent with landing/login: 20 px breakpoints vs em, 18 hex + 11 rgba hard-coded, fragmented small type scale, no h1 on tabs.
