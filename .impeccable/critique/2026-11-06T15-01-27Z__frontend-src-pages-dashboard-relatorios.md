---
target: aba Relatórios
total_score: 29
max_score: 40
na_heuristics: 
p0_count: 0
p1_count: 1
target_identity: "file:/Users/igor/Desktop/workspace/projetos/Controlfinance/frontend/src/pages/dashboard/Relatorios"
timestamp: 2026-11-06T15-01-27Z
slug: frontend-src-pages-dashboard-relatorios
closed: true
---
# Critique — aba Relatórios (2ª rodada)
DEGRADED: single-context (usuário pediu sem agentes), código + detector + capturas de harness, sem aba logada. Nota 29/40 (Bom), antes 19/40.

## Heurísticas
1 Visibilidade 3 (recarga troca a tabela por um bloco cinza) · 2 Linguagem 3 (edição mostra "Tipo|Titular" cru) · 3 Controle 3 · 4 Consistência 3 (Atrasado âmbar só aqui; ‹ › glifos) · 5 Prevenção 2 (edição pode apagar categoria) · 6 Reconhecimento 3 (busca sem rótulo visível) · 7 Eficiência 3 (sem salto de página) · 8 Estética 3 · 9 Erros 3 (erro de carga esconde o seletor de período) · 10 Ajuda 3.

## Especificidade
Extrato que explica os números (nota das somas, "Fora do filtro", subtotal do servidor). Detector: 0 achados.

## Prioridades
1. [P1] getCategories falha em silêncio; startEdit sem match deixa categoryId "" → salvar só o nome envia null e o backend apaga a categoria (TransactionsController.cs:504). Fix: bloquear ✎ até categorias carregarem, erro com retry, não salvar se a categoria atual não está na lista. /impeccable harden
2. [P2] Edição de transferência mostra "Transferência Recebida|Maria Souza" cru; apagar após a barra perde o titular. Fix: editar só a parte principal e preservar "|Titular". /impeccable harden
3. [P2] Toda recarga (salvar, página, filtro) troca as linhas por um Skeleton de 8rem; altura despenca e rolagem pula. Fix: manter linhas esmaecidas com aria-busy; só totais/subtotais em esqueleto. /impeccable harden
4. [P3] Atrasado âmbar só aqui; ‹ › glifos; hífen em vez de "−"; erro de carga esconde o seletor de período. /impeccable polish

## Personas
Alex: tabela colapsa a cada correção; sem salto de página. Sam: recarga sem aria-busy; busca sem rótulo visível. Pessoa leiga: categoria some ao editar nome com conexão instável; apaga "|Maria Souza" achando que é sujeira.

## Menores
Espaço do SectionHeading compartilhado; rótulo "Entradas − saídas em …" quebra no celular; foco laranja = convenção compartilhada.

## Perguntas
Padrão "Por mês"? Corrigir categoria oferecer aplicar ao estabelecimento?
