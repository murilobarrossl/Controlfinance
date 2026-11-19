import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate } from "react-router";
import { useAuth } from "../../../context/AuthContext.jsx";
import { isBusinessHolder } from "../../../utils/holder.js";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import { useAccount } from "../../../context/AccountContext.jsx";
import { logoutAllSessions } from "../../../api/auth.js";
import { downloadMyData, updateNotificationPreferences, deleteMyAccount, getDataPolicy } from "../../../api/account.js";
import { clearLocalDataFor, setCurrentUserId } from "../../../utils/authToken.js";
import Button from "../../../components/ui/Button/Button.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import ConfirmDialog from "../../../components/ui/ConfirmDialog/ConfirmDialog.jsx";
import DeleteAccountDialog from "./DeleteAccountDialog.jsx";
import "./Privacidade.css";
import usePageTitle from "../../../hooks/usePageTitle.js";
import { ChevronDownIcon } from "../../../components/ui/icons/FeatureIcons.jsx";

const SETTINGS = [
  { id: "data", label: "Seus dados", description: "Saiba quais informações são armazenadas e por quanto tempo." },
  { id: "banks", label: "Bancos conectados", description: "Consulte suas conexões bancárias e gerencie o acesso aos dados." },
  { id: "notifications", label: "Avisos por e-mail", description: "Escolha quais avisos deseja receber do Control Finance." },
  { id: "export", label: "Baixar seus dados", description: "Obtenha uma cópia das informações da sua conta em formato JSON." },
  { id: "sessions", label: "Sessões e acesso", description: "Encerre o acesso à sua conta nos aparelhos e navegadores conectados." },
  { id: "delete", label: "Excluir conta", description: "Entenda o que será apagado antes de solicitar a exclusão definitiva." },
];

function PrivacySection({ id, active, title, children, className = "" }) {
  const setting = SETTINGS.find((item) => item.id === id);
  return (
    <section id={`privacy-${id}`} hidden={!active} aria-labelledby={`privacy-title-${id}`} className={`privacidade__section ${className}`}>
      <h2 id={`privacy-title-${id}`} className="privacidade__section-title" tabIndex={-1}>{title}</h2>
      <p className="privacidade__section-description">{setting.description}</p>
      <div className="privacidade__section-body">{children}</div>
    </section>
  );
}

// Tudo que o titular pode fazer sobre os próprios dados, num lugar só e funcionando de verdade
// (cada botão chama um endpoint real; nenhum só muda texto na tela).
export default function Privacidade() {
  usePageTitle("Privacidade e dados");
  const navigate = useNavigate();
  const { user, logout, updateUser } = useAuth();
  const { accounts, connections: bankConnections, connectionsStatus, disconnectConnection } = useAccount();

  const [activeSection, setActiveSection] = useState("data");
  const [mobileDetail, setMobileDetail] = useState(false);
  const detailRef = useRef(null);
  const menuRef = useRef(null);

  function openSection(id) {
    setActiveSection(id);
    setMobileDetail(true);
    requestAnimationFrame(() => detailRef.current?.querySelector("section:not([hidden]) h2")?.focus());
  }

  function backToSettings() {
    setMobileDetail(false);
    requestAnimationFrame(() => menuRef.current?.querySelector('[aria-current="true"]')?.focus());
  }

  const [prefs, setPrefs] = useState({
    dueRemindersEnabled: user?.dueRemindersEnabled ?? true,
    monthlySummaryEnabled: user?.monthlySummaryEnabled ?? false,
  });
  const [prefsStatus, setPrefsStatus] = useState("");
  const [exportStatus, setExportStatus] = useState("");
  const [pendingDisconnect, setPendingDisconnect] = useState(null);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleteError, setDeleteError] = useState("");
  const [deleting, setDeleting] = useState(false);
  const [dataPolicy, setDataPolicy] = useState(null);

  useEffect(() => {
    const controller = new AbortController();
    getDataPolicy({ signal: controller.signal })
      .then(setDataPolicy)
      .catch(() => {});
    return () => controller.abort();
  }, []);

  const connections = useMemo(() => {
    return bankConnections.map((c) => ({
        id: c.id,
        label: c.institutionName || "Instituição conectada",
        accountNames: accounts.filter((a) => a.localIntegrationId === c.id).map((a) => a.name),
        lastSyncedAt: c.lastSyncedAt,
      }));
  }, [accounts, bankConnections]);

  async function handleSavePrefs(e) {
    e.preventDefault();
    setPrefsStatus("");
    try {
      const updated = await updateNotificationPreferences(prefs);
      updateUser(updated);
      setPrefsStatus("Preferências salvas.");
    } catch (err) {
      setPrefsStatus(err.message || "Não foi possível salvar.");
    }
  }

  async function handleExport() {
    setExportStatus("");
    try {
      await downloadMyData();
      setExportStatus("Arquivo baixado.");
    } catch (err) {
      setExportStatus(err.message || "Não foi possível gerar a cópia dos dados.");
    }
  }

  async function handleLogoutAll() {
    try {
      await logoutAllSessions();
    } catch {
      // mesmo se falhar, encerra a sessão deste navegador
    }
    logout();
    navigate("/loginemail", { replace: true });
  }

  const closeDelete = useCallback(() => {
    if (deleting) return;
    setDeleteOpen(false);
    setDeleteError("");
  }, [deleting, setDeleteOpen, setDeleteError]);

  async function handleDeleteAccount(password) {
    setDeleteError("");
    setDeleting(true);
    try {
      await deleteMyAccount(password);
      if (user?.id) clearLocalDataFor(user.id);
      setCurrentUserId(null);
      logout();
      navigate("/", { replace: true });
    } catch (err) {
      setDeleteError(err.message || "Não foi possível excluir a conta.");
      setDeleting(false);
    }
  }

  return (
    <div className={`privacidade${mobileDetail ? " privacidade--detail" : ""}`}>
      <SectionHeading title="Privacidade e dados" level={1} />

      <div className="privacidade__settings">
        <aside className="privacidade__menu" ref={menuRef}>
          <h2 className="privacidade__menu-title">Configurações</h2>
          <nav aria-label="Configurações de privacidade">
            {SETTINGS.map((item) => (
              <button
                key={item.id}
                type="button"
                className={`privacidade__menu-item${item.id === activeSection ? " privacidade__menu-item--active" : ""}`}
                aria-current={item.id === activeSection ? "true" : undefined}
                aria-controls={`privacy-${item.id}`}
                onClick={() => openSection(item.id)}
              >
                <span>{item.label}</span>
                <span className="privacidade__menu-chevron" aria-hidden="true"><ChevronDownIcon /></span>
              </button>
            ))}
          </nav>
        </aside>
        <div className="privacidade__content" ref={detailRef}>
          <button type="button" className="privacidade__back" onClick={backToSettings}>
            <span aria-hidden="true"><ChevronDownIcon /></span>
            Voltar às configurações
          </button>

      <PrivacySection id="data" active={activeSection === "data"} title="O que guardamos sobre você">
        <ul className="privacidade__list">
          <li><strong>Cadastro:</strong> nome, e-mail, senha (só em forma irreversível), fuso horário e o tipo de documento. O número do CPF/CNPJ não fica guardado, só um código de verificação gerado a partir dele.</li>
          <li><strong>Bancos conectados:</strong> contas, saldos, extrato (com horário, tipo da operação e, quando o banco informa, nome, banco e 4 últimos dígitos da conta da outra parte), dados de cartão, investimentos, caixinhas e os 4 últimos dígitos do número da conta, cifrados, enquanto a autorização no banco estiver ativa. Se ela expirar ou for revogada no banco, os dados deixam de ser usados e são apagados após 30 dias.{isBusinessHolder(user) ? " Inclui observações de saldo disponível com data do banco, sujeitas ao mesmo prazo do histórico bancário. Quando o banco informa o CPF ou a conta completa do destinatário de uma saída, guardamos só códigos derivados deles (HMAC com chave secreta e o identificador da sua empresa; não são anônimos), usados apenas para identificar pagamentos aos funcionários que você cadastrou. Pagamento identificado não significa obrigação quitada." :" Inclui contratos de empréstimo (produto, valores contratados, CET, parcelas, saldo devedor e vencimentos), quando você autoriza operações de crédito; são só informativos e não entram nos totais."}</li>
          <li><strong>O que você cadastra:</strong> categorias e edições de nome/categoria das transações importadas.</li>
          {isBusinessHolder(user) && (
            <li><strong>Funcionários, fornecedores e caixa:</strong> cadastros de funcionários (nome, CPF, cargo, departamento, vínculo, admissão, salário bruto e desligamento) e de fornecedores (nomes, CPF ou CNPJ e categoria), formas de pagamento, custos adicionais, marcações de pagamento com o histórico de alterações e fechamentos de caixa. A empresa decide sobre os dados de funcionários e fornecedores; o Control Finance os trata em nome dela. Ficam até você excluí-los ou excluir a conta; fechamentos de caixa que usaram dados de um banco saem quando ele é desconectado, e o histórico de marcações continua.</li>
          )}
          <li><strong>Aceites:</strong> qual versão dos Termos e da Política você aceitou e quando, e cada autorização de conexão bancária.</li>
          <li><strong>Inatividade:</strong> a data do seu último acesso. Uma conta sem nenhum acesso por 3 anos é excluída com todos os dados, depois de avisos por e-mail com 90, 30 e 7 dias de antecedência; basta entrar na conta antes da data informada para mantê-la.</li>
          <li>
            <strong>Prazo do histórico bancário:</strong>{" "}
            {dataPolicy === null
              ? <Skeleton width="60%" />
              : dataPolicy.syncedHistoryMonths
              ? `extrato, movimentações de investimento e histórico de posições e de caixinhas com mais de ${dataPolicy.syncedHistoryMonths} meses não são importados e são apagados automaticamente${isBusinessHolder(user) ? ", assim como os fechamentos de caixa e os valores do banco no histórico de marcações desse período" : ""}. A posição atual dos investimentos e o que você cadastrou não são afetados.`
              : "sem prazo máximo definido: o histórico importado fica guardado enquanto a autorização no banco estiver ativa."}
          </li>
          {dataPolicy?.accessLogEnabled && (
            <li>
              <strong>Registros de acesso:</strong> data, hora e IP (cifrado) de cada login, guardados por{" "}
              {dataPolicy.accessLogRetentionDays} dias por obrigação legal (Marco Civil da Internet) e depois apagados
              automaticamente, mesmo que você exclua a conta antes.
            </li>
          )}
          <li><strong>Reserva de emergência e metas de economia:</strong> as metas (nome, valor-alvo e prazo) ficam na sua conta, nos nossos servidores, com nome e valor-alvo cifrados, até você removê-las em Investimentos ou excluir a conta. Da reserva e das metas guardamos só quais investimentos você vinculou a cada uma, apagados junto com a conexão bancária ou a conta. Os valores vêm sempre dos saldos informados pelos bancos.</li>
          <li><strong>Neste navegador:</strong> a conta selecionada no painel e uma conexão bancária em andamento, só para facilitar o uso. São apagados ao sair da conta.</li>
        </ul>
        <p className="privacidade__text">
          Detalhes, fornecedores envolvidos e prazos estão na{" "}
          <Link to="/politica-de-privacidade" target="_blank" rel="noopener noreferrer">Política de privacidade</Link>.
        </p>
      </PrivacySection>

      <PrivacySection id="banks" active={activeSection === "banks"} title="Bancos conectados">
        {connectionsStatus === "loading" ? (
          <Skeleton width="100%" height="4rem" />
        ) : connectionsStatus === "error" ? (
          <p className="privacidade__text">Não foi possível carregar as conexões bancárias.</p>
        ) : connections.length === 0 ? (
          <p className="privacidade__text">Nenhum banco conectado.</p>
        ) : (
          <ul className="privacidade__connections">
            {connections.map((c) => (
              <li key={c.id}>
                <div>
                  <strong>{c.label}</strong>
                  <span>{c.accountNames.join(" · ")}</span>
                  {c.lastSyncedAt && (
                    <span>Última sincronização: {new Date(c.lastSyncedAt).toLocaleString("pt-BR")}</span>
                  )}
                </div>
                <Button as="button" type="button" variant="outline" size="sm" onClick={() => setPendingDisconnect(c)}>
                  Desconectar
                </Button>
              </li>
            ))}
          </ul>
        )}
        <p className="privacidade__hint">
          Desconectar apaga do Control Finance tudo que veio daquele banco, inclusive investimentos, caixinhas e contratos de empréstimo, e solicita a
          exclusão da conexão ao serviço de integração bancária.
          Sua conta no Control Finance continua existindo.
          {isBusinessHolder(user) && " Fechamentos de caixa que usaram dados desse banco também são apagados; cadastros e marcações de pagamento continuam guardados."}
        </p>
      </PrivacySection>

      <PrivacySection id="notifications" active={activeSection === "notifications"} title="Avisos por e-mail">
        <form className="privacidade__prefs" onSubmit={handleSavePrefs}>
          <label>
            <input
              type="checkbox"
              checked={prefs.dueRemindersEnabled}
              onChange={(e) => setPrefs({ ...prefs, dueRemindersEnabled: e.target.checked })}
            />
            <span>
              <strong>Lembrete de vencimento</strong> — avisa quantas contas pendentes vencem nos próximos 3 dias,
              sem nomes nem valores.
            </span>
          </label>
          <label>
            <input
              type="checkbox"
              checked={prefs.monthlySummaryEnabled}
              onChange={(e) => setPrefs({ ...prefs, monthlySummaryEnabled: e.target.checked })}
            />
            <span>
              <strong>Aviso mensal</strong> — no dia 1º, avisa que o resumo do mês está no painel, sem números.
            </span>
          </label>
          <p className="privacidade__hint">
            Os e-mails são enviados pela Resend, um serviço com processamento nos Estados Unidos. Por isso eles
            só levam o seu endereço de e-mail e um aviso genérico.
          </p>
          <div className="privacidade__actions">
            <Button as="button" type="submit" variant="primary" size="sm">
              Salvar preferências
            </Button>
            {prefsStatus && <span className="privacidade__status" role="status">{prefsStatus}</span>}
          </div>
        </form>
      </PrivacySection>

      <PrivacySection id="export" active={activeSection === "export"} title="Cópia dos seus dados">
        <p className="privacidade__text">
          Baixe um arquivo com tudo que guardamos sobre você no servidor: cadastro, aceites, contas, lançamentos,
          cartões, categorias, investimentos e caixinhas, reserva de emergência, metas, regras de
          rendimento e, se existirem, registros de acesso.
        </p>
        <div className="privacidade__actions">
          <Button as="button" type="button" variant="outline" size="sm" onClick={handleExport}>
            Baixar meus dados (JSON)
          </Button>
          {exportStatus && <span className="privacidade__status" role="status">{exportStatus}</span>}
        </div>
      </PrivacySection>

      <PrivacySection id="sessions" active={activeSection === "sessions"} title="Sessões">
        <p className="privacidade__text">
          Encerra o acesso em todos os aparelhos e navegadores onde você está conectado, inclusive este.
        </p>
        <Button as="button" type="button" variant="outline" size="sm" onClick={handleLogoutAll}>
          Sair de todos os aparelhos
        </Button>
      </PrivacySection>

      <PrivacySection id="delete" active={activeSection === "delete"} title="Excluir conta" className="privacidade__danger">
        <p className="privacidade__text">
          Apaga de forma definitiva sua conta e tudo que está guardado nos nossos servidores: cadastro, aceites,
          bancos conectados, contas, lançamentos, cartões, categorias, investimentos e caixinhas importados,
          reserva de emergência, metas de economia e regras de rendimento informadas{isBusinessHolder(user) ? ", além de funcionários, fornecedores, custos, marcações de pagamento e fechamentos de caixa" : ""}. Também solicitamos ao serviço de integração bancária a exclusão das suas
          conexões bancárias. Não dá para desfazer.
        </p>
        <p className="privacidade__hint">
          Cópias de segurança da hospedagem só deixam de conter seus dados quando expiram (o prazo está na Política
          de Privacidade).{" "}
          {dataPolicy?.accessLogEnabled && "Os registros de acesso ficam até o fim do prazo legal. "}
          Se quiser uma cópia dos seus dados, baixe antes de excluir.
        </p>
        <div className="privacidade__actions privacidade__delete-actions">
          <Button as="button" type="button" variant="danger" size="sm" onClick={() => setDeleteOpen(true)}>
            Excluir minha conta
          </Button>
        </div>
      </PrivacySection>

        </div>
      </div>

      {deleteOpen && (
        <DeleteAccountDialog
          deleting={deleting}
          error={deleteError}
          onConfirm={handleDeleteAccount}
          onClose={closeDelete}
        />
      )}

      <ConfirmDialog
        confirmVariant="danger"
        open={pendingDisconnect !== null}
        title={`Desconectar ${pendingDisconnect?.label}?`}
        message={`Todas as contas e cartões desta conexão saem do Control Finance, e apagamos na hora o saldo, o extrato, os investimentos e as caixinhas que vieram desse banco${isBusinessHolder(user) ? ", além dos fechamentos de caixa que usaram esses dados" : ""}. Também solicitamos ao serviço de integração bancária a exclusão da conexão.`}
        confirmLabel="Desconectar e apagar dados"
        onConfirm={() => {
          const target = pendingDisconnect;
          setPendingDisconnect(null);
          disconnectConnection(target.id);
        }}
        onClose={() => setPendingDisconnect(null)}
      />
    </div>
  );
}
