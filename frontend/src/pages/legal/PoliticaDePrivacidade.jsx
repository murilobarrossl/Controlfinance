import LegalPageLayout from "./LegalPageLayout.jsx";

export default function PoliticaDePrivacidade() {
  return (
    <LegalPageLayout kicker="Legal" title="Política de privacidade" updatedAt="julho de 2026">
      <p>
        Esta Política de Privacidade explica como o Control Finance coleta, usa, armazena e
        protege seus dados pessoais, em conformidade com a Lei Geral de Proteção de Dados
        (Lei nº 13.709/2018 — LGPD).
      </p>

      <h2>1. Dados que coletamos</h2>
      <ul>
        <li><strong>Dados de cadastro:</strong> nome, e-mail, telefone e CPF/CNPJ.</li>
        <li><strong>Dados financeiros:</strong> contas, cartões, transações e categorias que você cadastra manualmente ou sincroniza via Open Finance.</li>
        <li><strong>Dados de uso:</strong> informações técnicas de acesso, como endereço IP, para fins de segurança e prevenção de fraude.</li>
      </ul>

      <h2>2. Como protegemos seus dados</h2>
      <p>
        Dados sensíveis — como CPF/CNPJ e valores financeiros — são armazenados de forma
        criptografada em nosso banco de dados. O CPF/CNPJ é adicionalmente protegido por um
        hash de busca que impede sua recuperação a partir do banco de dados. Senhas nunca
        são armazenadas em texto puro.
      </p>

      <h2>3. Como usamos seus dados</h2>
      <ul>
        <li>Para fornecer as funcionalidades da plataforma (dashboards, relatórios, categorização de gastos).</li>
        <li>Para enviar lembretes de vencimento e resumos mensais, quando aplicável.</li>
        <li>Para prevenir fraudes e proteger a segurança da sua conta.</li>
      </ul>
      <p>Não vendemos seus dados pessoais ou financeiros a terceiros.</p>

      <h2>4. Compartilhamento com terceiros</h2>
      <p>
        Utilizamos provedores de infraestrutura (hospedagem e banco de dados) e um parceiro
        homologado de Open Finance para sincronizar dados bancários mediante sua autorização
        explícita. Esses parceiros têm acesso apenas ao necessário para prestar o serviço
        contratado e estão sujeitos a obrigações de confidencialidade.
      </p>

      <h2>5. Seus direitos</h2>
      <p>Conforme a LGPD, você pode a qualquer momento solicitar:</p>
      <ul>
        <li>Confirmação da existência de tratamento e acesso aos seus dados.</li>
        <li>Correção de dados incompletos, inexatos ou desatualizados.</li>
        <li>Exclusão dos seus dados, encerrando sua conta.</li>
        <li>Revogação do consentimento de conexão via Open Finance.</li>
      </ul>

      <h2>6. Retenção de dados</h2>
      <p>
        Mantemos seus dados enquanto sua conta estiver ativa. Após o encerramento, os dados
        são removidos ou anonimizados, exceto quando a retenção for exigida por obrigação
        legal.
      </p>

      <h2>7. Contato</h2>
      <p>
        Dúvidas sobre esta política ou sobre o tratamento dos seus dados podem ser
        encaminhadas para o e-mail de suporte da plataforma.
      </p>
    </LegalPageLayout>
  );
}
