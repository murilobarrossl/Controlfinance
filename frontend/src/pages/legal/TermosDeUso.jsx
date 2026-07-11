import LegalPageLayout from "./LegalPageLayout.jsx";

export default function TermosDeUso() {
  return (
    <LegalPageLayout kicker="Legal" title="Termos de uso" updatedAt="julho de 2026">
      <p>
        Estes Termos de Uso regem o acesso e a utilização da plataforma Control Finance
        ("Control Finance", "nós" ou "plataforma") por você ("usuário"). Ao criar uma conta,
        você concorda com as condições descritas abaixo.
      </p>

      <h2>1. O que é o Control Finance</h2>
      <p>
        O Control Finance é uma plataforma de organização financeira pessoal que permite
        cadastrar contas, cartões e transações manualmente ou conectar contas bancárias via
        Open Finance, através de instituições parceiras homologadas pelo Banco Central do
        Brasil, para consolidar e visualizar suas movimentações em um só lugar.
      </p>

      <h2>2. Cadastro e conta</h2>
      <ul>
        <li>Você é responsável por manter a confidencialidade da sua senha e pelo uso da sua conta.</li>
        <li>As informações fornecidas no cadastro (nome, e-mail, telefone, CPF/CNPJ) devem ser verdadeiras e atualizadas.</li>
        <li>Cada CPF/CNPJ e e-mail podem estar associados a apenas uma conta na plataforma.</li>
      </ul>

      <h2>3. Conexão com instituições financeiras</h2>
      <p>
        Ao optar por conectar uma conta bancária, você autoriza expressamente o
        compartilhamento dos seus dados financeiros através do ecossistema Open Finance,
        conforme a Resolução Conjunta nº 1/2020 do Banco Central. Essa autorização pode ser
        revogada a qualquer momento, diretamente na plataforma ou junto à instituição
        conectada.
      </p>

      <h2>4. Uso permitido</h2>
      <p>
        A plataforma deve ser usada apenas para fins pessoais e lícitos. É proibido tentar
        acessar contas de terceiros, realizar engenharia reversa, ou usar a plataforma de
        forma que sobrecarregue ou comprometa sua infraestrutura.
      </p>

      <h2>5. Limitação de responsabilidade</h2>
      <p>
        O Control Finance é uma ferramenta de organização e visualização financeira — não
        somos uma instituição financeira, não custodiamos recursos e não realizamos
        transações em seu nome. Decisões financeiras tomadas com base nas informações da
        plataforma são de sua exclusiva responsabilidade.
      </p>

      <h2>6. Cancelamento</h2>
      <p>
        Você pode encerrar sua conta a qualquer momento. Após o cancelamento, seus dados
        são tratados conforme descrito na nossa{" "}
        <a href="/politica-de-privacidade">Política de Privacidade</a>.
      </p>

      <h2>7. Alterações destes termos</h2>
      <p>
        Podemos atualizar estes Termos periodicamente. Alterações relevantes serão
        comunicadas por e-mail ou por aviso na própria plataforma antes de entrarem em vigor.
      </p>
    </LegalPageLayout>
  );
}
