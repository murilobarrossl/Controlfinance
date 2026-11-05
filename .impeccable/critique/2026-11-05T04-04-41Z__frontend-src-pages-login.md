---
target: telas de login e cadastro
total_score: 16
max_score: 40
na_heuristics: 
p0_count: 1
p1_count: 5
target_identity: "file:/Users/igor/Desktop/workspace/projetos/Controlfinance/frontend/src/pages/login"
timestamp: 2026-11-05T04-04-41Z
slug: frontend-src-pages-login
closed: true
---
# Critique — auth flow (login email/CPF-CNPJ, cadastro, TermsDialog)
Method: dual-agent. Score 16/40 (Poor). No password recovery exists.

## Trust/compliance
1. [P0] No password recovery; rate-limit lockout returns same "Credenciais inválidas." (AuthService.cs:127-151). Add reset flow; interim support link; actionable message.
2. [P1] Validation (password rule, CPF/CNPJ check digit, duplicates) only after Terms acceptance (Cadastro.jsx:49-88); error far from field, no role=alert/aria-describedby; must re-scroll Terms.
3. [P1] Network/server errors leak "Failed to fetch"/"Erro 500" (api/client.js).
4. [P1] Trust fine print #808080 on #1a1a1a 4.41:1 at 12px; placeholders 2.47:1; dialog policy link default blue 1.85:1; checkbox border 1.53:1; dialog lacks focus trap.
5. [P2] PF/PJ unequal: email login default, CPF/CNPJ behind 21px link; backend accepts both in one field (AuthService.cs:136-141); "NOME" ambiguous.
6. [P2] Claims: "Criar conta grátis", "segurança total", "simule financiamentos".

## Aesthetic
1. [P1] Template look: kicker badges, uppercase hard-coded labels, no hero photo, 32px logo tile, "entrar" x3.
2. [P2] Heading order (h1→h3; dialog h3 title with h2 sections); mobile login form starts ~420px; trust bullets hidden on mobile.
3. [P2] Touch targets <44 (toggle 28, submit 40h, checkbox 20, Voltar 21h, dialog buttons 33h); links default blue focus ring; no loading indicator.
4. [P3] duplicate disabled opacity; error same color as links; generic page title; legal stragglers.

## Questions
1. Single identifier field? 2. Order data→validation→terms? 3. Why is the most reassuring line the faintest?
