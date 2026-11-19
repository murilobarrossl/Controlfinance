# Despesas recorrentes (CPF e CNPJ)

Implementação somente de leitura, independente do radar legado e de suas decisões manuais.
Não cria lançamentos, não usa IsFixed, não altera totais financeiros de outras áreas e não
executa pagamentos nem contrata ou cancela serviços.

## Documentação oficial consultada

- https://polp.com.br/docs/celcoin
- https://polp.com.br/docs/celcoin/credit-cards/recurrings
- https://polp.com.br/docs/celcoin/credit-cards/transactions
- https://polp.com.br/docs/celcoin/accounts/transactions
- https://polp.com.br/docs/recurrings/by-account (legado v1, não utilizado)

A base v2 documentada coincide com a integração existente, incluindo a separação entre
contas e cartões e paginação por meta.next_cursor. O recurso de cartão
GET /credit-cards/{creditCard}/recurrings é uma detecção derivada pela Polp após a
sincronização das transações, não uma declaração de assinatura pelo banco. Seus campos
são camelCase, diferentemente das transações. O recurso informa descrições, ocorrências
(IDs das transações), periodicidade e datas esperadas. A documentação também oferece média,
moeda e regularidade; o Control Finance não usa a média como cobrança confirmada.
A documentação descreve exclusão de parcelas, A_PRAZO, tarifas, IOF, créditos e estornos.
Disponibilidade e qualidade dependem dos dados importados. Não foi encontrada rota equivalente
para contas na referência Celcoin v2. As rotas de recorrências por conta/integração da v1
não são utilizadas nem adaptadas para a v2.

## Sincronização e armazenamento

A leitura de recorrências acompanha a leitura de transações de cada cartão em sincronizações
CPF e CNPJ, com paginação já existente. Nenhuma leitura da página chama a Polp. O resultado é
substituído, e não acumulado, sob o lock da integração, junto da importação existente.
Somente dois campos novos em BankAccounts: PolpRecurringsEncrypted (JSON cifrado pelo
conversor existente) e PolpRecurringsSyncedAt. A migration CardRecurringSnapshots foi criada
manualmente no padrão das migrations recentes; não foi aplicada.

JSON null significa indisponível/ainda não consultado; [] significa resposta sem recorrências.
HTTP 403, 404 e 501 são tratados como indisponibilidade, sem afirmar ausência de assinaturas.
Falhas transitórias mantêm o snapshot anterior e não interrompem a importação financeira.
Nenhum corpo de resposta é registrado em logs. Dados sem atualização há mais de sete dias
ficam sinalizados e não geram próximas datas estimadas. Conexões sem autorização não são
expostas. A exclusão de uma conta/cartão remove seu snapshot com o registro existente.
Cartões existentes recebem o snapshot na próxima sincronização de suas transações;
não há reimportação forçada de histórico nem botão de consulta avulsa.

## Identificação

Cartões: prioriza exclusivamente os grupos fornecidos pela Polp, relacionando os IDs com as
transações locais do mesmo usuário, consentimento e cartão. Exige três ocorrências reais
locais e três IDs distintos no grupo. Grupos sobrepostos são unidos pelos IDs, nunca pelo nome.
Não associa automaticamente uma nova transação a um grupo apenas por descrição semelhante.
Não há fallback de cartão: o banco local ainda não guarda payment_type, necessário para
excluir A_PRAZO com segurança. Valores variáveis são aceitos nos grupos detectados pela Polp.

Contas: inferência própria só com identificador bancário. A v2 não tem tipo de débito
automático nem de Pix Automático (enum AccountTransactionType); os tipos aceitos são BOLETO,
PIX, TED, DOC, TRANSFERENCIA_MESMA_INSTITUICAO e CONVENIO_ARRECADACAO, sempre com o CNPJ do
destinatário enviado pelo banco (partie_cnpj_cpf, CounterpartyCnpjFromBank), diferente do
titular (comparado pelo DocumentHash). O agrupamento é por CNPJ; se o CNPJ tiver mais de uma
cobrança por mês, separa pela descrição bancária exata. PACOTE_TARIFA_SERVICOS forma um grupo
próprio por conta (cobrança do próprio banco). CPF de destinatário, conta sem documento,
descrições genéricas, saque, débito no cartão, folha, operações de crédito, transferências
(PolpMovementKind), tributos e parcelas não são candidatos.
Exige a última sequência ininterrupta de três ou mais cobranças: mensal (cada uma a até cinco
dias de um mês após a anterior) ou anual (até dez dias de um ano após). Cobranças fora dessa
sequência não entram no grupo. Valores: até 20% acima do mínimo; convênio (conta de consumo),
até o dobro. Nenhuma recorrência é classificada como tipo de serviço; a interface mostra
apenas o meio de pagamento.

Recorrência inferida é um padrão, não confirmação de assinatura/contrato; boletos parcelados
sem qualquer identificação de parcela nos dados disponíveis podem ser indistinguíveis de
uma cobrança periódica. As limitações são explicitadas na interface. Preferimos não detectar
quando faltam evidências, inclusive sem CNPJ bancário ou com reajustes acima da faixa.
Anuais inferidas exigem ao menos três anos de cobranças no histórico disponível.

## Valores, previsão e limites

O resumo mensal soma somente débitos reais vinculados aos grupos identificados, com decimal
no backend e deduplicação por recurso de conta/cartão, ID remoto na conta e ID local nas somas. A data usada é a
data civil importada, já resolvida no fuso do titular. Os valores do cartão são BRL reais,
não a média da Polp ou uma conversão de câmbio estimada. Cobrança no cartão não equivale a
fatura quitada. O resumo é bruto de cobranças identificadas: não concilia estornos posteriores
com o débito original, pois não há vínculo de estorno no modelo local.

A próxima data é uma estimativa de uma única próxima ocorrência; aceita a data da Polp
somente quando compatível com o último registro e a periodicidade. Não soma previsão, não
projeta atrasados para datas futuras e não estima com dados antigos. Sem repetição após
a próxima data mais dez dias (trinta para anuais), mostra ausência de cobrança recente,
sem declarar cancelamento ou atividade de contrato.

A listagem mostra todas as recorrências do histórico disponível no escopo selecionado.
O seletor só oferece contas: com uma conta selecionada, entram ela e os cartões da mesma
conexão. Sem conta (conexão só com cartões), a página consulta todos os cartões do usuário.

o seletor mensal altera o resumo efetivamente cobrado, não a existência da recorrência.
A página individual apresenta os lançamentos bancários reais, com paginação de exibição,
origem da identificação, instituição, conta/cartão, atualização e estimativa separada.
Não há entradas de nome, valor ou pagamento, nem ações de confirmação manual.

Não unimos serviços entre contas/cartões diferentes apenas pelo nome: podem ser contratos
distintos. Um ID de detalhe pode mudar quando o histórico mais antigo deixa de estar disponível,
quando a Polp reagrupa ocorrências ou quando a chave de busca é rotacionada; a página trata 404.
Não há garantia de cobertura de todos os serviços, bancos, reajustes, períodos ou cancelamentos.

## Desempenho

A detecção roda uma vez por usuário (todas as contas autorizadas) e fica em IMemoryCache por
até dois minutos, validada por um carimbo: data do titular, UpdatedAt/LastSyncedAt de cada
integração (toda importação passa por PolpSyncService.ApplyAsync) e PolpRecurringsSyncedAt de
cada cartão. Escopo, mês, busca, paginação e o detalhe reutilizam o mesmo resultado.
Em uma falta de cache: contas são projetadas sem o JSON de recorrências; os snapshots são lidos
só dos cartões que os têm; transações vêm projetadas, filtradas no SQL por colunas não
cifradas (tipo, status, movimento, tributo, parcelas, tipo bancário, CNPJ do banco) e, nos
cartões, só pelos IDs de ocorrência da Polp. Sem corte de data, para não perder anuais.
O índice existente (UserId, BankAccountId, DueDate) atende; não há migration.

## Endpoints e acesso

- GET /api/recurring-expenses?year=AAAA&month=MM[&bankAccountId=UUID][&page=N][&search=texto]
  (25 por página; busca por nome, instituição e conta no servidor; resumo do mês independe da busca)
- GET /api/recurring-expenses/{id}

Ambos exigem autenticação, para usuários CPF e CNPJ. Todas as consultas
são filtradas por UserId, e os grupos permanecem isolados por conta/cartão. Não reutilizam
endpoints de decisão manual do radar. As duas navegações usam as mesmas páginas e rotas,
protegidas pelo ProtectedRoute do dashboard. Não há componentes, endpoints ou critérios
duplicados. A adaptação para CPF reutiliza os campos existentes e não exige migration adicional.

Conforme solicitado, não foram executados build, lint, testes, navegador ou reinício.
A revisão foi feita por leitura de código, contratos oficiais, referências e diff.

## Evidências do sandbox (2026-11-18)

O sandbox expõe no máximo 50 transações por conta/cartão. Nos 6 cartões (2 CNPJ, 4 CPF)
há assinaturas mensais de valor fixo, mas só 4 grupos alcançam três meses dentro da janela:
3 num cartão CNPJ e 1 num cartão CPF, todos devolvidos pela Polp e casados localmente pelos IDs.
Nas contas, a janela cobre cerca de 45 dias e os boletos chegam sem CNPJ do recebedor e com
descrição genérica; nenhum grupo atinge três ocorrências. A aba aparecia vazia porque o escopo
sempre é uma conta corrente e os cartões ficavam fora da consulta.
