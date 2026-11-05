import { useCallback, useEffect, useMemo, useState } from "react";
import { Link, useNavigate } from "react-router";
import { useAuth } from "../../../context/AuthContext.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import { useAccount } from "../../../context/AccountContext.jsx";
import { logoutAllSessions } from "../../../api/auth.js";
import { downloadMyData, updateNotificationPreferences, deleteMyAccount, getDataPolicy } from "../../../api/account.js";
import { clearLocalDataFor, setCurrentUserId } from "../../../utils/authToken.js";
import { groupAccounts } from "../../../utils/accountGrouping.js";
import Card from "../../../components/ui/Card/Card.jsx";
import Button from "../../../components/ui/Button/Button.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import ConfirmDialog from "../../../components/ui/ConfirmDialog/ConfirmDialog.jsx";
import DeleteAccountDialog from "./DeleteAccountDialog.jsx";
import "./Privacidade.css";
import usePageTitle from "../../../hooks/usePageTitle.js";

// Tudo que o titular pode fazer sobre os próprios dados, num lugar só e funcionando de verdade
// (cada botão chama um endpoint real; nenhum só muda texto na tela).
export default function Privacidade() {
  usePageTitle("Privacidade e dados");
  const navigate = useNavigate();
  const { user, logout, updateUser } = useAuth();
  const { accounts, connectors, accountsStatus, disconnectConnection } = useAccount();

  const [prefs, setPrefs] = useState({
    dueRemindersEnabled: user?.dueRemindersEnabled ?? true,
    monthlySummaryEnabled: user?.monthlySummaryEnabled ?? false,
  });
  const [prefsStatus, setPrefsStatus] = useState("");
  const [exportStatus, setExportStatus] = useState("");
  const [pendingDisconnect, setPendingDisconnect] = useState(null);
  const [confirmClearLocal, setConfirmClearLocal] = useState(false);
  const [localCleared, setLocalCleared] = useState(false);
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
    const connectorsById = new Map(connectors.map((c) => [c.id, c]));
    return groupAccounts(accounts, connectorsById)
      .filter((g) => g.accounts[0].localIntegrationId)
      .map((g) => ({
        id: g.accounts[0].localIntegrationId,
        label: g.label ?? g.accounts[0].name,
        accountNames: g.accounts.map((a) => a.name),
        lastSyncedAt: g.accounts[0].lastSyncedAt,
      }));
  }, [accounts, connectors]);

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

  function handleClearLocal() {
    if (user?.id) clearLocalDataFor(user.id);
    setConfirmClearLocal(false);
    setLocalCleared(true);
  }

  const closeDelete = useCallback(() => {
    if (deleting) return;
    setDeleteOpen(false);
    setDeleteError("");
  }, [deleting]);

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
    <div className="privacidade">
      <SectionHeading title="Privacidade e dados">Seus dados, suas escolhas</SectionHeading>

      <Card title="O que guardamos sobre você">
        <ul className="privacidade__list">
          <li><strong>Cadastro:</strong> nome, e-mail, senha (só em forma irreversível) e o tipo de documento. O número do CPF/CNPJ não fica guardado, só um código de verificação gerado a partir dele.</li>
          <li><strong>Bancos conectados:</strong> contas, saldos, extrato, dados de cartão e os 4 últimos dígitos do número da conta, cifrados, enquanto a conexão existir.</li>
          <li><strong>O que você cadastra:</strong> contas e cartões manuais, lançamentos, parcelamentos e categorias.</li>
          <li><strong>Aceites:</strong> qual versão dos Termos e da Política você aceitou e quando, e cada autorização de conexão bancária.</li>
          <li>
            <strong>Prazo do histórico bancário:</strong>{" "}
            {dataPolicy === null
              ? <Skeleton width="60%" />
              : dataPolicy.syncedHistoryMonths
              ? `transações importadas dos bancos com mais de ${dataPolicy.syncedHistoryMonths} meses não são importadas e são apagadas automaticamente (lançamentos manuais não são afetados).`
              : "sem prazo máximo definido: o histórico importado fica guardado enquanto o banco estiver conectado."}
          </li>
          {dataPolicy?.accessLogEnabled && (
            <li>
              <strong>Registros de acesso:</strong> data, hora e IP (cifrado) de cada login, guardados por{" "}
              {dataPolicy.accessLogRetentionDays} dias e depois apagados automaticamente.
            </li>
          )}
          <li><strong>Neste navegador:</strong> metas de economia, reserva de emergência e a conta selecionada no painel. Esses dados ficam só aqui, não vão para os nossos servidores.</li>
        </ul>
        <p className="privacidade__text">
          Detalhes, fornecedores envolvidos e prazos estão na{" "}
          <Link to="/politica-de-privacidade" target="_blank" rel="noopener noreferrer">Política de privacidade</Link>.
        </p>
      </Card>

      <Card title="Bancos conectados">
        {accountsStatus === "loading" ? (
          <Skeleton width="100%" height="4rem" />
        ) : accountsStatus === "error" ? (
          <p className="privacidade__text">Não foi possível carregar as contas.</p>
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
          Desconectar apaga do Control Finance tudo que veio daquele banco e pede à Polp que exclua a conexão.
          Sua conta no Control Finance continua existindo.
        </p>
      </Card>

      <Card title="Avisos por e-mail">
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
            {prefsStatus && <span className="privacidade__status">{prefsStatus}</span>}
          </div>
        </form>
      </Card>

      <Card title="Cópia dos seus dados">
        <p className="privacidade__text">
          Baixe um arquivo com tudo que guardamos sobre você no servidor: cadastro, aceites, contas, lançamentos,
          cartões, parcelamentos e categorias.
        </p>
        <div className="privacidade__actions">
          <Button as="button" type="button" variant="outline" size="sm" onClick={handleExport}>
            Baixar meus dados (JSON)
          </Button>
          {exportStatus && <span className="privacidade__status">{exportStatus}</span>}
        </div>
      </Card>

      <Card title="Sessões">
        <p className="privacidade__text">
          Encerra o acesso em todos os aparelhos e navegadores onde você está conectado, inclusive este.
        </p>
        <Button as="button" type="button" variant="outline" size="sm" onClick={handleLogoutAll}>
          Sair de todos os aparelhos
        </Button>
      </Card>

      <Card title="Dados guardados neste navegador">
        <p className="privacidade__text">
          Metas de economia, reserva de emergência e a conta selecionada ficam só neste navegador. Apagar aqui
          remove esses dados deste aparelho de forma definitiva (não existe cópia nos nossos servidores).
        </p>
        <div className="privacidade__actions">
          <Button as="button" type="button" variant="outline" size="sm" onClick={() => setConfirmClearLocal(true)}>
            Apagar dados deste navegador
          </Button>
          {localCleared && <span className="privacidade__status">Dados deste navegador apagados.</span>}
        </div>
      </Card>

      <Card title="Excluir conta" className="privacidade__danger">
        <p className="privacidade__text">
          Apaga de forma definitiva sua conta e tudo que está guardado nos nossos servidores: cadastro, aceites,
          bancos conectados, contas, lançamentos, cartões, parcelamentos e categorias. Também pedimos à Polp que
          exclua suas conexões bancárias. Não dá para desfazer.
        </p>
        <p className="privacidade__hint">
          Cópias de segurança da hospedagem só deixam de conter seus dados quando expiram (o prazo está na Política
          de Privacidade). Se quiser uma cópia dos seus dados, baixe antes de excluir.
        </p>
        <div className="privacidade__actions privacidade__delete-actions">
          <Button as="button" type="button" variant="primary" size="sm" onClick={() => setDeleteOpen(true)}>
            Excluir minha conta
          </Button>
        </div>
      </Card>

      {deleteOpen && (
        <DeleteAccountDialog
          deleting={deleting}
          error={deleteError}
          onConfirm={handleDeleteAccount}
          onClose={closeDelete}
        />
      )}

      <ConfirmDialog
        open={pendingDisconnect !== null}
        title={`Desconectar ${pendingDisconnect?.label}?`}
        message="Todas as contas e cartões desta conexão saem do Control Finance, e apagamos na hora o saldo, o extrato e os parcelamentos que vieram desse banco. Também pedimos à Polp que exclua a conexão."
        confirmLabel="Desconectar e apagar dados"
        onConfirm={() => {
          const target = pendingDisconnect;
          setPendingDisconnect(null);
          disconnectConnection(target.id);
        }}
        onClose={() => setPendingDisconnect(null)}
      />

      <ConfirmDialog
        open={confirmClearLocal}
        title="Apagar dados deste navegador?"
        message="Suas metas de economia e a reserva de emergência registradas neste navegador serão apagadas de forma definitiva."
        confirmLabel="Apagar"
        onConfirm={handleClearLocal}
        onClose={() => setConfirmClearLocal(false)}
      />
    </div>
  );
}
