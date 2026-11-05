import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useNavigate } from "react-router";
import Button from "../../components/ui/Button/Button.jsx";
import { EyeIcon, EyeSlashIcon } from "../../components/ui/icons/EyeIcons.jsx";
import { register } from "../../api/auth.js";
import { useAuth } from "../../context/AuthContext.jsx";
import { applyMask, CNPJ_MASK, getCpfCnpjMask } from "../../utils/masks.js";
import { TERMS_VERSION, PRIVACY_VERSION } from "../../legal/versions.js";
import { documentError, passwordError } from "../../utils/authRules.js";
import logo from "../../assets/images/cflogobranco.svg";
import TermsDialog from "./TermsDialog.jsx";
import "./Cadastro.css";
import usePageTitle from "../../hooks/usePageTitle.js";

export default function RegisterPage() {
  usePageTitle("Criar conta");
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
  const [errorField, setErrorField] = useState(null);
  const [errorAttempt, setErrorAttempt] = useState(0);
  const documentRef = useRef(null);
  const passwordRef = useRef(null);
  const confirmPasswordRef = useRef(null);
  const errorRef = useRef(null);

  useEffect(() => {
    if (!error) return;
    const fieldRefs = { document: documentRef, password: passwordRef, confirmPassword: confirmPasswordRef };
    const target = fieldRefs[errorField]?.current ?? errorRef.current;
    target?.focus();
  }, [error, errorField, errorAttempt]);

  function showError(message, field = null) {
    setErrorField(field);
    setError(message);
    setErrorAttempt((n) => n + 1);
  }

  function fieldAria(field, hintId) {
    const invalid = errorField === field;
    const describedBy = [hintId, invalid ? "register-error" : null].filter(Boolean).join(" ");
    return {
      "aria-invalid": invalid ? true : undefined,
      "aria-describedby": describedBy || undefined,
    };
  }

  function handleDocumentChange(e) {
    const rawValue = e.target.value;
    const digits = rawValue.replace(/\D/g, "").slice(0, 14);
    const mask = getCpfCnpjMask(digits);
    const masked = applyMask(digits, mask, document.replace(/\D/g, ""));
    setDocument(masked);
  }

  function handleSubmit(e) {
    e.preventDefault();
    if (submitting) return;
    setError("");
    setErrorField(null);

    const documentProblem = documentError(document);
    if (documentProblem) {
      showError(documentProblem, "document");
      return;
    }

    const passwordProblem = passwordError(password);
    if (passwordProblem) {
      showError(passwordProblem, "password");
      return;
    }

    if (password !== confirmPassword) {
      showError("As senhas não coincidem.", "confirmPassword");
      return;
    }

    setTermsOpen(true);
  }

  const closeTerms = useCallback(() => {
    if (!submitting) setTermsOpen(false);
  }, [submitting]);

  async function handleAcceptTerms() {
    if (submitting) return;
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
      showError(err.message || "Não foi possível criar a conta. Tente novamente.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="register">
      <aside className="register__intro">
        <div className="register__top">
          <Link to="/" className="register__back">
            <span aria-hidden="true">‹</span>
            <img src={logo} alt="" className="register__logo" />
            Voltar
          </Link>
        </div>

        <div className="register__intro-body">
          <h1 className="register__title">
            Crie sua conta e comece a planejar
          </h1>
          <p className="register__subtitle">
            Em alguns cliques você terá acesso ao seu painel
            financeiro.
          </p>
        </div>
      </aside>

      <main className="register__panel">
        <form className="register__card" onSubmit={handleSubmit} aria-busy={submitting}>
          <div className="register__card-head">
            <h2 className="register__card-title">Criar conta</h2>
            <p className="register__card-subtitle">
              Já tem conta?{" "}
              <Link to="/loginemail" className="register__link">
                Entrar agora
              </Link>
            </p>
            <p className="register__required-note">Todos os campos são obrigatórios.</p>
          </div>

          <div className="register__group">
            <div className="register__field">
              <label htmlFor="register-name" className="register__field-label">Nome</label>
              <input
                type="text"
                id="register-name"
                name="name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                className="register__input"
                autoComplete="name"
                required
              />
            </div>

            <div className="register__field">
              <label htmlFor="register-email" className="register__field-label">E-mail</label>
              <input
                type="email"
                id="register-email"
                name="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                className="register__input"
                autoComplete="email"
                placeholder="exemplo@email.com"
                required
              />
            </div>

            <div className="register__field">
              <label htmlFor="register-document" className="register__field-label">CPF/CNPJ</label>
              <input
                type="text"
                ref={documentRef}
                id="register-document"
                name="document"
                value={document}
                onChange={handleDocumentChange}
                className="register__input"
                inputMode="numeric"
                maxLength={CNPJ_MASK.length}
                autoComplete="off"
                placeholder="000.000.000-00 ou 00.000.000/0000-00"
                {...fieldAria("document", "register-document-hint")}
                required
              />
              <span id="register-document-hint" className="register__field-hint">
                Usado para entrar com CPF/CNPJ e para confirmar que é você ao conectar um banco. O número
                não fica guardado: guardamos só um código de verificação gerado a partir dele.
              </span>
            </div>

          </div>

          <div className="register__group">
            <div className="register__field">
              <label htmlFor="register-password" className="register__field-label">Senha</label>
              <div className="register__password-wrapper">
                <input
                  type={showPassword ? "text" : "password"}
                  ref={passwordRef}
                  id="register-password"
                  name="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  className="register__input register__input--password"
                  autoComplete="new-password"
                  {...fieldAria("password", "register-password-hint")}
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
              <span id="register-password-hint" className="register__field-hint">
                Mínimo de 8 caracteres, incluindo letras e números.
              </span>
            </div>

            <div className="register__field">
              <label htmlFor="register-confirm-password" className="register__field-label">Confirmar senha</label>
              <div className="register__password-wrapper">
                <input
                  type={showConfirmPassword ? "text" : "password"}
                  ref={confirmPasswordRef}
                  id="register-confirm-password"
                  name="confirmPassword"
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)}
                  className="register__input register__input--password"
                  autoComplete="new-password"
                  {...fieldAria("confirmPassword")}
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
            </div>

          </div>

          <div className="register__group register__group--consent">
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

          </div>

          <div className="register__actions">
            {error && (
              <p key={errorAttempt} id="register-error" ref={errorRef} className="register__error" role="alert" tabIndex={-1}>
                {error}
              </p>
            )}

            <Button
              as="button"
              type="submit"
              variant="primary"
              size="md"
              className="register__submit"
              disabled={submitting}
            >
              {submitting ? "Criando conta..." : "Criar conta"}
            </Button>
          </div>
        </form>
      </main>

      {termsOpen && (
        <TermsDialog submitting={submitting} onAccept={handleAcceptTerms} onClose={closeTerms} />
      )}
    </div>
  );
}
