import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import Button from "../../components/ui/Button/Button.jsx";
import { EyeIcon, EyeSlashIcon } from "../../components/ui/icons/EyeIcons.jsx";
import { ShieldIcon, SyncIcon, SparkleIcon } from "../../components/ui/icons/FeatureIcons.jsx";
import { login } from "../../api/auth.js";
import { getIntegrations } from "../../api/polp.js";
import { useAuth } from "../../context/AuthContext.jsx";
import { applyMask, CNPJ_MASK, getCpfCnpjMask } from "../../utils/masks.js";
import logo from "../../assets/images/control-finance-transparente-branco.svg";
import "./Login.css";

const DESTAQUES = [
  { Icon: ShieldIcon, texto: "Dados protegidos com criptografia" },
  { Icon: SyncIcon, texto: "Sincronização via Open Finance" },
  { Icon: SparkleIcon, texto: "Agente de IA incluso" },
];

export default function LoginPage({ mode = "email" }) {
  const navigate = useNavigate();
  const { login: setAuthToken } = useAuth();
  const [identifier, setIdentifier] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState("");
  const [submitting, setSubmitting] = useState(false);

  const isCpf = mode === "cpf";

  const identifierConfig = isCpf
    ? {
        label: "CPF/CNPJ",
        type: "text",
        placeholder: "000.000.000-00 ou 00.000.000/0000-00",
      }
    : {
        label: "E-MAIL",
        type: "email",
        placeholder: "exemplo@email.com",
      };

  function handleIdentifierChange(e) {
    const rawValue = e.target.value;

    if (!isCpf) {
      setIdentifier(rawValue);
      return;
    }

    const digits = rawValue.replace(/\D/g, "").slice(0, 14);
    const mask = getCpfCnpjMask(digits);
    const masked = applyMask(digits, mask, identifier.replace(/\D/g, ""));
    setIdentifier(masked);
  }

  async function handleSubmit(e) {
    e.preventDefault();
    setError("");
    setSubmitting(true);

    try {
      const cleanIdentifier = isCpf ? identifier.replace(/\D/g, "") : identifier;
      const data = await login(cleanIdentifier, password);

      if (data?.token) {
        setAuthToken(data.token);
      }

      // Só manda pro fluxo de conexão quem ainda não tem banco conectado — mandar todo mundo
      // pra lá incondicionalmente forçava reconexão a cada login, e cada reconexão podia gerar
      // uma nova conta local (a Polp às vezes emite um novo id de conta por integração), o que
      // duplicava transações nas somas de receitas/despesas.
      let hasAccount = false;
      try {
        const accounts = await getIntegrations();
        hasAccount = Array.isArray(accounts) && accounts.length > 0;
      } catch {
        // se a checagem falhar, cai no comportamento antigo (manda conectar) — mais seguro
        // do que assumir que já tem conta e esconder o fluxo de conexão de quem precisa dele
      }

      navigate(hasAccount ? "/dashboard" : "/conectar-banco");
    } catch (err) {
      setError(err.message || "Não foi possível entrar. Tente novamente.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="login">
      <aside className="login__intro">
        <Link to="/" className="login__logo">
          <img src={logo} alt="Control Finance" />
        </Link>

        <span className="login__badge">Entrar na sua conta</span>

        <h1 className="login__title">
          Inteligência financeira
          <br />
          no seu bolso
        </h1>

        <p className="login__subtitle">
          Organize suas finanças, simule investimentos e planeje seu futuro —
          tudo em um só lugar.
        </p>

        <p className="login__welcome-back">
          Entrar em sua conta — Que bom te ver novamente!
        </p>

        <ul className="login__highlights">
          {DESTAQUES.map(({ Icon, texto }) => (
            <li key={texto} className="login__highlight">
              <span className="login__highlight-icon">
                <Icon />
              </span>
              {texto}
            </li>
          ))}
        </ul>
      </aside>

      <div className="login__panel">
        <form className="login__card" onSubmit={handleSubmit}>
          <h2 className="login__card-title">Entrar na conta</h2>

          <p className="login__card-subtitle">
            Não tem conta?{" "}
            <Link to="/register" className="login__link">
              Cadastre-se agora
            </Link>
          </p>

          <label className="login__field">
            <span className="login__field-label">
              {identifierConfig.label}
            </span>

            <input
              type={identifierConfig.type}
              value={identifier}
              onChange={handleIdentifierChange}
              className="login__input"
              placeholder={identifierConfig.placeholder}
              autoComplete={isCpf ? "off" : "email"}
              inputMode={isCpf ? "numeric" : "email"}
              maxLength={isCpf ? CNPJ_MASK.length : undefined}
              required
            />
          </label>

          <label className="login__field">
            <span className="login__field-label">SENHA</span>

            <div className="login__password-wrapper">
              <input
                type={showPassword ? "text" : "password"}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                className="login__input login__input--password"
                placeholder="Digite sua senha"
                autoComplete="current-password"
                required
              />
              <button
                type="button"
                className="login__password-toggle"
                onClick={() => setShowPassword((prev) => !prev)}
                aria-label={showPassword ? "Ocultar senha" : "Mostrar senha"}
                aria-pressed={showPassword}
              >
                {showPassword ? <EyeSlashIcon /> : <EyeIcon />}
              </button>
            </div>
          </label>

          {error && <p className="login__error">{error}</p>}

          <Button
            as="button"
            type="submit"
            variant="primary"
            size="md"
            className="login__submit"
            disabled={submitting}
          >
            {submitting ? "Entrando..." : "Entrar"}
          </Button>

          <Link
            to={isCpf ? "/loginemail" : "/logincpf-cnpj"}
            className="login__mode-toggle"
          >
            {isCpf ? "Entrar com e-mail" : "Entrar com CPF/CNPJ"}
          </Link>
        </form>
      </div>
    </div>
  );
}
