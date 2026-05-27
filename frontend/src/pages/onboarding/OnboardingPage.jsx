import { useState, useEffect } from "react";
import { useNavigate } from "react-router-dom";
import {
  getConnectors,
  createIntegration,
  getIntegrationStatus,
} from "../../api/polp.js";
import logoappfinance from "../../assets/images/logoappfinance.png";
import "./onboarding.css";

// ──────────────────────────────────────────
//  Bancos padrão (fallback se a API falhar)
// ──────────────────────────────────────────
const DEFAULT_BANKS = [
  {
    id: "001",
    name: "Banco do Brasil",
    color: "#F9C200",
    textColor: "#003882",
    initials: "BB",
  },
  {
    id: "260",
    name: "Nubank",
    color: "#820AD1",
    textColor: "#fff",
    initials: "Nu",
  },
  {
    id: "341",
    name: "Itaú",
    color: "#EC7000",
    textColor: "#fff",
    initials: "It",
  },
  {
    id: "033",
    name: "Santander",
    color: "#CC0000",
    textColor: "#fff",
    initials: "San",
  },
  {
    id: "336",
    name: "C6 Bank",
    color: "#000",
    textColor: "#fff",
    initials: "C6",
  },
  {
    id: "237",
    name: "Bradesco",
    color: "#CC092F",
    textColor: "#fff",
    initials: "Bra",
  },
  {
    id: "077",
    name: "Inter",
    color: "#FF6B00",
    textColor: "#fff",
    initials: "Int",
  },
  {
    id: "104",
    name: "Caixa Econômica",
    color: "#005CA9",
    textColor: "#fff",
    initials: "CEF",
  },
  {
    id: "422",
    name: "Safra",
    color: "#1B3A6B",
    textColor: "#fff",
    initials: "Saf",
  },
  {
    id: "290",
    name: "PagBank",
    color: "#00C851",
    textColor: "#fff",
    initials: "Pag",
  },
];

// ──────────────────────────────────────────
//  TELA DE SINCRONIZAÇÃO
// ──────────────────────────────────────────
function SyncScreen({ bank, integrationId, onSuccess }) {
  const [steps, setSteps] = useState([
    { label: "Conexão estabelecida", status: "done" },
    { label: "Contas encontradas", status: "active" },
    { label: "Importando transações...", status: "pending" },
    { label: "Categorizando com IA", status: "pending" },
  ]);

  useEffect(() => {
    // Simula progresso visual enquanto aguarda a API
    const timers = [
      setTimeout(() => advanceStep(1), 1500),
      setTimeout(() => advanceStep(2), 3000),
      setTimeout(() => advanceStep(3), 4800),
      setTimeout(onSuccess, 6500),
    ];
    return () => timers.forEach(clearTimeout);
  }, []);

  // Também faz polling real do status da integração
  useEffect(() => {
    if (!integrationId) return;
    const interval = setInterval(async () => {
      try {
        const data = await getIntegrationStatus(integrationId);
        if (data?.status === "active") {
          clearInterval(interval);
          onSuccess();
        }
      } catch (_) {}
    }, 3000);
    return () => clearInterval(interval);
  }, [integrationId]);

  const advanceStep = (index) => {
    setSteps((prev) =>
      prev.map((s, i) => {
        if (i < index) return { ...s, status: "done" };
        if (i === index) return { ...s, status: "active" };
        return { ...s, status: "pending" };
      }),
    );
  };

  return (
    <div className="sync-page">
      <div className="sync-card">
        <img src={logoappfinance} alt="Control Finance" className="sync-logo" />
        <div className="sync-spinner" />
        <h2 className="sync-title">Sincronizando sua conta</h2>
        <p className="sync-subtitle">
          Conectando ao {bank?.name || "banco"}. Aguarde alguns segundos...
        </p>

        <div className="sync-steps">
          {steps.map((step) => (
            <div key={step.label} className={`sync-step ${step.status}`}>
              <span className="sync-step-dot" />
              <span className="sync-step-label">{step.label}</span>
              {step.status === "done" && (
                <span className="sync-step-status">✓ Concluído</span>
              )}
              {step.status === "active" && (
                <span className="sync-step-status">Em andamento</span>
              )}
            </div>
          ))}
        </div>

        <div className="sync-ai-box">
          <svg
            viewBox="0 0 24 24"
            fill="none"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
          >
            <path d="M12 2a5 5 0 0 1 5 5v3a5 5 0 0 1-10 0V7a5 5 0 0 1 5-5Z" />
            <path d="M15 14a6 6 0 0 1-6 0" />
            <path d="M9.5 14.5 8 20l4-2 4 2-1.5-5.5" />
          </svg>
          A IA da Polp está categorizando suas transações automaticamente
        </div>
      </div>
    </div>
  );
}

// ──────────────────────────────────────────
//  TELA PRINCIPAL — LISTA DE BANCOS
// ──────────────────────────────────────────
export default function OnboardingPage() {
  const navigate = useNavigate();
  const [banks, setBanks] = useState(DEFAULT_BANKS);
  const [search, setSearch] = useState("");
  const [selectedBank, setSelectedBank] = useState(null);
  const [integrationId, setIntegrationId] = useState(null);
  const [syncing, setSyncing] = useState(false);
  const [error, setError] = useState("");

  // Tenta carregar lista real de bancos da Polp
  useEffect(() => {
    getConnectors()
      .then((data) => {
        if (data?.length) setBanks(data);
      })
      .catch(() => {}); // Mantém o fallback silenciosamente
  }, []);

  const filtered = banks.filter((b) =>
    b.name.toLowerCase().includes(search.toLowerCase()),
  );

  const handleSelectBank = async (bank) => {
    setError("");
    setSelectedBank(bank);
    setSyncing(true);

    try {
      const data = await createIntegration(bank.id);

      // Se a Polp retornar uma URL de autenticação, redireciona o usuário
      if (data?.authUrl) {
        window.location.href = data.authUrl;
        return;
      }

      // Se retornar integrationId direto (sandbox), passa para a tela de sync
      if (data?.integrationId) {
        setIntegrationId(data.integrationId);
      }
    } catch (err) {
      setError(err.message || "Erro ao conectar. Tente novamente.");
      setSyncing(false);
      setSelectedBank(null);
    }
  };

  const handleSyncSuccess = () => {
    navigate("/dashboard");
  };

  // Mostra tela de sincronização
  if (syncing && selectedBank) {
    return (
      <SyncScreen
        bank={selectedBank}
        integrationId={integrationId}
        onSuccess={handleSyncSuccess}
      />
    );
  }

  // Tela de seleção de banco
  return (
    <div className="onboarding-page">
      <div className="onboarding-card">
        <img
          src={logoappfinance}
          alt="Control Finance"
          className="onboarding-logo"
        />

        <div className="onboarding-header">
          <h2 className="onboarding-title">Escolha seu banco</h2>
          <p className="onboarding-subtitle">
            <svg
              viewBox="0 0 24 24"
              fill="none"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
            >
              <rect x="3" y="11" width="18" height="11" rx="2" ry="2" />
              <path d="M7 11V7a5 5 0 0 1 10 0v4" />
            </svg>
            Conexão segura via Open Finance — Polp
          </p>
        </div>

        <div className="onboarding-search">
          <svg
            viewBox="0 0 24 24"
            fill="none"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
          >
            <circle cx="11" cy="11" r="8" />
            <path d="m21 21-4.35-4.35" />
          </svg>
          <input
            placeholder="Buscar banco..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>

        {error && (
          <p
            style={{
              color: "#e84520",
              fontSize: "12px",
              textAlign: "center",
              margin: "0 0 12px",
            }}
          >
            {error}
          </p>
        )}

        <div className="onboarding-banks">
          {filtered.length === 0 ? (
            <p className="onboarding-empty">Nenhum banco encontrado.</p>
          ) : (
            filtered.map((bank) => (
              <button
                key={bank.id}
                className="bank-item"
                onClick={() => handleSelectBank(bank)}
                style={{
                  background: "none",
                  width: "100%",
                  textAlign: "left",
                  fontFamily: "inherit",
                }}
              >
                <div
                  className="bank-item-icon"
                  style={{
                    background: bank.color,
                    color: bank.textColor || "#fff",
                  }}
                >
                  {bank.initials || bank.name?.slice(0, 2)}
                </div>
                <span className="bank-item-name">{bank.name}</span>
                <svg
                  className="bank-item-arrow"
                  viewBox="0 0 24 24"
                  fill="none"
                  strokeWidth="2"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                >
                  <path d="M9 18l6-6-6-6" />
                </svg>
              </button>
            ))
          )}
        </div>

        <div className="onboarding-footer">
          <svg
            viewBox="0 0 24 24"
            fill="none"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
          >
            <rect x="3" y="11" width="18" height="11" rx="2" ry="2" />
            <path d="M7 11V7a5 5 0 0 1 10 0v4" />
          </svg>
          300+ bancos e fintechs suportados
        </div>
      </div>
    </div>
  );
}
