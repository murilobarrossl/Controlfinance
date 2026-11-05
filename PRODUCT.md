# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Pessoa física (CPF) e pequenos negócios (CNPJ), com peso igual: nenhum perfil tem prioridade, e cada tela precisa servir aos dois. O denominador comum é quem não entende de finanças e quer saber, sem esforço, para onde o dinheiro está indo, o que vence e quanto sobra. A pessoa está lidando com o próprio dinheiro, muitas vezes com pressa ou preocupação, e o produto vai ser usado por milhares delas.

## Product Purpose

O Control Finance é uma plataforma SaaS de gestão financeira para o mercado brasileiro, desenvolvida pela Mitech. O usuário conecta a conta bancária uma vez pelo Open Finance, e cada transação passa a ser importada e organizada por categoria automaticamente. A proposta central é devolver clareza financeira a quem não é da área: **explicação em vez de dado cru**. O sucesso é o usuário entender a própria situação financeira sem precisar interpretar números soltos.

O redesign mantém o conteúdo, a proposta e todas as funcionalidades. Só a interface muda: mais profissional, com personalidade própria e sem a aparência genérica de site feito por IA.

## Positioning

Uma única conexão pelo Open Finance basta para organizar as contas de PF e PJ em um só lugar, explicando cada número em vez de só mostrá-lo. O produto também coleta o mínimo possível: a senha do banco não passa pelo Control Finance, o CPF não fica guardado (só um hash), valores e nomes ficam cifrados, os e-mails não levam valores e o usuário pode baixar ou apagar tudo quando quiser.

## Operating Context

- Conexão bancária: o usuário autoriza no próprio banco, pelo Open Finance, por meio de um fornecedor parceiro (Polp). Depois da carga inicial, a sincronização é automática, quando o fornecedor avisa que há dados novos; o produto não oferece um botão de "sincronizar agora". Toda informação vinda do banco precisa mostrar de quando ela é (data da última sincronização).
- A conexão pode ser desfeita a qualquer momento no painel; ao desconectar, os dados daquela conexão são apagados.
- Lembretes por e-mail: um aviso agregado de vencimentos (o usuário pode desativar) e um aviso mensal (o usuário precisa ativar), os dois sem nomes nem valores.
- Assinatura: os planos (Mensal, Trimestral, Anual; preços derivados de `frontend/src/utils/pricing.js`) aparecem só como preço de referência. **Não há checkout nem cobrança**, e o botão diz "Contratação em breve". O teste grátis previsto é de 14 dias. Renovação, cancelamento e reembolso precisam ser publicados antes de qualquer cobrança.

## Capabilities and Constraints

- **Telas atuais:** landing pública (Hero, Funcionalidades, Planos, Sobre nós, FAQ), cadastro com aceite dos termos, login, conexão bancária, painel inicial, Receitas e Despesas, Relatórios, Categorias, Cartões (faturas, parcelas, vencimentos), Investimentos e Privacidade (exportação de dados, preferências de e-mail, exclusão da conta). Há também as páginas de Termos de Uso e de Política de Privacidade.
- **Investimentos:** a aba vai passar por uma reforma grande depois. Por enquanto fica como está (comparações por banco com taxas de referência datadas e simuladores) e fica fora do escopo do redesign, a não ser que você peça.
- **Correção antes de velocidade:** a interface nunca pode exibir um valor errado, desatualizado ou ambíguo, nem por um instante. Prefira um estado de carregamento explícito a um número provisório.
- **Dinheiro:** sempre no padrão brasileiro (R$ 1.234,56). Sinal, cor e rótulo de entrada e saída precisam ser consistentes. A cor nunca pode ser o único indicador de positivo ou negativo.
- **Recência:** datas, saldos, faturas e status de sincronização deixam claro de quando é a informação.
- **Ações sensíveis:** desconectar banco, excluir dados e cancelar assinatura exigem uma confirmação clara e não podem ser disparadas por engano.
- **LGPD (Lei 13.709/2018):** o consentimento é explícito, compreensível e nunca vem pré-marcado. A Política de Privacidade e os direitos do titular ficam a um clique. Os aceites são registrados com versão (`LegalDocuments.cs` e `frontend/src/legal/versions.js`).
- **Open Finance / Banco Central:** o fluxo de conexão diz quais dados são acessados, para qual finalidade, por quanto tempo e como revogar o consentimento.
- **CDC:** preço, recorrência, período de teste, renovação e cancelamento ficam sempre explícitos e visíveis. Nada de dark patterns: sem letras miúdas, sem cancelamento escondido, sem urgência falsa, sem botão enganoso, sem botão que sugira uma contratação que não acontece.
- **CVM:** nenhuma tela sugere recomendação de investimento personalizada. O produto organiza e explica dados, não presta consultoria. O rodapé declara que o Control Finance não é instituição financeira.
- **Sem promessas:** nenhuma promessa de resultado financeiro, nenhum número de usuários, nenhum depoimento que não seja real.
- **Termos:** "Abrir conta" é o CTA de cadastro; "Open Finance", "sincronização" e "fatura" são termos usados na interface.

## Brand Commitments

- Nome: Control Finance, desenvolvido pela Mitech. Logos em `frontend/src/assets/images/cflogobranco.svg` e `cflogopreto.svg`.
- Tom institucional, confiável e claro: nem banco tradicional, nem startup genérica. Linguagem simples para quem não é da área.
- Todo texto da interface em português do Brasil.
- A foto de arranha-céus do hero (`frontend/src/assets/images/hero-bg.webp`) fica: escolha do usuário, mantida em redesigns.
- **Em aberto:** razão social, CNPJ, endereço e encarregado de dados (DPO) ainda não foram informados e aparecem como `[PENDENTE]` no rodapé e na Política de Privacidade. Também não está confirmado se a Mitech é a razão social do controlador. Não inventar esses dados.

## Evidence on Hand

- Textos reais da landing, do FAQ e das páginas legais em `frontend/src/pages`.
- Contato: suporte@controlfinance.app.br.
- **Não existem**: depoimentos, número de usuários, clientes, métricas de resultado, prêmios ou menções na imprensa. O dado de endividamento das famílias em "Sobre nós" só volta com fonte citada. Nada disso pode ser fabricado.

## Product Principles

1. **Correção é a funcionalidade.** Um número errado, provisório ou sem data é um defeito financeiro, não estético.
2. **Explicar, não despejar.** Todo número relevante vem acompanhado do que ele significa para a pessoa.
3. **O usuário está no controle dos próprios dados.** Consentimento, revogação, exportação e exclusão ficam visíveis e são fáceis de usar.
4. **Honestidade comercial.** Preço, condições e limitações aparecem por inteiro. O que ainda não existe não é sugerido.
5. **Organizar, nunca aconselhar.** O produto informa e esclarece, mas não indica o que fazer com o dinheiro.

## Accessibility & Inclusion

Lei Brasileira de Inclusão (13.146/2015), com WCAG 2.1 AA como referência prática: contraste adequado, navegação completa por teclado, foco sempre visível, textos legíveis, rótulos em todos os campos de formulário e alternativa textual para os gráficos. A cor nunca é o único portador de significado, e isso vale especialmente para entrada e saída de dinheiro.
