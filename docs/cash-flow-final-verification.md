# Verificação final — Fluxo de Caixa CNPJ

Data: 16/11/2026. Escopo: revisão funcional e visual sem redesenho.

## Resultado e limite de acesso

Não foi identificado defeito novo da página que justificasse alterar o código nesta revisão.
As verificações técnicas passaram. **A homologação com dados bancários reais carregados
permanece pendente**, portanto não é possível declarar todos os oito itens homologados.

O frontend e o backend locais estavam ativos. Não havia sessão de documento/navegador
conectada às ferramentas do Codex. A tentativa de utilizar o Safari existente foi recusada
pelo macOS com erro `-1743` (envio de eventos Apple não autorizado). Não foram lidos cookies,
forjadas sessões, criadas contas, conexões ou movimentações no aplicativo.

## Evidências por requisito

| Verificação | Resultado | Base e limite |
| --- | --- | --- |
| Layout com dados bancários reais carregados | Pendente | Sem acesso à sessão CNPJ autenticada; não foi substituída por exemplos financeiros. |
| Dia/Mês/Ano, setas e comparações | Passou nos testes e revisão de código | Intervalos consecutivos com fim exclusivo; comparação com o período anterior. Ano usa ano anterior completo, mantendo a base existente que inclui agendados. Totais reais da sessão continuam sem conferência manual. |
| Valores e percentuais das categorias | Código e testes consistentes | Agregados e categorias compartilham banco/intervalo. Percentual = valor da categoria / total do respectivo tipo no período × 100; mesma formatação CPF. Valores bancários carregados não homologados. |
| Expansão e movimentações | Passou nos testes e revisão de código | Detalhes recebem banco, intervalo, tipo e categoria; não há limite de 2.000 nesse endpoint. Há teste passando com 2.300 lançamentos. Requisições anteriores são abortadas e resultados de outro filtro não são exibidos. Não foi possível auditar duplicações ou omissões nos registros reais. |
| Transferências próprias e pagamentos de fatura | Passou nos testes | Agregados e detalhes usam `TransferDetection`/`PolpMovementKind`; a exclusão não depende do nome ou categoria editáveis. Sem mudança de regra. |
| Dia ao entrar e retornar | Passou no navegador isolado | Seleção inicial Dia; após selecionar Ano e período anterior, sair e retornar restaura Dia e o período atual. |
| Desktop e celular | Passou nos estados sem dados | Chromium em 1440, 1024, 768, 390 e 320px; sem overflow ou erro JavaScript. Conteúdo bancário real carregado continua pendente. |
| CPF preservado | Passou na comparação disponível | Mesmo DOM e estilos calculados; capturas consistentes com a referência anterior nos estados de carregamento. CPF continua mensal e sem o seletor empresarial. Código de cálculos e experiência CPF não alterados nesta revisão. |

## Verificações executadas

- `dotnet test backend.Tests/ControlFinance.API.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false`:
  745 testes passaram. Os testes financeiros usam SQLite em memória, sem gravação no banco do aplicativo.
- `npm run lint`: passou.
- `node --test scripts/tests/periods.test.mjs`: 2 testes passaram.
- `npm run build`: passou.
- Renderizador isolado com os componentes reais, sem dados financeiros fictícios: cinco larguras,
  filtros e navegação temporal, retorno em Dia, erro, ausência de conta e comparação CPF/CNPJ.
  A comparação usa DOM e estilos exatos; pixels admitem variação de um nível em 255 por canal
  para antialiasing do Chromium, sem mudança estrutural.
- Skill Impeccable usada apenas para verificar consistência. Nenhum JSX, CSS, cálculo,
  endpoint ou integração foi alterado nesta revisão. Somente este relatório foi acrescentado.

## Encerramento

A implementação passou na revisão técnica. Para encerrar a homologação sem ressalvas, falta
uma sessão CNPJ acessível para conferir os valores existentes em Dia/Mês/Ano, as comparações,
as categorias e suas expansões, com dados carregados no desktop e no celular.

Nenhum commit ou push foi executado. As alterações locais existentes foram preservadas.
