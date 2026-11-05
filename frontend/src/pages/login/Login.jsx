import { useEffect, useRef, useState } from "react";
import { Link, useNavigate } from "react-router";
import Button from "../../components/ui/Button/Button.jsx";
import { EyeIcon, EyeSlashIcon } from "../../components/ui/icons/EyeIcons.jsx";
import { ShieldIcon, SyncIcon } from "../../components/ui/icons/FeatureIcons.jsx";
import { login } from "../../api/auth.js";
import { getIntegrations } from "../../api/polp.js";
import { useAuth } from "../../context/AuthContext.jsx";
import { applyMask, CNPJ_MASK, getCpfCnpjMask } from "../../utils/masks.js";
import logo from "../../assets/images/cflogobranco.svg";
import "./Login.css";
import usePageTitle from "../../hooks/usePageTitle.js";

const DESTAQUES = [
  { Icon: ShieldIcon, texto: "Valores e extratos guardados de forma cifrada" },
  { Icon: SyncIcon, texto: "Sincronização via Open Finance" },
];

export default function LoginPage({ mode = "email" }) {
  usePageTitle("Entrar");
  const navigate = useNavigate();
  const { login: startSession } = useAuth();
  const [identifier, setIdentifier] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [errorAttempt, setErrorAttempt] = useState(0);
  const identifierRef = useRef(null);

  const isCpf = mode === "cpf";

  useEffect(() => {
    if (error) identifierRef.current?.focus();
  }, [error, errorAttempt]);

  const identifierConfig = isCpf
    ? {
        label: "CPF/CNPJ",
        type: "text",
        placeholder: "000.000.000-00 ou 00.000.000/0000-00",
      }
    : {
        label: "E-mail",
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
    if (submitting) return;
    setError("");
    setSubmitting(true);

    try {
      const cleanIdentifier = isCpf ? identifier.replace(/\D/g, "") : identifier;
      const data = await login(cleanIdentifier, password);
      startSession(data);

      // Só manda pro fluxo de conexão quem ainda não tem banco conectado: mandar todo mundo
      // pra lá incondicionalmente forçava reconexão a cada login, e cada reconexão podia gerar
      // uma nova conta local (a Polp às vezes emite um novo id de conta por integração), o que
      // duplicava transações nas somas de receitas/despesas.
      let hasAccount = false;
      try {
        const accounts = await getIntegrations();
        hasAccount = Array.isArray(accounts) && accounts.length > 0;
      } catch {
        // se a checagem falhar, cai no comportamento antigo (manda conectar), mais seguro
        // do que assumir que já tem conta e esconder o fluxo de conexão de quem precisa dele
      }

      navigate(hasAccount ? "/dashboard" : "/conectar-banco");
    } catch (err) {
      const invalidCredentials = isCpf ? "CPF/CNPJ ou senha incorretos." : "E-mail ou senha incorretos.";
      setError(err.status === 401 ? invalidCredentials : err.message || "Não foi possível entrar. Tente novamente.");
      setErrorAttempt((n) => n + 1);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="login">
      <aside className="login__intro">
        <div className="login__top">
          <Link to="/" className="login__back">
            <span aria-hidden="true">‹</span>
            <img src={logo} alt="" className="login__logo" />
            Voltar
          </Link>
        </div>

        <div className="login__intro-body">
          <h1 className="login__title">
            Suas finanças organizadas no seu bolso
          </h1>

          <p className="login__subtitle">
            Organize suas finanças, simule financiamentos e planeje seu futuro,
            tudo em um só lugar.
          </p>
        </div>
      </aside>

      <main className="login__panel">
        <form className="login__card" onSubmit={handleSubmit} aria-busy={submitting}>
          <div className="login__card-head">
            <h2 className="login__card-title">Entrar na conta</h2>

            <p className="login__card-subtitle">
              Não tem conta?{" "}
              <Link to="/register" className="login__link">
                Cadastre-se agora
              </Link>
            </p>
            <p className="login__required-note">Todos os campos são obrigatórios.</p>
          </div>

          <div className="login__fields">
            <label className="login__field">
              <span className="login__field-label">
                {identifierConfig.label}
              </span>

              <input
                ref={identifierRef}
                id="login-identifier"
                name="username"
                type={identifierConfig.type}
                value={identifier}
                onChange={handleIdentifierChange}
                className="login__input"
                placeholder={identifierConfig.placeholder}
                autoComplete={isCpf ? "username" : "email"}
                inputMode={isCpf ? "numeric" : "email"}
                maxLength={isCpf ? CNPJ_MASK.length : undefined}
                aria-invalid={error ? true : undefined}
                aria-describedby={error ? "login-error" : undefined}
                required
              />
            </label>

            <label className="login__field">
              <span className="login__field-label">Senha</span>

              <div className="login__password-wrapper">
                <input
                  id="login-password"
                  name="password"
                  type={showPassword ? "text" : "password"}
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  className="login__input login__input--password"
                    autoComplete="current-password"
                  aria-invalid={error ? true : undefined}
                  aria-describedby={error ? "login-error" : undefined}
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
          </div>

          <div className="login__actions">
            {error && (
              <p key={errorAttempt} id="login-error" className="login__error" role="alert">
                {error}
              </p>
            )}

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
          </div>

          <ul className="login__highlights">
            {DESTAQUES.map(({ Icon, texto }) => (
              <li key={texto} className="login__highlight">
                <span className="login__highlight-icon" aria-hidden="true">
                  <Icon />
                </span>
                {texto}
              </li>
            ))}
          </ul>
        </form>
      </main>
    </div>
  );
}
