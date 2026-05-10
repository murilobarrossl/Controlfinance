import { useState } from "react";
import graficologo from "../../assets/images/graficologo.png";
import logoappfinance from "../../assets/images/logoappfinance.png";
import "./register.css";
import { Link, useNavigate } from "react-router-dom";

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

export default function RegisterPage() {
  const navigate = useNavigate();
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const [form, setForm] = useState({
    nome: "",
    email: "",
    celular: "",
    documento: "",
    senha: "",
  });

  const handleChange = (field) => (e) =>
    setForm((prev) => ({ ...prev, [field]: e.target.value }));

  const handleSubmit = async () => {
    setError("");
    setLoading(true);

    try {
      const response = await fetch(
        `${import.meta.env.VITE_API_URL}/auth/register`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            name: form.nome,
            email: form.email,
            document: form.documento,
            phone: form.celular,
            password: form.senha,
          }),
        },
      );

      const data = await response.json();

      if (!response.ok) {
        setError(data.message || "Erro ao criar conta.");
        return;
      }

      navigate("/dashboard");
    } catch {
      setError("Erro de conexão. Tente novamente.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="register-page">
      <div className="register-left">
        <span className="register-brand-label">Control Finance</span>
        <h1 className="register-headline">
          Control finance: clareza financeira para decisões mais seguras.
        </h1>
        <p className="register-description">
          Transforme a forma como você controla seu dinheiro. Gerencie receitas,
          despesas e investimentos em um único lugar, com visão clara do seu
          saldo e decisões mais inteligentes para o seu futuro financeiro.
        </p>
        <img
          src={graficologo}
          alt="Gráfico financeiro"
          className="register-chart"
        />
      </div>

      <div className="register-right">
        <div className="register-card">
          <img
            src={logoappfinance}
            alt="Control Finance"
            className="register-logo"
          />

          <div className="register-fields">
            {error && <div className="register-error">{error}</div>}

            <div className="register-field">
              <label>Nome</label>
              <input
                placeholder="como gostaria de ser chamado"
                value={form.nome}
                onChange={handleChange("nome")}
              />
            </div>

            <div className="register-field">
              <label>E-mail</label>
              <input
                type="email"
                placeholder="nome@minhaempresa.com.br"
                value={form.email}
                onChange={handleChange("email")}
              />
            </div>

            <div className="register-field">
              <label>CNPJ/CPF</label>
              <input
                placeholder="00.000.000/0000-00"
                value={form.documento}
                onChange={(e) => {
                  const digits = e.target.value.replace(/\D/g, "").slice(0, 14);
                  const mask =
                    digits.length <= 11
                      ? "###.###.###-##"
                      : "##.###.###/####-##";

                  let i = 0;
                  let result = "";
                  for (let j = 0; j < mask.length; j++) {
                    if (i >= digits.length) break;
                    if (mask[j] === "#") result += digits[i++];
                    else result += mask[j];
                  }

                  setForm((prev) => ({ ...prev, documento: result }));
                }}
                maxLength={18}
              />
            </div>

            <div className="register-field">
              <label>Número de celular</label>
              <input
                placeholder="(00) 00000-0000"
                value={form.celular}
                onChange={(e) =>
                  setForm((prev) => ({
                    ...prev,
                    celular: applyMask(
                      e.target.value,
                      "(##) #####-####",
                      prev.celular,
                    ),
                  }))
                }
                maxLength={15}
              />
            </div>

            <div className="register-field">
              <label>Senha</label>
              <div className="register-password-wrap">
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
                  className="register-lock-icon"
                >
                  <rect width="18" height="11" x="3" y="11" rx="2" ry="2" />
                  <path d="M7 11V7a5 5 0 0 1 10 0v4" />
                </svg>
                <input
                  type={showPassword ? "text" : "password"}
                  placeholder="*********"
                  value={form.senha}
                  onChange={handleChange("senha")}
                  className="register-password-input"
                />
                <button
                  className="register-eye-btn"
                  onClick={() => setShowPassword((v) => !v)}
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
              className="register-submit"
              onClick={handleSubmit}
              disabled={loading}
            >
              {loading ? "Criando conta..." : "Criar conta"}
            </button>

            <p className="register-login">
              Já tem uma conta? <Link to="/login">Entre aqui</Link>
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}
