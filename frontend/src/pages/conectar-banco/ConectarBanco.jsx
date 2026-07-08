import { useState } from "react";
import { getConnectors, createIntegration } from "../../api/polp.js";
import "./ConectarBanco.css";

export default function ConnectBankPage() {
  const [step, setStep] = useState("intro"); // "intro" | "choosing" | "connecting"
  const [connectors, setConnectors] = useState([]);
  const [loadingConnectors, setLoadingConnectors] = useState(false);
  const [connectingId, setConnectingId] = useState(null);
  const [error, setError] = useState("");

  async function handleStart() {
    setError("");
    setLoadingConnectors(true);
    try {
      const data = await getConnectors();
      setConnectors(data?.connectors ?? data ?? []);
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
    try {
      const data = await createIntegration(connectorId);
      if (data?.url_to_authenticate) {
        window.location.href = data.url_to_authenticate;
      } else {
        setError("Não foi possível iniciar a conexão. Tente novamente.");
      }
    } catch (err) {
      setError(err.message || "Não foi possível conectar ao banco. Tente novamente.");
    } finally {
      setConnectingId(null);
    }
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
          <ul className="connect-bank__connector-list">
            {connectors.map((connector) => (
              <li key={connector.id}>
                <button
                  type="button"
                  className="connect-bank__connector"
                  onClick={() => handleSelectConnector(connector.id)}
                  disabled={connectingId !== null}
                >
                  {connector.imageUrl && (
                    <img
                      src={connector.imageUrl}
                      alt=""
                      className="connect-bank__connector-icon"
                    />
                  )}
                  <span>{connector.name}</span>
                  {connectingId === connector.id && (
                    <span className="connect-bank__connector-status">
                      Conectando...
                    </span>
                  )}
                </button>
              </li>
            ))}
          </ul>
        )}

        <p className="connect-bank__footer">
          Seguro via Open Finance · Banco Central
        </p>
      </div>
    </div>
  );
}
