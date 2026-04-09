// imports
import { Link } from "react-router-dom";
import { useState } from "react";
import graficologo from "../../assets/images/graficologo.png";
import logoappfinance from "../../assets/images/logoappfinance.png";
import "./register.css";

export default function RegisterPage() {
  // controla visibilidade da senha
  const [showPassword, setShowPassword] = useState(false);

  // estado do formulário (SUBSTITUIR PELAS APIS DO BACKEND QUANDO TIVER PRONTO!!!)
  const [form, setForm] = useState({
    nome: "",
    email: "",
    celular: "",
    documento: "",
    senha: "",
  });

  // atualiza os campos do formulário
  const handleChange = (field) => (e) =>
    setForm((prev) => ({ ...prev, [field]: e.target.value }));

  // envio do formulário
  const handleSubmit = () => {
    console.log(form);
  };

  return (
    <div className="register-page">
      {/* lado esquerdo*/}
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

      {/* formulário */}
      <div className="register-right">
        <div className="register-card">
          <img
            src={logoappfinance}
            alt="Control Finance"
            className="register-logo"
          />

          <div className="register-fields">
            {/* nome */}
            <div className="register-field">
              <label>Nome</label>
              <input
                placeholder="como gostaria de ser chamado"
                value={form.nome}
                onChange={handleChange("nome")}
              />
            </div>

            {/* email */}
            <div className="register-field">
              <label>E-mail</label>
              <input
                type="email"
                placeholder="nome@minhaempresa.com.br"
                value={form.email}
                onChange={handleChange("email")}
              />
            </div>

            {/* CNPJ / CPF */}
            <div className="register-field">
              <label>CNPJ/CPF</label>
              <input
                placeholder="00.000.000/0000-00"
                value={form.documento}
                onChange={handleChange("documento")}
              />
            </div>

            {/* número de celular */}
            <div className="register-field">
              <label>Número de celular</label>
              <input
                placeholder="Número com DDD"
                value={form.celular}
                onChange={handleChange("celular")}
              />
            </div>

            {/* senha */}
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

                {/* mostrar/ocultar senha */}
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
            {/* submit */}
            <button className="register-submit" onClick={handleSubmit}>
              Criar conta
            </button>

            <p className="register-login">
              Já tem uma conta? <Link to="/login">Login</Link>
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}
