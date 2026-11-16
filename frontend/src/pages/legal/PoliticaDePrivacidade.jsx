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
    <LegalPageLayout kicker="Legal" title="Política de privacidade" updatedAt={`14 de novembro de 2026 (versão ${PRIVACY_VERSION})`}>
      <h2>Resumo</h2>
      <ul>
        <li>Coletamos o mínimo para o painel funcionar. O número do seu CPF/CNPJ não fica guardado, e não pedimos telefone.</li>
        <li>Se você conectar um banco, guardamos contas, saldos, extrato, dados de cartão, investimentos e caixinhas, cifrados, enquanto a conexão existir.</li>
        <li>Os dados passam só por fornecedores indispensáveis ao serviço, listados abaixo. Não vendemos dados e não usamos para publicidade.</li>
        <li>No painel, em <strong>Privacidade e dados</strong>, você baixa uma cópia de tudo, desconecta bancos, desliga e-mails e exclui a conta.</li>
      </ul>

      <h2>O que mudou nesta versão (16 de novembro de 2026)</h2>
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
        em <strong>[PENDENTE]</strong>, responsável pela plataforma Control Finance.
      </p>
      <p>
        Canal para assuntos de privacidade e para exercer seus direitos: <strong>suporte@controlfinance.app.br</strong>.
        Encarregado pelo tratamento de dados pessoais: <strong>[PENDENTE: nome ou canal do encarregado, ou registro do
        enquadramento como agente de pequeno porte]</strong>.
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
          terceiros. O CPF de pessoas físicas e a agência da outra parte chegam da Polp mas não são guardados. Quando a Polp não identifica a outra parte, a tela pode mostrar o nome
          que aparece na descrição original do banco, indicando que foi lido da descrição; esse nome não é guardado à
          parte.
        </li>
        <li>
          Investimentos que o banco disponibiliza pelo Open Finance: nome e tipo do produto, características do
          contrato (como indexador, taxa e vencimento, quando informados), posição, histórico de posições e
          movimentações. Também os saldos reservados da conta (caixinhas), com nome, valor e histórico.
        </li>
      </ul>

      <p><strong>Dados que você cadastra</strong></p>
      <ul>
        <li>Categorias e edições de nome/categoria das transações importadas.</li>
        <li>Metas de economia que você cria na aba Investimentos: nome, valor-alvo e prazo de cada meta. Ficam na sua conta, nos nossos servidores, com nome e valor-alvo cifrados.</li>
        <li>Marcações que você faz em investimentos e caixinhas importados: quais compõem a sua reserva de emergência e a qual meta cada um está vinculado. O valor da reserva e o progresso das metas são calculados a partir dos saldos informados pelos bancos; você não digita esses valores. Se você tiver informado uma regra de rendimento (por exemplo, percentual do CDI), ela também fica guardada.</li>
      </ul>

      <p><strong>Registros de aceite e preferências</strong></p>
      <ul>
        <li>Versão dos Termos de Uso aceita, versão desta Política apresentada, declaração de maioridade e data/hora; cada autorização de conexão bancária (instituição, versão do aviso e data/hora).</li>
        <li>Suas escolhas de e-mail (lembrete de vencimento e aviso mensal).</li>
      </ul>

      <p><strong>Dados técnicos</strong></p>
      <ul>
        <li>Endereço IP: usado só em memória, por até 15 minutos e já convertido em código irreversível, para limitar tentativas de login, cadastro e exclusão de conta.</li>
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
        <li>Conectar bancos e importar seus dados: execução de contrato, a seu pedido, e a autorização que você dá no próprio banco pelo Open Finance.</li>
        <li>Lembrete de vencimento: execução de contrato; você pode desligar a qualquer momento.</li>
        <li>Aviso mensal: seu consentimento (só é enviado se você ligar a opção); pode ser retirado a qualquer momento.</li>
        <li>Limitar tentativas de acesso e proteger a conta: legítimo interesse (art. 7º, IX, e art. 10, II), usando só o estritamente necessário (para isso, o IP fica em memória por até 15 minutos e não é gravado).</li>
        <li>Registros de acesso, quando gravados: cumprimento de obrigação legal (art. 7º, II; Marco Civil da Internet, art. 15).</li>
        <li>Investimentos e caixinhas importados, e as projeções feitas com eles: execução de contrato, a seu pedido, ao conectar o banco.</li>
        <li>Guardar registros de aceite: exercício regular de direitos (art. 7º, VI).</li>
        <li>Mostrar os detalhes de cada transação importada: execução de contrato (art. 7º, V).</li>
        <li>Dados de terceiros que aparecem nas suas transações (nome, razão social, CNPJ de empresas, tipo de pessoa, código do banco e 4 últimos dígitos da conta da outra parte): legítimo interesse (art. 7º, IX, e art. 10), limitado a mostrar seu próprio extrato e os detalhes de cada transação a você. Guardamos só o necessário para isso, cifrado, e nunca usamos para outra finalidade, como contato, perfil ou publicidade.</li>
        <li>Declaração de maioridade: execução de contrato (o serviço é destinado a maiores de 18 anos).</li>
      </ul>
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

      <h2>5. Transferência internacional</h2>
      <p>
        Há transferência para os Estados Unidos no envio de e-mails (Resend) e, conforme a lista pública da Polp, em parte do
        processamento feito pelos subprocessadores dela. Mecanismo adotado (LGPD, art. 33, e Resolução CD/ANPD nº 19/2024):
        <strong> [PENDENTE: cláusulas-padrão contratuais ou outro fundamento]</strong>. Hospedagem:
        <strong> [PENDENTE: confirmar se há transferência]</strong>.
      </p>

      <h2>6. Como protegemos</h2>
      <ul>
        <li>Saldos, valores, limites, faturas, descrições de transações (inclusive a descrição original do banco), nome comercial, razão social, CNPJ e dígitos de conta da outra parte, dígitos do cartão usado, valor em moeda estrangeira, investimentos e caixinhas importados (nomes e valores de contratos, posições e movimentações), nomes e valores-alvo das metas, IP dos registros de acesso e os dígitos de conta guardados são cifrados (AES-256-GCM) no banco de dados, com chaves que podem ser trocadas periodicamente.</li>
        <li>Comunicação cifrada (HTTPS) entre o navegador, o Control Finance e os fornecedores.</li>
        <li>Sessão em cookie que scripts da página não conseguem ler, com proteção contra requisições forjadas por outros sites.</li>
        <li>Cada pessoa só acessa os próprios dados; tentativas repetidas de login são bloqueadas temporariamente.</li>
        <li>Controle de acesso administrativo ao banco de dados e aos logs: <strong>[PENDENTE: política interna]</strong>.</li>
      </ul>
      <p>Nenhum sistema é imune a falhas. Em caso de incidente de segurança que possa causar risco ou dano relevante, comunicaremos você e a ANPD, como exige a LGPD (art. 48).</p>

      <h2>7. Por quanto tempo guardamos</h2>
      <ul>
        <li>Cadastro, reserva de emergência, metas de economia, edições e preferências: até você excluir a conta (ou remover o item).</li>
        <li>Dados de um banco conectado, inclusive investimentos e caixinhas: até você desconectar esse banco ou excluir a conta. Ao desconectar, apagamos na hora tudo que veio dele. Um prazo máximo para o histórico importado (extrato, movimentações de investimento e histórico de posições e de caixinhas) pode ser configurado; o prazo em vigor aparece em Privacidade e dados, no painel.</li>
        <li>Marcação de reserva e regras de rendimento em aplicações importadas: até você removê-las, desconectar o banco da aplicação ou excluir a conta.</li>
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

      <h2>9. Desconectar um banco e excluir a conta</h2>
      <p>
        <strong>Desconectar um banco</strong> apaga do Control Finance as contas, cartões, extrato, investimentos e
        caixinhas que vieram dele e pede à Polp que exclua a conexão. Sua conta no Control Finance continua existindo.
      </p>
      <p>
        <strong>Excluir a conta</strong> apaga todos os seus dados dos nossos servidores e pede à Polp a exclusão de todas as
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
