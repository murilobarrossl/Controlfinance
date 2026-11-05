---
target_identity: "file:/Users/igor/Desktop/workspace/projetos/Controlfinance/frontend/src/pages/dashboard/Cartoes"
timestamp: 2026-11-05T12-39-34Z
slug: frontend-src-pages-dashboard-cartoes
closed: true
---
---
target: aba Cartões (/dashboard/cartoes)
total_score: 18
max_score: 40
na_heuristics: 
p0_count: 1
p1_count: 5
---
# Critique — aba Cartões
DEGRADED: single-context (economia pedida pelo usuário). Browser com dados simulados (1440/390; cheio, vazio, erro, carregando) + detector.

## Leitura de valores
1. [P0] Limite estourado lido como 100%: CreditCardVisual.jsx:14 limita a 100 (Itaú 3.412,55 de 3.000,00 → "100%"), sem aviso de acima do limite.
2. [P1] Fatura vencida aparece "Pendente" (upcomingDues.js status fixo) e some do "Próximo vencimento" (cardGroups.js filtra date >= hoje).
3. [P1] Cartão manual rotulado "Banco conectado" com iniciais BC no seletor (Cartoes.jsx:255 fallback).
4. [P1] Estado vazio mostra R$ 0,00 em limite usado/disponível/faturas (summarizeCards de lista vazia).
5. [P1] Erro de carga troca a página por uma linha: sem título, sem tentar de novo, sem role=alert (Cartoes.jsx:350).
6. [P1] Remover parcelamento: botão "×" sem confirmação nem desfazer (Cartoes.jsx:617).
7. [P2] Um cartão sem limite zera o agregado para "—" sem explicar por quê.
8. [P2] Mesma fatura com 3 nomes (Faturas abertas / Fatura aberta / Fatura atual) e repetida 3-4 vezes; "Vence dia X" (dueDay) ao lado de "Próximo vencimento" (invoiceDueDate) podem divergir.
9. [P2] Resumo sem data da informação; "Atualizado em" só por banco; intro do extrato fala em "última sincronização" sem data.
10. [P2] Celular: Próximos vencimentos trunca nomes ("Conta d...", "Fatura...") e espreme status + valor.
## Estético / redundância
11. [P2] Cartão visual: inclinação 3D, brilho, ruído, chip, nome em CAIXA ALTA; ocupa ~1/3 da largura, o resto vazio; "Sincronizado via Polp" no lugar da bandeira.
12. [P2] Escopo repetido: "Os números abaixo mostram" + linha igual em 3 StatCards; seletor de escopo e abas do extrato com o mesmo visual e funções diferentes.
13. [P2] Texto de sistema: intro + "Sobre os dados desta página" (jargão Polp, "ponto de integração pra evoluir").
14. [P2] Ordem: Próximos vencimentos abaixo do extrato; formulário de cartão manual sempre aberto, parcelamento recolhido.
15. [P3] Detector: side-tab Cartoes.css:338 (3px), border-top 4px Cartoes.css:44; CAIXA ALTA em chips e rótulos de grupo; "1 cartão cadastrados".
