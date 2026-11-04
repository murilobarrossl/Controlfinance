import LegalPageLayout from "./LegalPageLayout.jsx";
import { TERMS_VERSION } from "../../legal/versions.js";

// Descreve o serviço como ele existe hoje. Itens [PENDENTE] dependem de informação que ainda não
// existe (dados da empresa, condições comerciais). Mudou o texto, muda a versão em legal/versions.js e em
// backend/Services/LegalDocuments.cs.
export const TERMS_UPDATED_AT = `31 de outubro de 2026 (versão ${TERMS_VERSION})`;

export default function TermosDeUso() {
  return (
    <LegalPageLayout kicker="Legal" title="Termos de uso" updatedAt={TERMS_UPDATED_AT}>
      <TermosDeUsoTexto />
    </LegalPageLayout>
  );
}

export function TermosDeUsoTexto() {
  return (
    <>
      <p>
        Estes Termos regem o uso da plataforma Control Finance, oferecida por <strong>[PENDENTE: razão social]</strong>, CNPJ{" "}
        <strong>[PENDENTE]</strong>, endereço <strong>[PENDENTE]</strong> ("Control Finance" ou "nós"). Contato:
        suporte@controlfinance.app.br. Ao criar uma conta, você aceita estes Termos. O tratamento dos seus dados está descrito na{" "}
        <a href="/politica-de-privacidade">Política de Privacidade</a>.
      </p>

      <h2>1. O que é o Control Finance</h2>
      <p>
        Uma ferramenta de organização e visualização financeira para pessoas físicas e pequenos negócios. Você cadastra contas,
        cartões e lançamentos manualmente e, se quiser, conecta bancos para importar saldos, extratos e dados de cartões.
      </p>
      <ul>
        <li>O Control Finance <strong>não é instituição financeira nem de pagamento</strong>: não guarda dinheiro, não faz pagamentos ou transferências e não concede crédito.</li>
        <li>O Control Finance <strong>não é participante do Open Finance</strong>: a conexão com bancos é feita por um fornecedor (Polp Tecnologia) que usa uma instituição participante.</li>
        <li>O Control Finance <strong>não faz recomendação nem consultoria de investimentos</strong>. Textos sobre modalidades de investimento são explicativos e genéricos, e o simulador de financiamento é uma estimativa.</li>
      </ul>

      <h2>2. Cadastro</h2>
      <ul>
        <li>O serviço é destinado a maiores de 18 anos, e o cadastro exige essa declaração.</li>
        <li>As informações do cadastro devem ser verdadeiras. Cada e-mail e cada CPF/CNPJ só pode estar em uma conta.</li>
        <li>Você é responsável por manter sua senha em sigilo. Se suspeitar de acesso indevido, use "Sair de todos os aparelhos" em Privacidade e dados e fale com o suporte.</li>
      </ul>

      <h2>3. Conexão com bancos</h2>
      <ul>
        <li>Conectar um banco é opcional. A autorização é dada por você no ambiente do próprio banco, pelo Open Finance.</li>
        <li>Os dados exibidos são os informados pelo banco, por meio do fornecedor, na última sincronização. A sincronização acontece quando você abre o painel ou pede uma atualização (no máximo a cada 5 minutos por banco); não é em tempo real. Dados podem chegar incompletos ou atrasados quando o banco ou o fornecedor não os enviam.</li>
        <li>A conexão depende do fornecedor de integração e dos bancos. Ela pode ficar indisponível temporariamente; nesse caso, os dados já importados continuam visíveis, mas não são atualizados.</li>
        <li>Você pode desconectar um banco a qualquer momento no painel. Ao desconectar, apagamos do Control Finance os dados que vieram dele e pedimos ao fornecedor que exclua a conexão. Você também pode revogar a autorização no aplicativo do banco.</li>
      </ul>

      <h2>4. Uso permitido</h2>
      <p>
        Use a plataforma apenas para organizar suas próprias finanças, pessoais ou do seu negócio, de forma lícita. É proibido
        tentar acessar dados de outras pessoas, conectar contas de terceiros sem autorização, fazer engenharia reversa ou
        sobrecarregar a infraestrutura.
      </p>

      <h2>5. Planos e pagamento</h2>
      <p>
        A contratação paga ainda não está disponível pelo site, e nenhuma cobrança é feita hoje. Antes de qualquer cobrança,
        publicaremos as condições comerciais completas: preço, forma de pagamento, renovação, reajuste, teste gratuito,
        cancelamento, reembolso e o direito de desistir em 7 dias previsto no Código de Defesa do Consumidor (art. 49).
        <strong> [PENDENTE: condições comerciais e gateway de pagamento]</strong>
      </p>

      <h2>6. Responsabilidades</h2>
      <ul>
        <li>Respondemos pelo funcionamento do serviço e pela segurança dos dados sob nossa guarda, nos termos do Código de Defesa do Consumidor e da LGPD. Nada nestes Termos afasta direitos que a lei garante a você.</li>
        <li>Como os dados bancários vêm do banco por meio do fornecedor, confira valores importantes também no aplicativo do banco antes de tomar decisões.</li>
        <li>As decisões financeiras que você tomar são suas; o Control Finance organiza e mostra informações, sem recomendar produtos ou operações.</li>
      </ul>

      <h2>7. Encerramento</h2>
      <ul>
        <li>Você pode excluir sua conta a qualquer momento em Privacidade e dados. Os efeitos estão descritos na Política de Privacidade.</li>
        <li>Podemos suspender ou encerrar contas que violem estes Termos, com aviso prévio sempre que possível.</li>
      </ul>

      <h2>8. Propriedade intelectual</h2>
      <p>
        O software, a marca e o conteúdo do Control Finance pertencem a <strong>[PENDENTE: titular]</strong>. Nomes e logotipos
        de bancos exibidos na plataforma pertencem às respectivas instituições e aparecem só para identificar a instituição
        conectada, sem indicar parceria.
      </p>

      <h2>9. Alterações destes Termos</h2>
      <p>
        Cada versão tem data e número. Mudanças relevantes serão avisadas por e-mail ou no painel antes de valerem. Se você não
        concordar, pode excluir a conta sem custo.
      </p>

      <h2>10. Lei aplicável e foro</h2>
      <p>
        Aplica-se a lei brasileira. Se você for consumidor, pode propor ação no foro do seu domicílio (Código de Defesa
        do Consumidor, art. 101, I).
      </p>
    </>
  );
}
