import { publicIntegrationMessage } from "../../utils/integrationMessages.js";
import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate } from "react-router";
import Button from "../../components/ui/Button/Button.jsx";
import BankLogo from "../../components/ui/BankLogo/BankLogo.jsx";
import { ShieldIcon, SyncIcon } from "../../components/ui/icons/FeatureIcons.jsx";
import { EyeSlashIcon } from "../../components/ui/icons/EyeIcons.jsx";
import { getConnectors, createIntegration, getIntegrationStatus } from "../../api/polp.js";
import { filterConnectorsByName, groupConnectorsByType } from "../../utils/bankSorting.js";
import { applyMask, getCpfCnpjMask, CNPJ_MASK, CPF_MASK } from "../../utils/masks.js";
import { BANK_CONNECTION_NOTICE_VERSION } from "../../legal/versions.js";
import { useAuth } from "../../context/AuthContext.jsx";
import { isValidCnpj, isValidCpf } from "../../utils/authRules.js";
import { getHolderType, HOLDER_CNPJ, HOLDER_CPF, HOLDER_DOCUMENT_LENGTH } from "../../utils/holder.js";
import { setPendingIntegrationId, watchPolpConnection, wait } from "../../services/polpConnection.js";
import logo from "../../assets/images/cflogobranco.svg";
import "./ConectarBanco.css";
import usePageTitle from "../../hooks/usePageTitle.js";

const POLL_MAX_ATTEMPTS = 10;
const POLL_INTERVAL_MS = 1000;

function isSafeHttpUrl(url) {
  try {
    return ["http:", "https:"].includes(new URL(url).protocol);
  } catch {
    return false;
  }
}

export default function ConnectBankPage() {
  usePageTitle("Conectar banco");
  const navigate = useNavigate();
  // O backend só aceita o documento do cadastro; com o tipo conhecido, a tela pede só ele e mostra
  // só os bancos que atendem esse tipo de titular.
  const holderType = getHolderType(useAuth()?.user);
  const documentMaxDigits = holderType ? HOLDER_DOCUMENT_LENGTH[holderType] : 14;
  const [step, setStep] = useState("intro"); // "intro" | "choosing"
  const [connectors, setConnectors] = useState([]);
  const [personType, setPersonType] = useState("personal"); // "personal" | "business"
  const [search, setSearch] = useState("");
  const [loadingConnectors, setLoadingConnectors] = useState(false);
  const [connectingId, setConnectingId] = useState(null);
  const [selectedConnectorId, setSelectedConnectorId] = useState(null);
  const [connectingStage, setConnectingStage] = useState(null); // "creating" | "waiting"
  const [error, setError] = useState("");
  const cancelledRef = useRef(false);

  useEffect(() => {
    cancelledRef.current = false;
    return () => {
      cancelledRef.current = true;
    };
  }, []);
  // CPF/CNPJ só vive neste estado, durante a conexão: vai pra Polp na criação da integração e some
  // quando a tela é desmontada. Não é guardado no navegador nem no backend.
  const [document, setDocument] = useState("");
  // Só pra empresa (CNPJ): a Polp exige o CPF de quem opera a conta, além do CNPJ. Mesmo ciclo de
  // vida do documento acima: só em memória, nunca guardado.
  const [operatorDocument, setOperatorDocument] = useState("");
  const [noticeAccepted, setNoticeAccepted] = useState(false);

  const { personal, business } = useMemo(
    () => groupConnectorsByType(connectors),
    [connectors]
  );

  const visibleConnectors = useMemo(
    () => filterConnectorsByName(personType === "personal" ? personal : business, search),
    [personType, personal, business, search]
  );

  const selectedConnector = visibleConnectors.find((connector) => connector.id === selectedConnectorId) ?? null;

  const isBusinessDocument = document.replace(/\D/g, "").length === 14;

  function handleOperatorDocumentChange(e) {
    const digits = e.target.value.replace(/\D/g, "").slice(0, 11);
    setOperatorDocument(applyMask(digits, getCpfCnpjMask(digits), operatorDocument.replace(/\D/g, "")));
  }

  function handleDocumentChange(e) {
    const digits = e.target.value.replace(/\D/g, "").slice(0, documentMaxDigits);
    setDocument(applyMask(digits, getCpfCnpjMask(digits), document.replace(/\D/g, "")));
  }

  async function handleStart(e) {
    e.preventDefault();
    setError("");
    const digits = document.replace(/\D/g, "");
    if (holderType && digits.length !== HOLDER_DOCUMENT_LENGTH[holderType]) {
      setError(holderType === HOLDER_CNPJ ? "Informe o CNPJ da empresa, o mesmo do cadastro." : "Informe o seu CPF, o mesmo do cadastro.");
      return;
    }
    if (digits.length !== 11 && digits.length !== 14) {
      setError("Informe o CPF ou CNPJ do titular da conta.");
      return;
    }
    // Mesmo dígito verificador que o backend confere: um número digitado errado avisa aqui, antes de
    // abrir a aba do banco e gastar uma tentativa.
    if (digits.length === 14 ? !isValidCnpj(digits) : !isValidCpf(digits)) {
      setError(digits.length === 14 ? "CNPJ inválido. Confira os números digitados." : "CPF inválido. Confira os números digitados.");
      return;
    }
    if (digits.length === 14 && !isValidCpf(operatorDocument.replace(/\D/g, ""))) {
      setError("Informe um CPF válido de quem opera a conta da empresa no banco.");
      return;
    }
    setLoadingConnectors(true);
    try {
      const data = await getConnectors();
      const list = Array.isArray(data) ? data : data?.data ?? [];
      setConnectors(list);
      setPersonType(digits.length === 14 ? "business" : "personal");
      setStep("choosing");
    } catch (err) {
      setError(err.message || "Não foi possível carregar os bancos disponíveis.");
    } finally {
      setLoadingConnectors(false);
    }
  }

  async function handleSelectConnector(connectorId) {
    setError("");
    setConnectingId(connectorId);
    setConnectingStage("creating");

    // Precisa ser chamado de forma síncrona, antes de qualquer await, senão o navegador
    // perde o "gesto do usuário" do clique e bloqueia a aba como pop-up.
    const authWindow = window.open("", "_blank");
    if (!authWindow) {
      setError("Não foi possível abrir a aba de autenticação. Verifique o bloqueador de pop-ups do navegador.");
      setConnectingId(null);
      setConnectingStage(null);
      return;
    }
    authWindow.focus();

    // Corta a referência window.opener da aba de autenticação (que logo vai navegar pra uma
    // página de terceiro, o banco/Polp) sem perder o handle que este script usa pra
    // escrever, navegar e fechar a aba. Sem isso, a página do banco poderia redirecionar
    // esta aba original via window.opener.location ("reverse tabnabbing").
    try {
      authWindow.opener = null;
    } catch {
      // navegadores mais antigos podem não permitir, não é bloqueante
    }

    authWindow.document.write(`
      <!doctype html>
      <html>
        <head><title>Conectando ao banco...</title></head>
        <body style="margin:0;height:100vh;display:flex;align-items:center;justify-content:center;background:#0d0d0d;color:#b8b8b8;font-family:sans-serif;">
          <p>Preparando a conexão com o banco, só um instante...</p>
        </body>
      </html>
    `);
    authWindow.document.close();

    try {
      const data = await createIntegration(
        connectorId,
        document.replace(/\D/g, ""),
        BANK_CONNECTION_NOTICE_VERSION,
        isBusinessDocument ? operatorDocument.replace(/\D/g, "") : undefined,
      );
      if (data?.urlToAuthenticate) {
        await openAuthAndWait(data.integrationId, data.urlToAuthenticate, authWindow);
      } else {
        await pollForAuthUrl(data.integrationId, authWindow);
      }
    } catch (err) {
      authWindow.close();
      setError(err.message || "Não foi possível conectar ao banco. Tente novamente.");
    } finally {
      setConnectingId(null);
      setConnectingStage(null);
    }
  }

  async function pollForAuthUrl(integrationId, authWindow) {
    setConnectingStage("waiting");

    for (let attempt = 0; attempt < POLL_MAX_ATTEMPTS && !cancelledRef.current; attempt++) {
      await wait(POLL_INTERVAL_MS);
      const status = await getIntegrationStatus(integrationId);

      if (status?.urlToAuthenticate) {
        await openAuthAndWait(integrationId, status.urlToAuthenticate, authWindow);
        return;
      }

      if (status?.status === "syncing" || status?.status === "active") {
        authWindow.close();
        await watchAuthorised(integrationId);
        return;
      }

      if (["login_error", "outdated", "revoked"].includes(status?.status)) {
        authWindow.close();
        setError(publicIntegrationMessage(status.error) || "Não foi possível autenticar com o banco. Tente novamente.");
        return;
      }
    }

    authWindow.close();
    setError("A conexão demorou para responder. Tente novamente em instantes.");
  }

  async function openAuthAndWait(integrationId, authUrl, authWindow) {
    // Confia na Polp pra devolver uma URL de autenticação legítima, mas nunca navega a aba
    // com um esquema que não seja http/https: uma resposta comprometida da API (ou de um
    // proxy no meio do caminho) não deveria conseguir rodar algo como javascript: nessa aba.
    if (!isSafeHttpUrl(authUrl)) {
      authWindow.close();
      setError("Não foi possível iniciar a autenticação com o banco. Tente novamente.");
      return;
    }

    setPendingIntegrationId(integrationId);
    authWindow.location.href = authUrl;
    authWindow.focus();
    await watchAuthorised(integrationId);
  }

  async function watchAuthorised(integrationId) {
    setPendingIntegrationId(integrationId);
    setConnectingStage("waiting");

    const result = await watchPolpConnection(integrationId, { isCancelled: () => cancelledRef.current });

    if (cancelledRef.current) return;

    if (result === "active") {
      navigate("/dashboard");
    } else if (result === "login_error") {
      setError("Não foi possível autenticar com o banco. Tente novamente.");
    } else {
      setError("A conexão demorou para responder. Tente novamente em instantes.");
    }
  }

  return (
    <div className="connect-bank">
      <main className="connect-bank__content">
        <header className="connect-bank__header">
          <div className="connect-bank__brand"><img src={logo} alt="" className="connect-bank__logo" /><span>Control Finance</span></div>
          <Link to="/dashboard" className="connect-bank__return">Voltar ao painel</Link>
        </header>
        <div className="connect-bank__heading">
          <h1 className="connect-bank__title">{step === "intro" ? "Conectar banco" : "Escolha sua instituição financeira"}</h1>
          <p className="connect-bank__description">
            {step === "intro"
              ? "Confira como seus dados serão utilizados antes de iniciar a conexão bancária."
              : "Escolha a instituição e clique em Conectar para continuar no ambiente do banco."}
          </p>
        </div>

        {connectingStage === "waiting" && (
          <p className="connect-bank__notice" role="status">
            Conclua a autenticação na aba que abrimos. A sincronização pode levar alguns minutos;
            você será levado ao dashboard assim que terminar.
          </p>
        )}

        {error && <p className="connect-bank__error" role="alert">{error}</p>}

        {step === "intro" && (
          <form className="connect-bank__consent" onSubmit={handleStart}>
            <section className="connect-bank__information" aria-labelledby="connection-information">
              <h2 id="connection-information" className="connect-bank__consent-title">Sobre o compartilhamento de dados</h2>
              <dl className="connect-bank__facts">
                <dt>O que vamos acessar</dt>
                <dd>
                  Contas, saldos, extrato de transações e, se houver, cartões de crédito (limite, fatura e
                  vencimento), além de investimentos disponíveis por Open Finance do banco que você escolher.
                </dd>

                <dt>Para quê</dt>
                <dd>
                  Só para montar o seu painel: saldo, receitas e despesas, categorias, cartões, investimentos e relatórios.
                  Não vendemos seus dados e não usamos para publicidade.
                </dd>

                <dt>Quem participa</dt>
                <dd>
                  Você faz login direto no seu banco, pelo Open Finance. A conexão é intermediada pela Polp
                  Tecnologia, nossa fornecedora, que recebe os dados por uma instituição participante do Open
                  Finance e repassa ao Control Finance. Não recebemos a senha do seu banco. Para criar a
                  conexão bancária, é necessário informar seu CPF/CNPJ e, no caso de empresa, também o CPF de quem opera a
                  conta no banco. Esses números são enviados só para criar a conexão e não ficam guardados.
                </dd>

                <dt>O que guardamos</dt>
                <dd>
                  Saldos, extrato, dados de cartão e investimentos disponibilizados pela conexão, e só os 4 últimos dígitos do número da conta, cifrados no
                  nosso banco de dados, enquanto a conexão estiver ativa.
                </dd>

                <dt>Como a sincronização funciona</dt>
                <dd>
                  Depois da primeira importação, os dados são atualizados periodicamente, conforme a agenda
                  de cada banco. Não é em tempo real. Você também pode atualizar o saldo de uma conta no
                  seletor de contas.
                </dd>

                <dt>Como desconectar</dt>
                <dd>
                  A qualquer momento, no seletor de contas ou em Privacidade e dados. Ao desconectar,
                  apagamos na hora tudo que veio desse banco e solicitamos a exclusão da conexão ao serviço de integração bancária. Você
                  também pode revogar a autorização no aplicativo do seu banco, na área de Open Finance.
                </dd>
              </dl>
            </section>

            <div className="connect-bank__identification">
              <h2 className="connect-bank__consent-title">Identificação do titular</h2>
              <p className="connect-bank__field-help">Informe o mesmo documento utilizado no cadastro do Control Finance.</p>
              <label className="connect-bank__field">
                <span className="connect-bank__field-label">
                  {holderType === HOLDER_CNPJ ? "CNPJ da empresa" : holderType === HOLDER_CPF ? "Seu CPF" : "CPF/CNPJ do titular"}
                </span>
                <input
                  id="connect-bank-holder-document"
                  name="holderDocument"
                  type="text"
                  value={document}
                  onChange={handleDocumentChange}
                  inputMode="numeric"
                  maxLength={holderType === HOLDER_CPF ? CPF_MASK.length : CNPJ_MASK.length}
                  autoComplete="off"
                  placeholder="O mesmo informado no cadastro"
                  required
                />
              </label>

              {isBusinessDocument && (
                <label className="connect-bank__field">
                  <span className="connect-bank__field-label">CPF de quem opera a conta da empresa</span>
                  <input
                    id="connect-bank-operator-document"
                    name="operatorDocument"
                    type="text"
                    value={operatorDocument}
                    onChange={handleOperatorDocumentChange}
                    inputMode="numeric"
                    maxLength={14}
                    autoComplete="off"
                    placeholder="CPF de quem acessa o banco"
                    required
                  />
                </label>
              )}

              <label className="connect-bank__checkbox">
                <input
                  type="checkbox"
                  checked={noticeAccepted}
                  onChange={(e) => setNoticeAccepted(e.target.checked)}
                  required
                />
                <span>
                  Li as informações sobre a conexão. O compartilhamento de contas, cartões e investimentos será autorizado diretamente no banco. Detalhes na{" "}
                  <Link to="/politica-de-privacidade" target="_blank" rel="noopener noreferrer">
                    Política de privacidade
                  </Link>
                  .
                </span>
              </label>

              <Button
                as="button"
                type="submit"
                variant="primary"
                size="md"
                className="connect-bank__button"
                disabled={loadingConnectors || !noticeAccepted}
              >
                {loadingConnectors ? "Carregando bancos..." : "Escolher banco"}
              </Button>
            </div>
          </form>
        )}

        {step === "choosing" && (
          <section className="connect-bank__selection" aria-label="Instituições disponíveis">
            <button type="button" className="connect-bank__back" disabled={connectingId !== null} onClick={() => { setStep("intro"); setError(""); }}>
              Voltar à identificação
            </button>
            {!holderType && (
              <div className="connect-bank__tabs" role="tablist">
                <button
                  type="button"
                  role="tab"
                  aria-selected={personType === "personal"}
                  className={`connect-bank__tab ${personType === "personal" ? "connect-bank__tab--active" : ""}`}
                  onClick={() => setPersonType("personal")}
                >
                  Pessoa física
                </button>
                <button
                  type="button"
                  role="tab"
                  aria-selected={personType === "business"}
                  className={`connect-bank__tab ${personType === "business" ? "connect-bank__tab--active" : ""}`}
                  onClick={() => setPersonType("business")}
                >
                  Pessoa jurídica
                </button>
              </div>
            )}

            <input
              type="search"
              className="connect-bank__search"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Pesquisar banco"
              aria-label="Pesquisar banco"
              autoComplete="off"
            />

            <div className="connect-bank__connector-panel">
              {visibleConnectors.length === 0 ? (
                <p className="connect-bank__empty">
                  {search.trim() ? "Nenhum banco encontrado com esse nome." : "Nenhum banco disponível nessa categoria."}
                </p>
              ) : (
                <ul className="connect-bank__connector-list">
                  {visibleConnectors.map((connector) => {
                    const logoUrl = connector.logoUrl;
                    const rawColor = connector.color || "3a3a3a";

                    return (
                      <li key={connector.id}>
                        <button
                          type="button"
                          className={`connect-bank__connector${selectedConnectorId === connector.id ? " connect-bank__connector--selected" : ""}`}
                          aria-pressed={selectedConnectorId === connector.id}
                          onClick={() => { setSelectedConnectorId(connector.id); setError(""); }}
                          disabled={connectingId !== null}
                        >
                          <BankLogo name={connector.name} logoUrl={logoUrl} color={rawColor} size={40} />
                          <span className="connect-bank__connector-name">{connector.name}</span>
                          {connectingId === connector.id && (
                            <span className="connect-bank__connector-status" role="status">
                              <span className="connect-bank__spinner" aria-hidden="true" />
                              {connectingStage === "waiting"
                                ? "Aguardando confirmação do banco..."
                                : "Conectando..."}
                            </span>
                          )}
                        </button>
                      </li>
                    );
                  })}
                </ul>
              )}
            </div>
            <div className="connect-bank__connect-action">
              <p className="connect-bank__selected" role="status">
                {selectedConnector ? `Banco selecionado: ${selectedConnector.name}` : "Selecione um banco para continuar."}
              </p>
              <Button
                as="button"
                type="button"
                variant="primary"
                size="md"
                className="connect-bank__button"
                disabled={!selectedConnector || connectingId !== null}
                onClick={() => selectedConnector && handleSelectConnector(selectedConnector.id)}
              >
                {connectingId !== null ? "Conectando..." : "Conectar"}
              </Button>
            </div>
          </section>
        )}

      </main>

      <ul className="connect-bank__security">
        <li>
          <ShieldIcon />
          O login acontece no ambiente do seu banco, pelo Open Finance (sistema regulado pelo Banco
          Central). O Control Finance não é instituição financeira.
        </li>
        <li>
          <EyeSlashIcon />
          A senha do banco não passa pelo Control Finance
        </li>
        <li>
          <SyncIcon />
          Desconectar apaga do Control Finance os dados que vieram do banco
        </li>
      </ul>
    </div>
  );
}
