---
target: landing page (src/pages/home)
total_score: 23
max_score: 32
na_heuristics: 7
p0_count: 1
p1_count: 7
target_identity: "file:/Users/igor/Desktop/workspace/projetos/Controlfinance/frontend/src/pages/home"
timestamp: 2026-11-05T02-56-08Z
slug: frontend-src-pages-home
closed: true
---
# Critique — landing page (frontend/src/pages/home)

Method: dual-agent (A: design review · B: detector + browser)

## Score: 23/32 (n/a: 7). H1 3, H2 3, H3 3, H4 2, H5 3, H6 2, H8 2, H9 2, H10 3.

## Design specificity
Category-interchangeable. Stock skyscraper hero (contradicts PRODUCT.md "not a traditional bank"), repeated kicker+title+bar SectionHeading x4 (redundant labels), 3x3 identical feature grid, stock 3-tier pricing with highlighted middle, no product imagery: the differentiator "explicação em vez de dado cru" is never shown.
Detector: CLI clean (0). Browser overlay 14 @1440 (2 low-contrast, 7 straggler, 5 kicker-above-heading), 9 @390. Overflow @390 was false positive (detector labels).

## Trust / compliance priority issues
1. [P0] Footer.jsx:77 shows "[PENDENTE]" for razão social/CNPJ/endereço (Decreto 7.962 art.2, LGPD art.41). Needs real data; block production publish.
2. [P1] Sync copy false: Funcionalidades.jsx:25 and FAQ.jsx:49 say data updates "quando você abre o painel"; actual sync is webhook-driven. Rewrite to automatic + last-sync date.
3. [P1] "Abrir conta" ambiguous/cost unclear; no "Como funciona" (dados acessados, finalidade, prazo, revogação) before CTA. Add microcopy + 3-step block.
4. [P1] Primary CTA white on #ed4a31 = 3.73:1 (navbar__cta, hero__cta), fails AA. Darken to ~#c93a24. Orange as text on dark passes (5.02/4.66/5.2).
5. [P1] index.html lang="en" on pt-BR page (LBI).
6. [P2] Vague promises: "suporte prioritário com respostas mais rápidas", "crescer financeiramente", "grandes investidores"; unexplained highlighted quarterly plan.

## Aesthetic priority issues
1. [P1] Generic identity + hero photo contradicts positioning; show the product (number + explanation, CPF and CNPJ variants).
2. [P1] PF/PJ not represented (PJ in 1 of 9 cards).
3. [P2] Typography: only Prompt 400 loaded, 500/600/700 faux-bold; weak H1/H2 ratio 1.43x; 7 stragglers.
4. [P2] Section order Hero→Sobre→Func→Planos→FAQ; trust content buried; hero left vs rest centered; trust line 0.82rem @60%.
5. [P2] Inconsistencies: planos__cta reimplements Button; cobrado/Cobrado; ✓ in income color #4ecdc4; "R$49,90" no space; white logo tile; A/o Control Finance.

## Personas
Jordan: Open Finance unexplained, "Abrir conta" ambiguous, mission before product, dead plan buttons.
Dona Márcia (MEI): skyscrapers + "grandes investidores", nothing on separating business/personal.
Casey: 80vh hero, 7.6k px page, 0.72rem legal text, mobile menu no Esc/focus.
Riley: dead #time link, sync contradiction.

## Minor
Lowercase feature descriptions vs Title Case titles; hero kicker repeats logo; nav CTA beside logo; Planos footnote no max-width (~180ch); stale "Seis links" comment Navbar.jsx:19.

## Questions
1. Why no explained number on the page? 2. Do skyscrapers help Dona Márcia trust? 3. Should commercial part end on "em breve" or on "seus dados, seu controle"?
