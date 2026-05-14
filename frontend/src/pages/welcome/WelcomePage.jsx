import { useNavigate } from "react-router-dom";
import logoappfinance from "../../assets/images/logoappfinance.png";
import "./welcome.css";

export default function WelcomePage() {
  const navigate = useNavigate();

  // Pega o nome do usuário salvo no localStorage após o login
  const user = JSON.parse(localStorage.getItem("user") || "{}");
  const firstName = user?.name?.split(" ")[0] || "você";

  const features = [
    {
      title: "Sincronização automática",
      desc: "Transações atualizadas em tempo real",
      icon: (
        <svg viewBox="0 0 24 24" fill="none" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M21 12a9 9 0 0 0-9-9 9.75 9.75 0 0 0-6.74 2.74L3 8" />
          <path d="M3 3v5h5" />
          <path d="M3 12a9 9 0 0 0 9 9 9.75 9.75 0 0 0 6.74-2.74L21 16" />
          <path d="M16 16h5v5" />
        </svg>
      ),
    },
    {
      title: "Dashboard completo",
      desc: "Saldo, receitas, despesas e cartões",
      icon: (
        <svg viewBox="0 0 24 24" fill="none" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <rect x="2" y="3" width="20" height="14" rx="2" />
          <path d="M8 21h8M12 17v4" />
        </svg>
      ),
    },
    {
      title: "IA financeira",
      desc: "Categorização e insights automáticos",
      icon: (
        <svg viewBox="0 0 24 24" fill="none" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M12 2a5 5 0 0 1 5 5v3a5 5 0 0 1-10 0V7a5 5 0 0 1 5-5Z" />
          <path d="M15 14a6 6 0 0 1-6 0" />
          <path d="M9.5 14.5 8 20l4-2 4 2-1.5-5.5" />
        </svg>
      ),
    },
  ];

  return (
    <div className="welcome-page">
      <div className="welcome-card">
        <img src={logoappfinance} alt="Control Finance" className="welcome-logo" />

        <div className="welcome-avatar">
          <svg viewBox="0 0 24 24" fill="none" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <circle cx="12" cy="8" r="4" />
            <path d="M4 20c0-4 3.6-7 8-7s8 3 8 7" />
          </svg>
        </div>

        <h1 className="welcome-title">Olá, {firstName}!</h1>
        <p className="welcome-subtitle">Bem-vindo ao Control Finance</p>

        <div className="welcome-features">
          {features.map((f) => (
            <div className="welcome-feature" key={f.title}>
              <div className="welcome-feature-icon">{f.icon}</div>
              <div>
                <p className="welcome-feature-title">{f.title}</p>
                <p className="welcome-feature-desc">{f.desc}</p>
              </div>
            </div>
          ))}
        </div>

        <button className="welcome-btn" onClick={() => navigate("/onboarding")}>
          Conectar meu banco
          <svg viewBox="0 0 24 24" fill="none" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M5 12h14M12 5l7 7-7 7" />
          </svg>
        </button>
      </div>
    </div>
  );
}
