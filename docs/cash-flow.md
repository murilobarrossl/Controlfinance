# Fluxo de caixa CNPJ

## Interface compartilhada

`ReceitasDespesas.jsx` usa um único corpo JSX para CPF e CNPJ, com os mesmos componentes
`SummaryMetric`, `CategoryBlocks`, `RangePicker` e a mesma seção de resultado. Os dois usam
`ReceitasDespesas.css`, sem classes ou sobrescritas de estilo empresariais no corpo da página.

As únicas diferenças visuais são o título **Fluxo de Caixa** e o seletor Dia/Mês/Ano no CNPJ.
O CNPJ começa em Dia a cada montagem da página, inclusive ao sair e retornar; CPF continua
sempre mensal. Setas, intervalos, comparações, dados financeiros e detalhes por categoria
continuam usando os endpoints existentes, sem alteração de cálculos ou de regras de exclusão.

Os indicadores extras e os gráficos empresariais foram removidos. Também foram removidos
`CashFlowCharts.jsx`, `CashFlowCharts.css`, o cliente frontend `api/cashFlow.js`, o auxiliar
`cashFlowSeries.js`, o modo compacto de categorias e os auxiliares/testes exclusivos dos gráficos.
Os testes de navegação por período permanecem em `scripts/tests/periods.test.mjs`.

A navegação mantém uma única aba `/dashboard/fluxo-de-caixa` e o redirecionamento da rota antiga
para CNPJ. Como a rota de Categorias é exclusiva do CPF, o link **Ver categoria** no CNPJ abre
as movimentações da categoria nesta mesma página e período; usa o mesmo elemento e estilo do
CPF. No CPF, o destino e o comportamento do link continuam inalterados.

## Backend preservado

Esta simplificação não modifica integração Polp, endpoints, persistência ou cálculos financeiros.
A captura de observações bancárias, a migração e os testes da investigação anterior permanecem
preservados. A aba simplificada não consulta nem apresenta o histórico de saldo bancário.
O histórico já recebido não é apagado por esta mudança de interface.

## Investigação da Polp v2 — 16/11/2026

O contrato de [conta](https://polp.com.br/docs/celcoin/accounts/show) fornece
`balance.update_date_time`, além de `available_amount`, `blocked_amount` e
`automatically_invested_amount`. O saldo disponível exclui cheque especial, aplicações
automáticas e reservas. A data do saldo é distinta de `updated_at` do recurso na Polp.
A integração local descartava `update_date_time` e mantinha somente o último valor.

A [listagem de contas](https://polp.com.br/docs/celcoin/consents/accounts) também inclui o objeto
`balance`. Portanto, importações e atualizações já recebiam a data sem precisar de outra chamada.
A [documentação dos webhooks](https://polp.com.br/docs/celcoin/webhooks/events) separa o evento
`accounts` de `accounts.transactions`: atualização de transações não comprova atualização de saldo.
A reconciliação local relê recursos alterados, mas antes sobrescrevia a posição sem preservar
observações. `LastSyncedAt` da conexão continua sem servir de data do saldo.

O [sync sob demanda](https://polp.com.br/docs/celcoin/accounts/balance-sync) devolve a posição
atualizada na própria resposta, sem disparar webhook. Por isso, ambos os endpoints locais
(conta e conexão) também precisam preservar a observação diretamente. Não foram adicionadas
chamadas, polling ou novas autorizações na Polp.

Não foi encontrado endpoint de série histórica de saldos na superfície pública v2 consultada.
As datas de criação/atualização filtram recursos existentes; não devolvem suas versões anteriores.
O [extrato de conta](https://polp.com.br/docs/celcoin/accounts/transactions) contém data/hora,
crédito/débito e valor da transação, mas não fornece saldo disponível após cada lançamento nem
uma garantia de conciliação com a posição. No importador, `Paid` é uma classificação local das
movimentações não futuras; não é prova de liquidez ou cobertura contínua do extrato.

**Limitação real:** não há base para reconstruir posições anteriores que já foram sobrescritas.
Faltam observações antigas datadas ou um saldo contábil de referência conciliado, cobertura integral
das movimentações e histórico das alterações de bloqueios, reservas e aplicações automáticas.
Subtrair o extrato do saldo disponível atual não resolve essas diferenças. Nenhuma reconstrução
foi introduzida.

## Verificação desta simplificação

- Lint, build e os dois testes de intervalos e comparação anual.
- Comparação renderizada de CPF/CNPJ no mesmo período e larguras 320, 390, 768, 1024 e
  1440px: mesmo DOM, estilos calculados e pixels no corpo compartilhado, excluindo o cabeçalho
  onde título e filtros são as diferenças autorizadas.
- Comparação do CPF com a implementação anterior nas mesmas cinco larguras: mesmo DOM e
  pixels nos estados de carregamento. Folha CSS da página preservada byte a byte.
- Filtros Dia/Mês/Ano, setas e retorno à página com Dia selecionado.
- Componentes reais em renderizador isolado, sem valores financeiros fictícios ou conexões
  criadas no aplicativo. A comparação visual usa estados de carregamento, erro e ausência
  de conta; não representa validação de uma sessão bancária autenticada.

Nenhum commit ou push foi executado.
