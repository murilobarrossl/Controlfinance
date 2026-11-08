---
target: aba Investimentos
total_score: 23
max_score: 40
na_heuristics: 
p0_count: 1
p1_count: 3
target_identity: "file:/Users/igor/Desktop/workspace/projetos/Controlfinance/frontend/src/pages/dashboard/Investimentos"
timestamp: 2026-11-08T14-59-08Z
slug: frontend-src-pages-dashboard-investimentos
---
⚠️ DEGRADED: single-context (usuário pediu para não usar agentes)

Score 23/40 (Aceitável). H1 2 sem data de sincronização; H2 3; H3 2 excluir meta/zerar reserva sem confirmação; H4 2 dois CDIs, legenda duplicada, poupança 0,5% fixo; H5 2 investimento recebe taxa de caixinha; H6 3; H7 2 saldo real não alimenta simulador/reserva; H8 2 sete seções empilhadas; H9 2 erro sem retry; H10 3.

Detector: 0 achados. Checagem manual: 20 font-size 0.68–0.78rem; cores fixas nos gráficos; botão × 24px.

Prioridades:
- [P0] BankYieldCards.jsx:103-105,162 soma contas "investimento" (CDB/Tesouro/fundos) em "Guardado aqui" e aplica a % do CDI de cada produto de caixinha ao saldo inteiro, linha a linha. Valor errado apresentado como "sobre o seu saldo". Fix: só caixinha/porquinho; senão "a cada R$ 1.000". (/impeccable harden)
- [P1] Não há resumo da situação: total só com >1 instituição (Investimentos.jsx:107), sem data de sync, sem evolução real. Fix: bloco de topo com total, data, nº de instituições, variação. (/impeccable layout)
- [P1] IA mistura carteira, ferramentas e dívida (financiamento em Investimentos.jsx:404); 4/8 falhas de carga cognitiva. Fix: "Minha situação" vs "Ferramentas"; avaliar mover financiamento. (/impeccable distill)
- [P1] Simulador abre em nubank-caixinha (YieldSimulator.jsx:40) + "rende R$ X a mais que a poupança": proximidade de recomendação (CVM). Fix: default neutro, veredito descritivo. (/impeccable clarify)
- [P2] Exclusões sem confirmação (Investimentos.jsx:223,336), dois inputs de CDI, legenda dupla, "linha verde/amarela" com cores turquesa/ocre, chip poupança fixo. (/impeccable polish)

Personas: Jordan (parágrafo antes dos números; estimativa sobre R$ 1.000 lida como dinheiro dele; % do CDI sem explicação perto), Sam (gráfico sem alternativa textual, selos 0,7rem, × sem nome da meta, barra sem progressbar), Casey (rolagem longa, × 24px, "1.5" com ponto), PJ (isenção da poupança só vale para PF mas a simulação aplica a todos).

Menores: erro sem retry; reserva/metas só no navegador; total some com 1 instituição; "Instituição não identificada" repetida; grade do gráfico com cor fixa.
