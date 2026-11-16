# Divisores do painel

- Resumos financeiros usam `summary-dividers` (duas colunas) e, quando necessário, `summary-dividers--four`. O CSS fica em `frontend/src/styles/dividers.css`.
- Aplique a classe somente ao grid de indicadores. O ancestral deve ter `container-type: inline-size`; as quebras usam a largura disponível do componente, inclusive no zoom.
- A divisória é uma borda de 1px em `--color-border` no limite entre colunas, com o mesmo padding de cada lado. Não combine esse padrão com `column-gap`, margens laterais ou pseudo-elementos deslocados.
- Quatro colunas a partir de 60rem disponíveis, duas abaixo disso, uma até 30rem. O primeiro indicador de cada linha não recebe borda vertical. Em uma coluna, a separação passa a ser horizontal.
- Folha de Pagamento preserva sua quebra específica em 44rem. Investimentos preserva o primeiro indicador mais largo e sua distribuição em duas linhas até 42rem.
- Linhas de seção pertencem ao contêiner completo, evitando fragmentá-las entre colunas. Se as seções empilharem, cada nova seção pode ter sua própria borda superior.
- Tabelas mantêm `border-collapse: collapse`, cabeçalho com `--color-border-strong` e linhas com `--color-border`. Listas de lançamentos mantêm divisores mais sutis onde já eram intencionais.
- Indicadores de navegação ativa, trilhas de transferência, estados de erro e bordas de campos não são divisores de seção. Não aplique uma regra global de borda a esses elementos.
