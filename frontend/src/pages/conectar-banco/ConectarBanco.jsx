import { useMemo, useState } from "react";
import { getConnectors, createIntegration, getIntegrationStatus } from "../../api/polp.js";
import { groupConnectorsByType } from "../../utils/bankSorting.js";
import "./ConectarBanco.css";

const POLL_MAX_ATTEMPTS = 10;
const POLL_INTERVAL_MS = 1000;

function wait(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

export default function ConnectBankPage() {
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
    try {
      const data = await createIntegration(connectorId);
      if (data?.urlToAuthenticate) {
        window.location.href = data.urlToAuthenticate;
        return;
      }
      await pollIntegrationStatus(data.integrationId);
    } catch (err) {
      setError(err.message || "Não foi possível conectar ao banco. Tente novamente.");
    } finally {
      setConnectingId(null);
      setConnectingStage(null);
    }
  }

  async function pollIntegrationStatus(integrationId) {
    setConnectingStage("waiting");

    for (let attempt = 0; attempt < POLL_MAX_ATTEMPTS; attempt++) {
      await wait(POLL_INTERVAL_MS);
      const status = await getIntegrationStatus(integrationId);

      if (status?.urlToAuthenticate) {
        window.location.href = status.urlToAuthenticate;
        return;
      }

      if (status?.status === "login_error") {
        setError(status.error || "Não foi possível autenticar com o banco. Tente novamente.");
        return;
      }
    }

    setError("A conexão demorou para responder. Tente novamente em instantes.");
  }

  return (
    <div className="connect-bank">
      <div className="connect-bank__card">
        <h1 className="connect-bank__title">Conecte sua conta</h1>
        <p className="connect-bank__subtitle">
          Para continuar, vincule seu banco via Open Finance.
        </p>

        {error && <p className="connect-bank__error">{error}</p>}

        {step === "intro" && (
          <button
            type="button"
            className="connect-bank__button"
            onClick={handleStart}
            disabled={loadingConnectors}
          >
            <span className="connect-bank__button-dot" />
            {loadingConnectors ? "Carregando bancos..." : "Conectar banco"}
          </button>
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

        <p className="connect-bank__footer">Seguro via Open Finance · Banco Central</p>
      </div>
    </div>
  );
}
