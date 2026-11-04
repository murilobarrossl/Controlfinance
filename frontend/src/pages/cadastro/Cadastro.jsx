import { useCallback, useState } from "react";
import { Link, useNavigate } from "react-router";
import Button from "../../components/ui/Button/Button.jsx";
import { EyeIcon, EyeSlashIcon } from "../../components/ui/icons/EyeIcons.jsx";
import { register } from "../../api/auth.js";
import { useAuth } from "../../context/AuthContext.jsx";
import { applyMask, CNPJ_MASK, getCpfCnpjMask } from "../../utils/masks.js";
import { TERMS_VERSION, PRIVACY_VERSION } from "../../legal/versions.js";
import logo from "../../assets/images/cflogobranco.svg";
import TermsDialog from "./TermsDialog.jsx";
import "./Cadastro.css";

const STEPS = [
  {
    number: 1,
    title: "Crie sua conta",
    description: "Preencha seus dados básicos e escolha uma senha segura.",
  },
  {
    number: 2,
    title: "Conecte seus bancos",
    description: "Sincronize suas contas bancárias com segurança total.",
  },
];

export default function RegisterPage() {
  const navigate = useNavigate();
  const { login: startSession } = useAuth();
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [document, setDocument] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [showConfirmPassword, setShowConfirmPassword] = useState(false);
  const [termsOpen, setTermsOpen] = useState(false);
  const [confirmsAdult, setConfirmsAdult] = useState(false);
  const [error, setError] = useState("");
  const [submitting, setSubmitting] = useState(false);

  function handleDocumentChange(e) {
    const rawValue = e.target.value;
    const digits = rawValue.replace(/\D/g, "").slice(0, 14);
    const mask = getCpfCnpjMask(digits);
    const masked = applyMask(digits, mask, document.replace(/\D/g, ""));
    setDocument(masked);
  }

  function handleSubmit(e) {
    e.preventDefault();
    setError("");

    if (password !== confirmPassword) {
      setError("As senhas não coincidem.");
      return;
    }

    setTermsOpen(true);
  }

  const closeTerms = useCallback(() => {
    if (!submitting) setTermsOpen(false);
  }, [submitting]);

  async function handleAcceptTerms() {
    setSubmitting(true);
    try {
      const data = await register({
        name,
        email,
        document: document.replace(/\D/g, ""),
        password,
        // Versões dos textos exibidos nesta tela: o backend registra o aceite com elas e recusa
        // se não forem as vigentes.
        acceptedTermsVersion: TERMS_VERSION,
        acceptedPrivacyVersion: PRIVACY_VERSION,
        confirmsAdult,
      });

      startSession(data);
      navigate("/conectar-banco");
    } catch (err) {
      setTermsOpen(false);
      setError(err.message || "Não foi possível criar a conta. Tente novamente.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="register">
      <aside className="register__intro">
        <div className="register__top">
          <Link to="/" className="register__back">
            <span aria-hidden="true">‹</span> Voltar
          </Link>
          <Link to="/" className="register__logo">
            <img src={logo} alt="Control Finance" />
          </Link>
        </div>

        <span className="register__badge">Novo: comece hoje mesmo</span>
        <h1 className="register__title">
          Crie sua conta e
          <br />
          comece a planejar
        </h1>
        <p className="register__subtitle">
          Em alguns cliques você terá acesso ao seu painel
          financeiro.
        </p>
        <ol className="register__steps">
          {STEPS.map((step) => (
            <li className="register__step" key={step.number}>
              <span className="register__step-number">{step.number}</span>
              <div className="register__step-content">
                <h3 className="register__step-title">{step.title}</h3>
                <p className="register__step-description">
                  {step.description}
                </p>
              </div>
            </li>
          ))}
        </ol>
      </aside>

      <div className="register__panel">
        <form className="register__card" onSubmit={handleSubmit}>
          <h2 className="register__card-title">Criar conta grátis</h2>
          <p className="register__card-subtitle">
            Já tem conta?{" "}
            <Link to="/loginemail" className="register__link">
              Entrar agora
            </Link>
          </p>

          <label className="register__field">
            <span className="register__field-label">NOME</span>
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              className="register__input"
              autoComplete="name"
              placeholder="Digite seu nome completo"
              required
            />
          </label>

          <label className="register__field">
            <span className="register__field-label">E-MAIL</span>
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              className="register__input"
              autoComplete="email"
              placeholder="exemplo@email.com"
              required
            />
          </label>

          <label className="register__field">
            <span className="register__field-label">CPF/CNPJ</span>
            <input
              type="text"
              value={document}
              onChange={handleDocumentChange}
              className="register__input"
              inputMode="numeric"
              maxLength={CNPJ_MASK.length}
              autoComplete="off"
              placeholder="000.000.000-00 ou 00.000.000/0000-00"
              required
            />
            <span className="register__field-hint">
              Usado para entrar com CPF/CNPJ e para confirmar que é você ao conectar um banco. O número
              não fica guardado: guardamos só um código de verificação gerado a partir dele.
            </span>
          </label>

          <label className="register__field">
            <span className="register__field-label">SENHA</span>
            <div className="register__password-wrapper">
              <input
                type={showPassword ? "text" : "password"}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                className="register__input register__input--password"
                autoComplete="new-password"
                placeholder="Crie uma senha segura"
                required
              />
              <button
                type="button"
                className="register__password-toggle"
                onClick={() => setShowPassword((prev) => !prev)}
                aria-label={showPassword ? "Ocultar senha" : "Mostrar senha"}
                aria-pressed={showPassword}
              >
                {showPassword ? <EyeSlashIcon /> : <EyeIcon />}
              </button>
            </div>
            <span className="register__field-hint">
              Mínimo de 8 caracteres, incluindo letras e números.
            </span>
          </label>

          <label className="register__field">
            <span className="register__field-label">CONFIRMAR SENHA</span>
            <div className="register__password-wrapper">
              <input
                type={showConfirmPassword ? "text" : "password"}
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                className="register__input register__input--password"
                autoComplete="new-password"
                placeholder="Repita a senha criada"
                required
              />
              <button
                type="button"
                className="register__password-toggle"
                onClick={() => setShowConfirmPassword((prev) => !prev)}
                aria-label={
                  showConfirmPassword ? "Ocultar senha" : "Mostrar senha"
                }
                aria-pressed={showConfirmPassword}
              >
                {showConfirmPassword ? <EyeSlashIcon /> : <EyeIcon />}
              </button>
            </div>
          </label>

          <label className="register__terms">
            <input
              type="checkbox"
              checked={confirmsAdult}
              onChange={(e) => setConfirmsAdult(e.target.checked)}
              className="register__checkbox"
              required
            />
            <span className="register__terms-text">Declaro que tenho 18 anos ou mais.</span>
          </label>

          <p className="register__privacy-note">
            Seus dados são tratados como descrito na{" "}
            <Link to="/politica-de-privacidade" className="register__link" target="_blank" rel="noopener noreferrer">
              Política de privacidade
            </Link>
            : o que guardamos, com quem compartilhamos, por quanto tempo e como excluir. Conectar um banco
            é opcional e pedido à parte, depois do cadastro.
          </p>

          {error && <p className="register__error">{error}</p>}

          <Button
            as="button"
            type="submit"
            variant="primary"
            size="md"
            className="register__submit"
            disabled={submitting}
          >
            {submitting ? "Criando conta..." : "Criar conta grátis"}
          </Button>
        </form>
      </div>

      {termsOpen && (
        <TermsDialog submitting={submitting} onAccept={handleAcceptTerms} onClose={closeTerms} />
      )}
    </div>
  );
}
