import { Link, useNavigate } from "react-router-dom";
import { useState } from "react";
import graficologo from "../../assets/images/graficologo.png";
import logoappfinance from "../../assets/images/logoappfinance.png";
import "./loginstyle.css";

const API = import.meta.env.VITE_API_URL ?? "http://localhost:5000/api";

const applyMask = (value, mask, prev) => {
  const isDeleting = prev && value.length < prev.length;
  const digits = value.replace(/\D/g, "");
  if (isDeleting && digits.length === 0) return "";
  let i = 0;
  let result = "";
  for (let j = 0; j < mask.length; j++) {
    if (i >= digits.length) break;
    if (mask[j] === "#") {
      result += digits[i++];
    } else {
      result += mask[j];
    }
  }
  return result;
};

const CPF_MASK = "###.###.###-##";
const CNPJ_MASK = "##.###.###/####-##";
const getCpfCnpjMask = (digits) => (digits.length <= 11 ? CPF_MASK : CNPJ_MASK);

export default function LoginPage({ mode = "email" }) {
  const navigate = useNavigate();
  const isCpf = mode === "cpf";

  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [identifier, setIdentifier] = useState("");
  const [senha, setSenha] = useState("");

  const handleIdentifierChange = (e) => {
    const raw = e.target.value;
    if (!isCpf) {
      setIdentifier(raw);
      return;
    }
    const digits = raw.replace(/\D/g, "").slice(0, 14);
    const mask = getCpfCnpjMask(digits);
    setIdentifier(applyMask(raw, mask, identifier));
  };

  const validate = () => {
    if (!identifier.trim())
      return isCpf ? "Informe o CPF ou CNPJ." : "Informe o e-mail.";
    if (!isCpf && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(identifier))
      return "E-mail inválido.";
    if (!senha) return "Informe a senha.";
    return null;
  };

  const handleSubmit = async () => {
    const validationError = validate();
    if (validationError) {
      setError(validationError);
      return;
    }

    setError("");
    setLoading(true);

    try {
      const cleanIdentifier = isCpf
        ? identifier.replace(/\D/g, "")
        : identifier;

      const response = await fetch(`${API}/auth/login`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          identifier: cleanIdentifier,
          password: senha,
        }),
      });

      const data = await response.json();
      if (!response.ok)
        throw new Error(data.message ?? data.title ?? "Credenciais inválidas.");

      localStorage.setItem("token", data.token);
      localStorage.setItem("user", JSON.stringify(data.user));
      navigate("/dashboard");
    } catch (err) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="login-page">
      <div className="login-left">
        <span className="login-brand-label">Control Finance</span>
        <h1 className="login-headline">
          Control finance: clareza financeira para decisões mais seguras.
        </h1>
        <p className="login-description">
          Transforme a forma como você controla seu dinheiro. Gerencie receitas,
          despesas e investimentos em um único lugar, com visão clara do seu
          saldo e decisões mais inteligentes para o seu futuro financeiro.
        </p>
        <img
          src={graficologo}
          alt="Gráfico financeiro"
          className="login-chart"
        />
      </div>

      <div className="login-right">
        <div className="login-card">
          <img
            src={logoappfinance}
            alt="Control Finance"
            className="login-logo"
          />

          <div className="login-fields">
            {error && <div className="login-error">{error}</div>}

            <div className="login-field">
              <label>{isCpf ? "CPF / CNPJ" : "E-mail"}</label>
              <input
                placeholder={isCpf ? "000.000.000-00" : "nome@empresa.com.br"}
                value={identifier}
                onChange={handleIdentifierChange}
                inputMode={isCpf ? "numeric" : "email"}
                autoComplete={isCpf ? "off" : "email"}
                onKeyDown={(e) => e.key === "Enter" && handleSubmit()}
              />
            </div>

            <div className="login-field">
              <label>Senha</label>
              <div className="login-password-wrap">
                <svg
                  xmlns="http://www.w3.org/2000/svg"
                  width="14"
                  height="14"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  className="login-lock-icon"
                >
                  <rect width="18" height="11" x="3" y="11" rx="2" ry="2" />
                  <path d="M7 11V7a5 5 0 0 1 10 0v4" />
                </svg>
                <input
                  type={showPassword ? "text" : "password"}
                  placeholder="*********"
                  value={senha}
                  onChange={(e) => setSenha(e.target.value)}
                  className="login-password-input"
                  autoComplete="current-password"
                  onKeyDown={(e) => e.key === "Enter" && handleSubmit()}
                />
                <button
                  className="login-eye-btn"
                  onClick={() => setShowPassword((v) => !v)}
                  type="button"
                  aria-label={showPassword ? "Ocultar senha" : "Mostrar senha"}
                >
                  {showPassword ? (
                    <svg
                      xmlns="http://www.w3.org/2000/svg"
                      width="16"
                      height="16"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="2"
                      strokeLinecap="round"
                      strokeLinejoin="round"
                    >
                      <path d="M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49" />
                      <path d="M14.084 14.158a3 3 0 0 1-4.242-4.242" />
                      <path d="M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143" />
                      <path d="m2 2 20 20" />
                    </svg>
                  ) : (
                    <svg
                      xmlns="http://www.w3.org/2000/svg"
                      width="16"
                      height="16"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="2"
                      strokeLinecap="round"
                      strokeLinejoin="round"
                    >
                      <path d="M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0" />
                      <circle cx="12" cy="12" r="3" />
                    </svg>
                  )}
                </button>
              </div>
            </div>

            <button
              className="login-submit"
              onClick={handleSubmit}
              disabled={loading}
              type="button"
            >
              {loading ? "Entrando..." : "Login"}
            </button>

            <p className="entrar-cpfcnpj">
              {isCpf ? (
                <Link to="/loginemail">Entrar com e-mail</Link>
              ) : (
                <Link to="/logincpf-cnpj">Entrar com CPF/CNPJ</Link>
              )}
            </p>

            <p className="login-register">
              Não tem conta? <Link to="/register">Registre-se</Link>
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}
