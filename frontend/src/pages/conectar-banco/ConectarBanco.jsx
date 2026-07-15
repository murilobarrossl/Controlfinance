import { useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import Button from "../../components/ui/Button/Button.jsx";
import { ShieldIcon, SyncIcon } from "../../components/ui/icons/FeatureIcons.jsx";
import { EyeSlashIcon } from "../../components/ui/icons/EyeIcons.jsx";
import { getConnectors, createIntegration, getIntegrationStatus } from "../../api/polp.js";
import { groupConnectorsByType } from "../../utils/bankSorting.js";
import { setPendingIntegrationId, watchPolpConnection, wait } from "../../services/polpConnection.js";
import logo from "../../assets/images/control-finance-transparente-branco.svg";
import "./ConectarBanco.css";

const POLL_MAX_ATTEMPTS = 10;
const POLL_INTERVAL_MS = 1000;

export default function ConnectBankPage() {
  const navigate = useNavigate();
  const [step, setStep] = useState("intro"); // "intro" | "choosing"
  const [connectors, setConnectors] = useState([]);
  const [personType, setPersonType] = useState("personal"); // "personal" | "business"
  const [loadingConnectors, setLoadingConnectors] = useState(false);
  const [connectingId, setConnectingId] = useState(null);
  const [connectingStage, setConnectingStage] = useState(null); // "creating" | "waiting"
  const [error, setError] = useState("");

  const { personal, business } = useMemo(
    () => groupConnectorsByType(connectors),
    [connectors]
  );

  const visibleConnectors = personType === "personal" ? personal : business;

  async function handleStart() {
    setError("");
    setLoadingConnectors(true);
    try {
      const data = await getConnectors();
      const list = Array.isArray(data) ? data : data?.data ?? [];
      setConnectors(list);
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
      const data = await createIntegration(connectorId);
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

    for (let attempt = 0; attempt < POLL_MAX_ATTEMPTS; attempt++) {
      await wait(POLL_INTERVAL_MS);
      const status = await getIntegrationStatus(integrationId);

      if (status?.urlToAuthenticate) {
        await openAuthAndWait(integrationId, status.urlToAuthenticate, authWindow);
        return;
      }

      if (status?.status === "login_error") {
        authWindow.close();
        setError(status.error || "Não foi possível autenticar com o banco. Tente novamente.");
        return;
      }
    }

    authWindow.close();
    setError("A conexão demorou para responder. Tente novamente em instantes.");
  }

  async function openAuthAndWait(integrationId, authUrl, authWindow) {
    setPendingIntegrationId(integrationId);
    authWindow.location.href = authUrl;
    authWindow.focus();
    setConnectingStage("waiting");

    const result = await watchPolpConnection(integrationId);

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
      <div className="connect-bank__card">
        <img src={logo} alt="Control Finance" className="connect-bank__logo" />

        <h1 className="connect-bank__title">Conecte sua conta</h1>
        <p className="connect-bank__subtitle">
          Para continuar, vincule seu banco via Open Finance.
        </p>

        {connectingStage === "waiting" && (
          <p className="connect-bank__notice">
            Abrimos a autenticação em outra aba. Conclua por lá: depois de confirmar, o banco
            ainda precisa sincronizar suas contas, o que pode levar alguns minutos. Você será
            levado automaticamente para o dashboard aqui assim que terminar.
          </p>
        )}

        {error && <p className="connect-bank__error">{error}</p>}

        {step === "intro" && (
          <Button
            as="button"
            type="button"
            variant="primary"
            size="md"
            className="connect-bank__button"
            onClick={handleStart}
            disabled={loadingConnectors}
          >
            {loadingConnectors ? "Carregando bancos..." : "Conectar banco"}
          </Button>
        )}

        {step === "choosing" && (
          <>
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

            {visibleConnectors.length === 0 ? (
              <p className="connect-bank__empty">Nenhum banco disponível nessa categoria.</p>
            ) : (
              <ul className="connect-bank__connector-list">
                {visibleConnectors.map((connector) => {
                  const logoUrl = connector.logoUrl;
                  const rawColor = connector.color || "3a3a3a";
                  const stripeColor = rawColor.startsWith("#") ? rawColor : `#${rawColor}`;

                  return (
                    <li key={connector.id}>
                      <button
                        type="button"
                        className="connect-bank__connector"
                        onClick={() => handleSelectConnector(connector.id)}
                        disabled={connectingId !== null}
                      >
                        <span
                          className="connect-bank__connector-stripe"
                          style={{ backgroundColor: stripeColor }}
                        />
                        {logoUrl && (
                          <img src={logoUrl} alt="" className="connect-bank__connector-icon" />
                        )}
                        <span className="connect-bank__connector-name">{connector.name}</span>
                        {connectingId === connector.id && (
                          <span className="connect-bank__connector-status">
                            <span className="connect-bank__spinner" />
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
          </>
        )}

      </div>

      <ul className="connect-bank__security">
        <li>
          <ShieldIcon />
          Conexão via Open Finance, regulamentada pelo Banco Central
        </li>
        <li>
          <EyeSlashIcon />
          Nunca vemos nem guardamos a senha do seu banco
        </li>
        <li>
          <SyncIcon />
          Você pode desconectar sua conta quando quiser
        </li>
      </ul>
    </div>
  );
}
