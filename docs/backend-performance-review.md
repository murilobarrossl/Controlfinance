# Revisão estática de desempenho e disponibilidade — 17/11/2026

Escopo: navegação, autenticação e processamento Polp. Não foram executados testes,
build, lint, navegador, servidores ou comandos no banco. Não havia logs completos
com stack traces e horários no contexto; os sintomas foram informados pelo usuário.

## Problemas identificados no código e alterações

- Dashboard materializava entidades Transaction completas e categorias, incluindo
  diversos campos criptografados sem uso no resumo. Agora projeta só tipo, valor,
  categoria e nome da categoria, e exclui transferências na consulta. Totais continuam
  calculados em memória: Amount é criptografado por conversor, portanto SUM SQL
  sobre a coluna cifrada não preservaria os cálculos.
- Agregados liam transferências para descartá-las no loop. O mesmo filtro de
  negócio agora também é aplicado antes da leitura SQL. Ordenação e agrupamentos
  foram preservados.
- Consultas do dashboard e verificações de revogação/sessão não recebiam o
  cancelamento HTTP. Agora recebem RequestAborted, para liberar recursos quando
  o cliente abandona a consulta.
- Logout fazia DELETE de todos os tokens expirados, SELECT e escrita. O DELETE
  global saiu do caminho interativo e passou para a retenção diária existente.
  A gravação da revogação continua obrigatória antes de remover o cookie.
- Falhas transitórias do provedor eram tratadas como erro genérico. Agora retornam
  503 com Retry-After e diagnóstico sanitizado. A autenticação propaga essas falhas
  ao handler, sem convertê-las em credencial inválida/401. Nenhuma verificação foi
  dispensada. Cancelamento do cliente não produz resposta de erro genérico.
- Worker tinha espera fixa após falhas e podia consumir o evento seguinte logo
  após erro transitório no handler. Agora aplica espera exponencial com jitter
  (aproximadamente 30 s até 8 min), inclusive após falha transitória no handler.
  A política de persistência, lease, deduplicação e tentativas dos eventos permanece.
- Reconciliação recebe pequeno atraso aleatório inicial para distribuir a disputa
  de locks entre réplicas na inicialização. Não altera seleção de integrações.
- GETs idênticos simultâneos no cliente agora compartilham a requisição enquanto
  o corpo chega. Cada consumidor mantém cancelamento independente; o último
  consumidor cancelado cancela o transporte. Troca de sessão limpa e cancela as
  leituras pendentes. Não há cache de respostas concluídas ou reutilização entre
  navegações. Headers/opções especiais ficam fora desse compartilhamento.

## Pool, consultas e limites da conclusão

O desenvolvimento configura pool máximo de 10 conexões; não foi aumentado.
A configuração local usa conexão direta Supabase na porta 5432. Isso, por si só,
não comprova defeito: é necessário verificar resolução DNS, acesso IPv4/IPv6,
conectividade e limites do projeto a partir do ambiente em que os erros ocorrem.
Não foi inventado um endereço de pooler ou alterada a credencial local.

PostgresJobLock usa uma conexão e transação por rodada inteira. É compatível com
poolers em modo transação, mas consome capacidade enquanto outros contextos fazem
as consultas do job. Vários jobs começam no startup. Não foi trocado por lock de
sessão (incompatível com pooler transacional), nem removida exclusão entre réplicas.
Essa arquitetura ainda precisa ser dimensionada com métricas para milhares de
usuários; as alterações não constituem certificação de capacidade.

Já existem índices (UserId, DueDate) e (UserId, BankAccountId, DueDate) para as
transações. Nenhum índice adicional foi criado sem plano real de execução.
Não há execução paralela adicionada sobre um mesmo DbContext nem retry automático
novo em operações financeiras. O worker já processava um evento por vez por instância.

As reduções de colunas e linhas lidas e a remoção de DELETE por logout são
comprováveis por inspeção do código. A queda de latência, consumo de conexões e
frequência de timeouts é esperada, mas não medida. Não se pode afirmar que a causa
primária de indisponibilidade do Supabase foi resolvida sem evidências do ambiente.

Para fechar o diagnóstico operacional, são necessários stack traces sanitizados,
latência de abertura versus execução, conexões ativas/ociosas, espera no pool e
pg_stat_statements/planos das consultas afetadas no período do erro. Esses dados
permitem distinguir falha de rede de saturação e confirmar os ganhos sem alterar
limites ou segurança por suposição.

## Diagnóstico após recebimento do log de inicialização

O log posterior confirmou timeout na abertura inicial, antes de migrations e
workers. Portanto, concorrência dos workers e consultas de navegação não explicam
essa ocorrência específica. Foram feitas verificações pontuais de conectividade,
sem build, testes automatizados, servidor, migrations ou alterações de dados:

- DNS resolveu o endpoint direto para IPv6; abertura TCP respondeu em 0,32 s.
- PostgreSQL respondeu ao pedido de TLS e à negociação inicial de autenticação.
- Uma abertura pelo psql expirou; outra, com a configuração local e negociação
  GSS desativada, autenticou e executou SELECT 1 em 3,45 s. Como as condições das
  tentativas diferiram, isso não isola a causa do timeout.
- O binário já compilado foi executado exclusivamente com --check-database.
  O próprio EF Core/Npgsql confirmou SELECT 1 e TLSv1.2, com saída zero, sem
  migrations, backfills, workers ou servidor.

A configuração atual funcionou no momento da verificação. Não foi necessário
alterar host, senha, pool ou timeouts. Isso confirma recuperação naquele momento,
mas não comprova resolução permanente da indisponibilidade intermitente nem
permite atribuir a causa a IPv6, TLS, Supabase ou rede local.

## Correção da abertura extra durante migrations

Um novo stack trace localizou a falha em NpgsqlDatabaseCreator.Exists, chamado
por HistoryRepository.Exists durante Migrate. O código-fonte do provider 8.0.4
confirma que Exists clona a conexão com Pooling=false e abre outra sessão, mesmo
quando o contexto já mantém uma sessão autenticada. Portanto, o diagnóstico
anterior de SELECT 1 não cobria esse caminho adicional.

ConnectedNpgsqlDatabaseCreator passa a reconhecer a existência do banco quando
a conexão do próprio contexto já está aberta. Se estiver fechada, preserva o
comportamento padrão do provider. A checagem da tabela de histórico e execução
das migrations continuam normais, sem repetição automática de DDL/backfills.
Migrate e GetPendingMigrations agora usam APIs assíncronas com cancelamento.

Essa extensão usa API interna do provider fixado em 8.0.4 e precisa ser revisada
ao atualizar essa dependência. A mudança remove o handshake extra identificado
no stack trace; não repara quedas da conexão principal. Foi revisada estaticamente,
sem build, testes, servidor ou execução das migrations após a alteração.

Referência: https://github.com/npgsql/efcore.pg/blob/v8.0.4/src/EFCore.PG/Storage/Internal/NpgsqlDatabaseCreator.cs
