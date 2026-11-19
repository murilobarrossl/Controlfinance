import LegalPageLayout from "./LegalPageLayout.jsx";
import { useEffect, useState } from "react";
import { PRIVACY_VERSION } from "../../legal/versions.js";
import { getDataPolicy } from "../../api/account.js";
import usePageTitle from "../../hooks/usePageTitle.js";

// Este texto descreve o que o código faz hoje. Ao mudar como um dado é coletado, guardado ou
// compartilhado, atualize aqui E a versão em legal/versions.js + backend/Services/LegalDocuments.cs.
// Itens [PENDENTE] dependem de informação que ainda não existe (dados da empresa, contratos,
// fornecedores): não preencher com suposições.
export default function PoliticaDePrivacidade() {
  usePageTitle("Política de privacidade");
  const [dataPolicy, setDataPolicy] = useState(null);

  useEffect(() => {
    const controller = new AbortController();
    getDataPolicy({ signal: controller.signal })
      .then(setDataPolicy)
      .catch(() => {});
    return () => controller.abort();
  }, []);

  return (
    <LegalPageLayout kicker="Legal" title="Política de privacidade" updatedAt={`20 de novembro de 2026 (versão ${PRIVACY_VERSION})`}>
      <h2>Resumo</h2>
      <ul>
        <li>Coletamos o mínimo para o painel funcionar. O número do seu CPF/CNPJ não fica guardado, e não pedimos telefone.</li>
        <li>Se você conectar um banco, guardamos contas, saldos, extrato, dados de cartão, investimentos, caixinhas e, para CPF, contratos de empréstimo, cifrados, enquanto a autorização no banco estiver ativa. Se ela expirar ou for revogada no banco, os dados deixam de ser usados e são apagados após 30 dias.</li>
        <li>Os dados passam só por fornecedores indispensáveis ao serviço, listados abaixo. Não vendemos dados e não usamos para publicidade.</li>
        <li>No painel, em <strong>Privacidade e dados</strong>, você baixa uma cópia de tudo, desconecta bancos, desliga e-mails e exclui a conta.</li>
      </ul>

      <h2>O que mudou nesta versão (20 de novembro de 2026)</h2>
      <ul>
        <li>
          Autorização de Open Finance que expira ou é revogada no banco: os dados que vieram daquele banco deixam de ser
          usados no painel e em qualquer cálculo e são apagados após 30 dias, como numa desconexão. Antes, ficavam
          guardados até você desconectar o banco.
        </li>
        <li>
          As finalidades do uso dos dados bancários em contas CNPJ passam a ser listadas por completo na seção 3:
          painel da empresa, fechamento de caixa, identificação de pagamentos aos funcionários cadastrados e
          identificação de pagamentos de tributos.
        </li>
        <li>
          Contas CNPJ: esta Política passa a descrever os dados de funcionários e fornecedores, custos adicionais,
          marcações de pagamento, fechamentos de caixa e impostos (seções 2, 3, 4, 7 e 8), e quem decide sobre os dados
          de funcionários e fornecedores (seção 1).
        </li>
        <li>
          A descrição da identificação de pagamentos foi corrigida (seção 2). A versão de 19 de novembro dizia que
          fornecedores também eram identificados e que conta e nome iguais indicavam um “possível pagamento”. O sistema
          identifica só funcionários, nunca usa nomes e não tem o nível “possível pagamento”.
        </li>
        <li>As seções 1 e 5 passam a indicar com precisão o que ainda depende da constituição da empresa e de contratos.</li>
        <li>
          Contas CNPJ: desconectar um banco (ou o fim da autorização dele, após 30 dias) passa a apagar também os
          fechamentos de caixa que usaram dados desse banco e os valores dele guardados no histórico de marcações de
          pagamento (seções 7 e 9).
        </li>
        <li>
          Contas sem atividade por 3 anos passam a ser excluídas, depois de três avisos por e-mail (seção 7). Para isso,
          guardamos a data da última atividade (seção 2).
        </li>
      </ul>

      <h2>O que mudou na versão de 19 de novembro de 2026</h2>
      <p>
        Para contas CNPJ, o Control Finance passou a identificar pagamentos aos funcionários cadastrados a partir das
        transações que o banco envia, guardando o CPF e a conta completa do destinatário só como códigos derivados. A
        descrição publicada nessa versão continha erros, corrigidos na versão de 20 de novembro; a descrição correta está
        na seção 2.
      </p>

      <h2>O que mudou na versão de 17 de novembro de 2026</h2>
      <p>
        Para contas CPF, a conexão bancária passa a pedir também a autorização de operações de crédito do Open Finance,
        para exibir seus contratos de empréstimo em Receitas e despesas. Guardamos, cifrados, só o produto, a data e o
        valor contratado, o CET, a quantidade de parcelas (pagas, a vencer e em atraso), o valor da próxima parcela, o
        saldo devedor e as datas de vencimento. Não guardamos número do contrato, garantias nem dados de financiamentos,
        cheque especial ou antecipações, embora a autorização do Open Finance os inclua. Contratos são informativos e não
        entram nos totais. Conexões já existentes só passam a enviar contratos se você conectar o banco de novo e autorizar.
      </p>

      <h2>O que mudou na versão de 16 de novembro de 2026</h2>
      <p>Para contas CNPJ, guardamos também as observações de saldo disponível com a data informada pelo banco, para exibir sua evolução no Fluxo de Caixa. Os valores são cifrados e seguem o mesmo prazo de retenção do histórico bancário, exportação e exclusão da conexão. Não reconstruímos saldos anteriores às observações recebidas.</p>

      <h2>O que mudou na versão de 14 de novembro de 2026</h2>
      <ul>
        <li>
          Deixamos de guardar valores digitados à mão no planejamento: o valor anotado da reserva de emergência e o valor
          “já guardado” das metas foram apagados. A reserva e o progresso das metas passam a vir só dos saldos informados
          pelos bancos.
        </li>
        <li>
          Passamos a guardar quais investimentos e caixinhas você vincula à reserva e a cada meta. Não há nova finalidade,
          novo compartilhamento nem novo fornecedor.
        </li>
      </ul>

      <h2>O que mudou na versão de 13 de novembro de 2026</h2>
      <ul>
        <li>
          Passamos a guardar mais detalhes de cada transação que o banco já enviava pelo Open Finance: horário, tipo da
          operação, descrição original, a outra parte (nome comercial, razão social e CNPJ de empresas, se é pessoa física
          ou jurídica, código do banco e 4 últimos dígitos da conta), cartão usado (4 últimos dígitos), parcela, mês da fatura e valor em moeda estrangeira.
        </li>
        <li>
          Finalidade: mostrar esses detalhes a você na nova tela de detalhes da transação, dentro do Relatórios. Não há nova
          finalidade, novo compartilhamento nem novo fornecedor.
        </li>
        <li>
          Para transações importadas antes desta versão, o Control Finance relê uma única vez o histórico já
          disponível na Polp. Isso não cria nova autorização no seu banco.
        </li>
      </ul>

      <h2>1. Quem é o responsável pelos seus dados</h2>
      <p>
        O controlador dos dados é <strong>[PENDENTE: razão social]</strong>, CNPJ <strong>[PENDENTE]</strong>, com endereço
        em <strong>[PENDENTE]</strong>, responsável pela plataforma Control Finance. Esses dados serão informados aqui
        quando a empresa for constituída.
      </p>
      <p>
        Canal para assuntos de privacidade e para exercer seus direitos: <strong>suporte@controlfinance.app.br</strong>.
        Encarregado pelo tratamento de dados pessoais: <strong>[PENDENTE: nome do encarregado ou, se a empresa se
        enquadrar como agente de tratamento de pequeno porte e não indicar um, a informação de que este canal atende os
        titulares (Resolução CD/ANPD nº 2/2022, art. 11)]</strong>.
      </p>
      <p>
        <strong>Dados de funcionários e fornecedores (contas CNPJ).</strong> Quando uma empresa cadastra funcionários e
        fornecedores, é ela quem decide por que e como esses dados são tratados: a empresa é a controladora, e o Control
        Finance atua como operador, tratando esses dados só em nome dela e para fornecer as funções da plataforma (LGPD,
        art. 5º, VI e VII, e art. 39). As regras desse tratamento estão nos Termos de Uso. Se você é funcionário ou
        fornecedor de uma empresa usuária, dirija seus pedidos a ela; os pedidos que chegarem ao nosso canal serão
        encaminhados à empresa.
      </p>

      <h2>2. Quais dados tratamos</h2>
      <p><strong>Cadastro</strong></p>
      <ul>
        <li>Nome e e-mail.</li>
        <li>Fuso horário do seu aparelho, informado no cadastro, para calcular datas de vencimento e o dia de envio dos e-mails.</li>
        <li>Senha: guardada só como hash (bcrypt), que não permite recuperar a senha.</li>
        <li>
          CPF ou CNPJ: o número não é guardado. Guardamos um código de verificação gerado a partir dele (hash HMAC com
          chave secreta), usado para login por CPF/CNPJ, para impedir cadastro duplicado e para conferir o documento quando
          você conecta um banco, e o tipo (CPF ou CNPJ). Esse código ainda é um dado pessoal (pseudonimizado), não um dado
          anônimo.
        </li>
      </ul>

      <p><strong>Dados de bancos conectados (só se você conectar)</strong></p>
      <ul>
        <li>Na hora de conectar, você digita o CPF/CNPJ. Ele é enviado à Polp, que o exige para criar a conexão, e descartado em seguida.</li>
        <li>Contas: nome do produto, tipo, saldo e os 4 últimos dígitos do número. Para CNPJ, também as observações de saldo disponível com a data fornecida pelo banco. O número completo não é guardado.</li>
        <li>Cartões: bandeira, limite, limite usado, dia de fechamento e vencimento, valor e vencimento da fatura.</li>
        <li>
          Transações: valor, data, horário (quando o banco informa), se é entrada ou saída, tipo da operação (Pix, TED,
          boleto, compra, estorno etc.), descrição original enviada pelo banco, se é lançamento futuro e categoria sugerida
          pelo provedor. Em cartão, também o cartão usado (só os 4 últimos dígitos), a parcela, o mês da fatura e o valor em
          moeda estrangeira, quando houver.
        </li>
        <li>
          A outra parte de cada transação, quando informada: nome comercial, razão social e CNPJ da empresa identificados
          pela Polp, se é pessoa física ou jurídica, código do banco e só os 4 últimos dígitos da conta. São dados de
          terceiros. O CPF de pessoas físicas e a agência da outra parte chegam da Polp mas não são guardados; em contas CNPJ,
          guardamos só códigos derivados do CPF e da conta completa (HMAC-SHA256 com chave secreta e o identificador da sua
          empresa; não são anônimos), usados apenas para identificar pagamentos aos funcionários que você cadastrou.
          Quando a Polp não identifica a outra parte, a tela pode mostrar o nome
          que aparece na descrição original do banco, indicando que foi lido da descrição; esse nome não é guardado à
          parte.
        </li>
        <li>
          Investimentos que o banco disponibiliza pelo Open Finance: nome e tipo do produto, características do
          contrato (como indexador, taxa e vencimento, quando informados), posição, histórico de posições e
          movimentações. Também os saldos reservados da conta (caixinhas), com nome, valor e histórico.
        </li>
        <li>
          Só para CPF, contratos de empréstimo que o banco disponibiliza pelo Open Finance: produto, data e valor
          contratado, CET, periodicidade, quantidade de parcelas (pagas, a vencer e em atraso), valor da próxima parcela,
          saldo devedor e datas de vencimento. Número do contrato, garantias e dados de financiamentos não são guardados.
        </li>
      </ul>

      <p><strong>Dados que você cadastra</strong></p>
      <ul>
        <li>Categorias e edições de nome/categoria das transações importadas.</li>
        <li>Metas de economia que você cria na aba Investimentos: nome, valor-alvo e prazo de cada meta. Ficam na sua conta, nos nossos servidores, com nome e valor-alvo cifrados.</li>
        <li>Marcações que você faz em investimentos e caixinhas importados: quais compõem a sua reserva de emergência e a qual meta cada um está vinculado. O valor da reserva e o progresso das metas são calculados a partir dos saldos informados pelos bancos; você não digita esses valores. Se você tiver informado uma regra de rendimento (por exemplo, percentual do CDI), ela também fica guardada.</li>
      </ul>

      <p><strong>Dados que a empresa cadastra (só contas CNPJ)</strong></p>
      <ul>
        <li>
          Funcionários: nome completo, CPF, cargo, departamento, tipo de vínculo (quando informado), data de admissão,
          salário bruto informado pela empresa, situação (ativo ou desligado) e data de desligamento. Nome, CPF e salário
          ficam cifrados.
        </li>
        <li>Fornecedores: razão social, nome fantasia, CPF ou CNPJ e categoria. Nomes e documento ficam cifrados.</li>
        <li>
          Formas de pagamento de funcionários e fornecedores: chave Pix e o tipo dela, ou banco, agência, conta e tipo de
          conta, e o CPF ou CNPJ de quem recebe. Chave Pix, agência, conta e documento de quem recebe ficam cifrados; a
          tela mostra esses dados mascarados, e a chave Pix completa só no detalhe do cadastro.
        </li>
        <li>
          Custos adicionais de cada funcionário: nome e valor digitados pela empresa (13º salário e férias são criados sem
          valor). Nada é calculado pelo Control Finance. Descrição e valor ficam cifrados.
        </li>
        <li>
          Marcação manual “pago este mês” de cada funcionário e o histórico de cada alteração dessa marcação (situação
          anterior e nova, se havia pagamento identificado pelo banco e o valor identificado naquele momento, data e
          hora), além do histórico de inclusão, alteração e exclusão de custos. Os valores ficam cifrados.
        </li>
        <li>
          Fechamentos de caixa: dia, conta (ou todas), saldos inicial e final lidos do banco, total de entradas e saídas
          e, para cada transação do dia, o identificador, o tipo e o valor naquele momento, com data e hora do fechamento
          e de uma eventual reabertura. Os valores ficam cifrados.
        </li>
        <li>Conta padrão da empresa, quando escolhida.</li>
      </ul>

      <p><strong>Identificação de pagamentos a funcionários (só contas CNPJ)</strong></p>
      <ul>
        <li>
          Vale só para funcionários: fornecedores são apenas um cadastro de contatos e formas de pagamento, sem
          acompanhamento de pagamento.
        </li>
        <li>
          Entram só as saídas pagas do mês em contas bancárias (nunca cartão de crédito) e só dados do destinatário
          enviados pelo banco: o CPF ou o CNPJ de quem recebeu e a conta completa (banco, agência, número e dígito). Nome,
          descrição da transação e classificações da Polp nunca são usados.
        </li>
        <li>
          Uma saída é identificada como pagamento a um funcionário quando o CPF ou CNPJ informado pelo banco é igual ao CPF
          do funcionário ou ao CPF/CNPJ de quem recebe em uma forma de pagamento dele (se o banco informar também uma conta
          diferente da conta cadastrada para TED, essa forma de pagamento não vale). Quando o banco não informa documento,
          a conta completa precisa ser igual a uma conta cadastrada para TED, com tipo de pessoa compatível. Se mais de um
          funcionário se encaixar, nenhum é identificado.
        </li>
        <li>
          A tela mostra se houve pagamento identificado no mês e a soma das saídas identificadas. A empresa pode marcar
          manualmente o funcionário como pago ou não pago no mês; a marcação manual prevalece e aparece como manual.
        </li>
        <li>
          Limites: o banco nem sempre envia esses dados, então um pagamento real pode ficar sem identificação. Pagamento
          identificado não significa salário ou obrigação quitados, estornos e devoluções não são descontados, e a falta de
          identificação não significa atraso.
        </li>
      </ul>

      <p><strong>Impostos (só contas CNPJ)</strong></p>
      <p>
        Não há dado novo: a aba Impostos lista saídas já importadas que a Polp classificou como pagamento de tributo e que
        o banco confirma por um fato próprio (o CNPJ da Receita Federal ou do INSS como destinatário, ou o tipo de operação
        de arrecadação). Pagamento listado não significa tributo quitado, e a ausência não significa débito.
      </p>

      <p><strong>Registros de aceite e preferências</strong></p>
      <ul>
        <li>Versão dos Termos de Uso aceita, versão desta Política apresentada, declaração de maioridade e data/hora; cada autorização de conexão bancária (instituição, versão do aviso e data/hora).</li>
        <li>Suas escolhas de e-mail (lembrete de vencimento e aviso mensal).</li>
      </ul>

      <p><strong>Dados técnicos</strong></p>
      <ul>
        <li>Endereço IP: usado só em memória, por até 15 minutos e já convertido em código irreversível, para limitar tentativas de login, cadastro e exclusão de conta.</li>
        <li>Data da sua última atividade (login ou uso do painel com a sessão aberta), registrada no máximo a cada 12 horas, e quais avisos de exclusão por inatividade já foram enviados. Sincronizações bancárias e outros processos automáticos não contam como atividade.</li>
        <li>
          Registros de acesso (data, hora, tipo de evento e IP de cada login): o Marco Civil da Internet (art. 15) obriga
          empresas que exploram aplicações de internet com fins econômicos a guardá-los por pelo menos 6 meses. A gravação
          é ligada quando essa obrigação se aplica ao Control Finance. Quando ligada, o IP é guardado cifrado, os registros
          são apagados automaticamente ao fim do prazo e continuam guardados até esse prazo mesmo se você excluir a conta,
          porque a obrigação legal permanece. A situação em vigor aparece aqui e em Privacidade e dados, no painel.{" "}
          {dataPolicy &&
            (dataPolicy.accessLogEnabled
              ? `Situação atual: gravados, por ${dataPolicy.accessLogRetentionDays} dias.`
              : "Situação atual: não gravados.")}
        </li>
        <li>
          Logs da aplicação: registram erros e eventos técnicos com identificadores internos, sem saldos, extratos, números de
          conta, CPF/CNPJ ou senhas. Registros de acesso mantidos pelo provedor de hospedagem: <strong>[PENDENTE: conteúdo e prazo
          conforme o provedor]</strong>.
        </li>
      </ul>

      <p><strong>Neste navegador</strong></p>
      <ul>
        <li>Um cookie de sessão (essencial, não legível por scripts da página), que expira em até 8 horas ou ao sair.</li>
        <li>No armazenamento local do navegador: identificador interno do usuário, conta selecionada no painel e uma conexão bancária em andamento. São apagados ao sair da conta.</li>
        <li>Metas e reserva registradas antes desta versão ficavam só no navegador. No primeiro acesso depois dela, são levadas para a sua conta e apagadas do navegador assim que ficam guardadas. Se o navegador tiver uma reserva diferente da já salva na conta, você escolhe qual manter.</li>
        <li>Não usamos cookies de publicidade nem ferramentas de análise de navegação.</li>
      </ul>

      <h2>3. Para que usamos e com qual base legal</h2>
      <p>As hipóteses legais (LGPD, art. 7º) adotadas para cada finalidade:</p>
      <ul>
        <li>Criar e manter sua conta e mostrar o painel: execução de contrato (LGPD, art. 7º, V).</li>
        <li>
          Conectar bancos, importar seus dados e usá-los para mostrar saldos, receitas e despesas, cartões, investimentos e
          relatórios e, em contas CNPJ, para o fechamento de caixa, para identificar pagamentos aos funcionários que você
          cadastrou (pelo CPF ou pela conta do destinatário que o banco informa) e para identificar pagamentos de tributos:
          execução de contrato, a seu pedido, e a autorização que você dá no próprio banco pelo Open Finance. Os dados de
          um banco só são usados enquanto essa autorização estiver ativa.
        </li>
        <li>Lembrete de vencimento: execução de contrato; você pode desligar a qualquer momento.</li>
        <li>Aviso mensal: seu consentimento (só é enviado se você ligar a opção); pode ser retirado a qualquer momento.</li>
        <li>Limitar tentativas de acesso e proteger a conta: legítimo interesse (art. 7º, IX, e art. 10, II), usando só o estritamente necessário (para isso, o IP fica em memória por até 15 minutos e não é gravado).</li>
        <li>Registros de acesso, quando gravados: cumprimento de obrigação legal (art. 7º, II; Marco Civil da Internet, art. 15).</li>
        <li>Investimentos e caixinhas importados, e as projeções feitas com eles: execução de contrato, a seu pedido, ao conectar o banco.</li>
        <li>Contratos de empréstimo importados (só CPF): execução de contrato, a seu pedido, ao conectar o banco e autorizar operações de crédito.</li>
        <li>Guardar registros de aceite: exercício regular de direitos (art. 7º, VI).</li>
        <li>Mostrar os detalhes de cada transação importada: execução de contrato (art. 7º, V).</li>
        <li>Dados de terceiros que aparecem nas suas transações (nome, razão social, CNPJ de empresas, tipo de pessoa, código do banco e 4 últimos dígitos da conta da outra parte e, em contas CNPJ, os códigos derivados do CPF e da conta completa dela): legítimo interesse (art. 7º, IX, e art. 10), limitado a mostrar seu próprio extrato e os detalhes de cada transação a você e, em contas CNPJ, a identificar pagamentos aos funcionários que você cadastrou. Guardamos só o necessário para isso, cifrado, e nunca usamos para outra finalidade, como contato, perfil ou publicidade.</li>
        <li>Recursos das contas CNPJ usados pela própria empresa (fechamento de caixa, impostos, conta padrão): execução de contrato com a empresa (art. 7º, V).</li>
        <li>
          Dados de funcionários e fornecedores cadastrados pela empresa, inclusive custos, marcações e históricos: tratados
          pelo Control Finance como operador, em nome da empresa. A base legal é definida pela empresa, que é a
          controladora (seção 1).
        </li>
        <li>Declaração de maioridade: execução de contrato (o serviço é destinado a maiores de 18 anos).</li>
      </ul>
      <p>
        Para cada tratamento baseado em legítimo interesse registramos uma avaliação de finalidade, necessidade e
        balanceamento com os direitos dos titulares (LGPD, art. 10 e art. 37), que pode ser pedida pelo canal de
        privacidade.
      </p>
      <p>Não usamos seus dados para decisões automatizadas sobre você, para publicidade ou para treinar modelos.</p>

      <h2>4. Com quem compartilhamos</h2>
      <ul>
        <li>
          <strong>Polp Tecnologia LTDA</strong> (CNPJ 63.588.124/0001-50, segundo o site da própria Polp): fornecedora da
          integração bancária, atua como operadora. Recebe seu CPF/CNPJ (e, em conexão de empresa, o CPF de quem opera a conta) ao criar a conexão e devolve os dados do banco.
        </li>
        <li>
          <strong>Instituição participante do Open Finance usada pela Polp</strong> (a Polp lista publicamente a Celcoin
          Instituição de Pagamento S.A. e a Pluggy Brasil IP LTDA; qual delas atende o Control Finance:
          <strong> [PENDENTE: confirmação da Polp]</strong>). É quem recebe os dados do seu banco no ecossistema regulado.
        </li>
        <li>
          <strong>Subprocessadores da Polp</strong>, conforme a lista pública dela (polp.com.br/subprocessadores), que inclui
          serviços de hospedagem e, para categorizar transações, a OpenAI (Estados Unidos). Quais deles tratam dados das
          conexões do Control Finance: <strong>[PENDENTE: confirmação da Polp]</strong>.
        </li>
        <li>
          <strong>Resend</strong> (envio de e-mails, processamento nos Estados Unidos): recebe só o seu endereço de e-mail e o
          texto do aviso, que não traz nome, transações nem valores.
        </li>
        <li><strong>Hospedagem da aplicação e do banco de dados</strong>: <strong>[PENDENTE: provedor e região]</strong>.</li>
        <li>Autoridades públicas, quando houver obrigação legal ou ordem judicial.</li>
      </ul>
      <p>
        Dados de funcionários e fornecedores cadastrados por empresas, custos, marcações e fechamentos de caixa ficam só nos
        servidores do Control Finance (hospedagem): não são enviados à Polp, ao banco nem ao serviço de e-mail.
      </p>

      <h2>5. Transferência internacional</h2>
      <p>
        Há transferência para os Estados Unidos no envio de e-mails (Resend) e, conforme a lista pública da Polp, em parte do
        processamento feito pelos subprocessadores dela. Desde agosto de 2025, transferências baseadas em cláusulas
        contratuais precisam usar as cláusulas-padrão aprovadas pela ANPD (LGPD, art. 33, e Resolução CD/ANPD nº 19/2024).
        Mecanismo adotado com a Resend: <strong>[PENDENTE: contrato com cláusulas-padrão da ANPD ou outro fundamento do
        art. 33]</strong>. Mecanismo adotado pela Polp com os subprocessadores dela: <strong>[PENDENTE: confirmação da
        Polp]</strong>. Hospedagem: <strong>[PENDENTE: provedor, região e, se for fora do Brasil, o mecanismo]</strong>.
      </p>

      <h2>6. Como protegemos</h2>
      <ul>
        <li>Saldos, valores, limites, faturas, descrições de transações (inclusive a descrição original do banco), nome comercial, razão social, CNPJ e dígitos de conta da outra parte, dígitos do cartão usado, valor em moeda estrangeira, investimentos e caixinhas importados (nomes e valores de contratos, posições e movimentações), contratos de empréstimo importados, nomes e valores-alvo das metas, IP dos registros de acesso e os dígitos de conta guardados são cifrados (AES-256-GCM) no banco de dados, com chaves que podem ser trocadas periodicamente.</li>
        <li>Comunicação cifrada (HTTPS) entre o navegador, o Control Finance e os fornecedores.</li>
        <li>Sessão em cookie que scripts da página não conseguem ler, com proteção contra requisições forjadas por outros sites.</li>
        <li>Cada pessoa só acessa os próprios dados; tentativas repetidas de login são bloqueadas temporariamente.</li>
        <li>Controle de acesso administrativo ao banco de dados e aos logs: <strong>[PENDENTE: política interna]</strong>.</li>
      </ul>
      <p>Nenhum sistema é imune a falhas. Em caso de incidente de segurança que possa causar risco ou dano relevante, comunicaremos você e a ANPD, como exige a LGPD (art. 48).</p>

      <h2>7. Por quanto tempo guardamos</h2>
      <ul>
        <li>Cadastro, reserva de emergência, metas de economia, edições e preferências: até você excluir a conta (ou remover o item).</li>
        <li>Dados de um banco conectado, inclusive investimentos, caixinhas e contratos de empréstimo: até você desconectar esse banco ou excluir a conta. Ao desconectar, apagamos na hora os dados importados dele (em contas CNPJ, veja a exceção logo abaixo). Se a autorização expirar ou for revogada no banco, esses dados deixam de ser usados no painel e em qualquer cálculo assim que o Control Finance recebe o novo status, ficam só guardados (e entram na cópia que você baixa em Privacidade e dados) e são apagados após 30 dias, como numa desconexão. Para voltar a usar, conecte o banco de novo. Um prazo máximo para o histórico importado (extrato, movimentações de investimento e histórico de posições e de caixinhas) pode ser configurado; o prazo em vigor aparece em Privacidade e dados, no painel.</li>
        <li>Marcação de reserva e regras de rendimento em aplicações importadas: até você removê-las, desconectar o banco da aplicação ou excluir a conta.</li>
        <li>
          Contas CNPJ: funcionários (que só podem ser excluídos depois de desligados), fornecedores, formas de pagamento e
          custos ficam até a empresa excluí-los ou excluir a conta. As marcações de pagamento e os históricos de marcações e
          de custos ficam até a exclusão da conta, mesmo depois de o funcionário ser excluído. Fechamentos de caixa ficam até
          a exclusão da conta ou até o prazo máximo do histórico bancário, quando configurado; um fechamento que usou dados
          de um banco é apagado quando esse banco é desconectado ou quando os dados dele são eliminados ao fim da
          autorização. O valor identificado pelo banco guardado no histórico de marcações é apagado nos mesmos casos (dos
          meses em que aquele banco tinha transações) e quando o mês sai do prazo máximo do histórico bancário; a marcação
          e o registro da alteração continuam.
        </li>
        <li>
          Conta sem nenhuma atividade por 3 anos: é excluída, com todos os dados dela (inclusive, em contas CNPJ, cadastros
          de funcionários e fornecedores e demais registros da empresa), pelo mesmo processo da exclusão pedida por você.
          Antes, enviamos três avisos por e-mail, com 90, 30 e 7 dias de antecedência, informando a data prevista. Para
          impedir a exclusão, basta entrar na conta antes dessa data. Se um aviso não puder ser enviado, a exclusão não
          acontece; se um aviso sair atrasado, a data é adiada para manter a antecedência informada.
        </li>
        <li>Registros de aceite: enquanto a conta existir; são apagados junto com ela.</li>
        <li>Registros de acesso, quando gravados: pelo prazo informado na seção 2, mesmo depois da exclusão da conta.</li>
        <li>Identificador de sessão encerrada: até o horário em que a sessão expiraria (no máximo 8 horas).</li>
        <li>
          Pedido de exclusão à Polp que não pôde ser enviado na hora: guardamos só o identificador técnico da conexão, sem
          nenhum dado seu, até a Polp aceitar o pedido.
        </li>
        <li>Cópias de segurança da hospedagem: <strong>[PENDENTE: prazo de retenção do provedor]</strong>. Os dados saem delas quando a cópia expira.</li>
        <li>Na Polp: ao receber o pedido de exclusão, ela exclui a conexão e os dados vinculados de forma assíncrona. Prazo final, inclusive em cópias de segurança dela: <strong>[PENDENTE: confirmação da Polp]</strong>.</li>
      </ul>

      <h2>8. Seus direitos e como exercer</h2>
      <p>Pela LGPD (art. 18), você pode pedir confirmação de tratamento, acesso, correção, anonimização, bloqueio ou eliminação de dados desnecessários, portabilidade, informação sobre compartilhamento, informação sobre a possibilidade de não consentir e revogação do consentimento, além de se opor a tratamentos irregulares.</p>
      <ul>
        <li><strong>No painel, em Privacidade e dados:</strong> baixar uma cópia de todos os seus dados, desconectar bancos, desligar e-mails, encerrar sessões em todos os aparelhos e excluir a conta.</li>
        <li><strong>Pelo canal de privacidade</strong> (suporte@controlfinance.app.br): correção de nome ou e-mail e qualquer outro pedido. Respondemos no prazo da LGPD (art. 19).</li>
        <li>Você também pode revogar a autorização de Open Finance diretamente no aplicativo do seu banco.</li>
        <li>Você pode apresentar reclamação à Autoridade Nacional de Proteção de Dados (ANPD).</li>
      </ul>
      <p>
        Funcionários e fornecedores de empresas usuárias exercem esses direitos perante a empresa, que é a controladora dos
        dados deles e pode consultar, corrigir, exportar e excluir os cadastros no painel. Pedidos que chegarem ao nosso
        canal serão encaminhados à empresa.
      </p>

      <h2>9. Desconectar um banco e excluir a conta</h2>
      <p>
        <strong>Desconectar um banco</strong> apaga do Control Finance as contas, cartões, extrato, investimentos,
        caixinhas e contratos de empréstimo que vieram dele e pede à Polp que exclua a conexão. Sua conta no Control Finance continua existindo.
        Em contas CNPJ, também são apagados os fechamentos de caixa que usaram dados desse banco e os valores dele guardados
        no histórico de marcações; cadastros de funcionários e fornecedores, custos e marcações de pagamento (com o
        histórico delas) continuam guardados.
      </p>
      <p>
        <strong>Excluir a conta</strong> apaga todos os seus dados dos nossos servidores, inclusive os cadastros de
        funcionários e fornecedores e os demais registros da empresa, e pede à Polp a exclusão de todas as
        suas conexões. Não dá para desfazer. A única exceção são os registros de acesso, quando gravados, que a lei obriga
        a guardar até o fim do prazo (seção 2).
      </p>
      <p>
        Nos dois casos, a exclusão do lado da Polp é feita por ela de forma assíncrona; se ela estiver indisponível, o pedido é
        reenviado automaticamente. A tela informa se o pedido foi aceito ou ficou pendente.
      </p>

      <h2>10. Idade</h2>
      <p>
        O Control Finance é destinado a maiores de 18 anos, e o cadastro pede essa declaração. Não tratamos
        intencionalmente dados de crianças ou adolescentes; se você souber de uma conta de menor de idade, avise pelo
        canal de privacidade para que ela seja excluída.
      </p>

      <h2>11. Alterações desta política</h2>
      <p>
        Cada versão tem data e número. Quando houver mudança relevante, avisaremos por e-mail ou no painel antes de ela valer e,
        quando o tratamento depender do seu consentimento, pediremos de novo.
      </p>
    </LegalPageLayout>
  );
}
