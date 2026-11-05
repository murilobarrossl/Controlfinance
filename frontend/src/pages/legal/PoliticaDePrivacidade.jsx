import LegalPageLayout from "./LegalPageLayout.jsx";
import { PRIVACY_VERSION } from "../../legal/versions.js";
import usePageTitle from "../../hooks/usePageTitle.js";

// Este texto descreve o que o código faz hoje. Ao mudar como um dado é coletado, guardado ou
// compartilhado, atualize aqui E a versão em legal/versions.js + backend/Services/LegalDocuments.cs.
// Itens [PENDENTE] dependem de informação que ainda não existe (dados da empresa, contratos,
// fornecedores): não preencher com suposições.
export default function PoliticaDePrivacidade() {
  usePageTitle("Política de privacidade");
  return (
    <LegalPageLayout kicker="Legal" title="Política de privacidade" updatedAt={`31 de outubro de 2026 (versão ${PRIVACY_VERSION})`}>
      <h2>Resumo</h2>
      <ul>
        <li>Coletamos o mínimo para o painel funcionar. O número do seu CPF/CNPJ não fica guardado, e não pedimos telefone.</li>
        <li>Se você conectar um banco, guardamos contas, saldos, extrato e dados de cartão, cifrados, enquanto a conexão existir.</li>
        <li>Os dados passam só por fornecedores indispensáveis ao serviço, listados abaixo. Não vendemos dados e não usamos para publicidade.</li>
        <li>No painel, em <strong>Privacidade e dados</strong>, você baixa uma cópia de tudo, desconecta bancos, desliga e-mails e exclui a conta.</li>
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
        <li>Contas: nome do produto, tipo, saldo e os 4 últimos dígitos do número. O número completo não é guardado.</li>
        <li>Cartões: bandeira, limite, limite usado, dia de fechamento e vencimento, valor e vencimento da fatura.</li>
        <li>
          Transações: descrição, estabelecimento, valor, data, situação e categoria sugerida pelo provedor. Em transferências,
          a descrição pode trazer o nome da outra pessoa envolvida (dado de terceiro), que é mostrado só a você.
        </li>
      </ul>

      <p><strong>Dados que você cadastra</strong></p>
      <ul>
        <li>Contas e cartões manuais, lançamentos, parcelamentos (a descrição é guardada cifrada), categorias e edições de nome/categoria.</li>
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
          Registros de acesso (data, hora e IP de cada login): hoje a aplicação não grava. O Marco Civil da Internet (art. 15)
          obriga empresas que exploram aplicações de internet com fins econômicos a guardá-los por 6 meses; quando essa
          obrigação passar a valer para o Control Finance, eles serão gravados com o IP cifrado, apagados automaticamente após
          6 meses e informados aqui.
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
        <li>No armazenamento local do navegador: identificador interno do usuário, conta selecionada no painel, metas de economia e reserva de emergência que você registra, e uma conexão bancária em andamento. Metas e reserva ficam só neste aparelho e não vão para os nossos servidores.</li>
        <li>Não usamos cookies de publicidade nem ferramentas de análise de navegação.</li>
      </ul>

      <h2>3. Para que usamos e com qual base legal</h2>
      <p>As hipóteses legais (LGPD, art. 7º) adotadas para cada finalidade:</p>
      <ul>
        <li>Criar e manter sua conta e mostrar o painel: execução de contrato (LGPD, art. 7º, V).</li>
        <li>Conectar bancos e importar seus dados: execução de contrato, a seu pedido, e a autorização que você dá no próprio banco pelo Open Finance.</li>
        <li>Lembrete de vencimento: execução de contrato; você pode desligar a qualquer momento.</li>
        <li>Aviso mensal: seu consentimento (só é enviado se você ligar a opção); pode ser retirado a qualquer momento.</li>
        <li>Limitar tentativas de acesso e proteger a conta: legítimo interesse (art. 7º, IX, e art. 10, II), usando só o estritamente necessário (o IP fica em memória por até 15 minutos e não é gravado).</li>
        <li>Guardar registros de aceite: exercício regular de direitos (art. 7º, VI).</li>
        <li>Nomes de terceiros que aparecem em transferências: legítimo interesse (art. 7º, IX), limitado a mostrar seu próprio extrato a você, cifrados no banco de dados e nunca usados para outra finalidade.</li>
        <li>Declaração de maioridade: execução de contrato (o serviço é destinado a maiores de 18 anos).</li>
      </ul>
      <p>Não usamos seus dados para decisões automatizadas sobre você, para publicidade ou para treinar modelos.</p>

      <h2>4. Com quem compartilhamos</h2>
      <ul>
        <li>
          <strong>Polp Tecnologia LTDA</strong> (CNPJ 63.588.124/0001-50, segundo o site da própria Polp): fornecedora da
          integração bancária, atua como operadora. Recebe seu CPF/CNPJ ao criar a conexão e devolve os dados do banco.
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
        <li>Saldos, valores, limites, faturas, descrições de transações e de parcelamentos e os dígitos de conta guardados são cifrados (AES-256-GCM) no banco de dados, com chaves que podem ser trocadas periodicamente.</li>
        <li>Comunicação cifrada (HTTPS) entre o navegador, o Control Finance e os fornecedores.</li>
        <li>Sessão em cookie que scripts da página não conseguem ler, com proteção contra requisições forjadas por outros sites.</li>
        <li>Cada pessoa só acessa os próprios dados; tentativas repetidas de login são bloqueadas temporariamente.</li>
        <li>Controle de acesso administrativo ao banco de dados e aos logs: <strong>[PENDENTE: política interna]</strong>.</li>
      </ul>
      <p>Nenhum sistema é imune a falhas. Em caso de incidente de segurança que possa causar risco ou dano relevante, comunicaremos você e a ANPD, como exige a LGPD (art. 48).</p>

      <h2>7. Por quanto tempo guardamos</h2>
      <ul>
        <li>Cadastro, lançamentos manuais e preferências: até você excluir a conta (ou remover o item).</li>
        <li>Dados de um banco conectado: até você desconectar esse banco ou excluir a conta. Ao desconectar, apagamos na hora tudo que veio dele. Um prazo máximo para o histórico importado pode ser configurado; o prazo em vigor aparece em Privacidade e dados, no painel.</li>
        <li>Registros de aceite: enquanto a conta existir; são apagados junto com ela.</li>
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
        <li><strong>No painel, em Privacidade e dados:</strong> baixar uma cópia de todos os seus dados, desconectar bancos, desligar e-mails, encerrar sessões em todos os aparelhos, apagar os dados guardados no navegador e excluir a conta.</li>
        <li><strong>Pelo canal de privacidade</strong> (suporte@controlfinance.app.br): correção de nome ou e-mail e qualquer outro pedido. Respondemos no prazo da LGPD (art. 19).</li>
        <li>Você também pode revogar a autorização de Open Finance diretamente no aplicativo do seu banco.</li>
        <li>Você pode apresentar reclamação à Autoridade Nacional de Proteção de Dados (ANPD).</li>
      </ul>

      <h2>9. Desconectar um banco e excluir a conta</h2>
      <p>
        <strong>Desconectar um banco</strong> apaga do Control Finance as contas, cartões, extrato e parcelamentos que vieram
        dele e pede à Polp que exclua a conexão. Sua conta no Control Finance continua existindo.
      </p>
      <p>
        <strong>Excluir a conta</strong> apaga todos os seus dados dos nossos servidores e pede à Polp a exclusão de todas as
        suas conexões. Não dá para desfazer.
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
