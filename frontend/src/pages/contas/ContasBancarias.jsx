import { useState, useEffect } from "react";
import { useNavigate, Link } from "react-router-dom";
import {
  getBankAccounts,
  createBankAccount,
  updateBankAccount,
  deleteBankAccount,
} from "../../api/finance.js";
import "./contas.css";

const fmt = (v) =>
  Number(v).toLocaleString("pt-BR", { style: "currency", currency: "BRL" });

const BANKS = [
  { code: "001", name: "Banco do Brasil", color: "#F9C200" },
  { code: "260", name: "Nubank", color: "#820AD1" },
  { code: "341", name: "Itaú", color: "#EC7000" },
  { code: "033", name: "Santander", color: "#CC0000" },
  { code: "336", name: "C6 Bank", color: "#222" },
  { code: "237", name: "Bradesco", color: "#CC092F" },
  { code: "077", name: "Inter", color: "#FF6B00" },
  { code: "104", name: "Caixa Econômica", color: "#005CA9" },
  { code: "other", name: "Outro", color: "#8a9bbf" },
];

const getBankColor = (code) =>
  BANKS.find((b) => b.code === code)?.color ?? "#8a9bbf";
const getBankName = (code) =>
  BANKS.find((b) => b.code === code)?.name ?? "Banco";

const NAV_LINKS = [
  { to: "/importacao", label: "Importação de Extratos" },
  { to: "/dashboard", label: "Dashboard Inteligente" },
  { to: "/contas-bancarias", label: "Contas Bancárias" },
  { to: "/receitas", label: "Receitas" },
  { to: "/despesas", label: "Despesas" },
  { to: "/relatorios", label: "Relatórios" },
  { to: "/categorias", label: "Categorias" },
  { to: "/conciliacao", label: "Conciliação" },
  { to: "/planejamento", label: "Planejamento" },
];

function Modal({ title, onClose, children }) {
  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-box" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h3>{title}</h3>
          <button className="modal-close" onClick={onClose}>
            ✕
          </button>
        </div>
        {children}
      </div>
    </div>
  );
}

export default function ContasBancarias() {
  const navigate = useNavigate();
  const user = JSON.parse(localStorage.getItem("user") ?? "{}");

  const [accounts, setAccounts] = useState([]);
  const [loading, setLoading] = useState(true);
  const [modal, setModal] = useState(null); // "create" | "edit" | "delete"
  const [selected, setSelected] = useState(null);
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);

  const [form, setForm] = useState({ name: "", bankCode: "001", balance: "" });

  const load = () => {
    setLoading(true);
    getBankAccounts()
      .then((data) => setAccounts(Array.isArray(data) ? data : []))
      .catch(() => setAccounts([]))
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    load();
  }, []);

  const openCreate = () => {
    setForm({ name: "", bankCode: "001", balance: "" });
    setError("");
    setModal("create");
  };

  const openEdit = (acc) => {
    setSelected(acc);
    setForm({
      name: acc.name,
      bankCode: acc.bankCode ?? "001",
      balance: String(acc.balance),
    });
    setError("");
    setModal("edit");
  };

  const openDelete = (acc) => {
    setSelected(acc);
    setModal("delete");
  };

  const handleSave = async () => {
    if (!form.name.trim()) {
      setError("Nome é obrigatório.");
      return;
    }
    const balance = parseFloat(form.balance.replace(",", "."));
    if (isNaN(balance)) {
      setError("Saldo inválido.");
      return;
    }

    setSaving(true);
    setError("");
    try {
      const dto = { name: form.name.trim(), bankCode: form.bankCode, balance };
      if (modal === "create") await createBankAccount(dto);
      else await updateBankAccount(selected.id, dto);
      setModal(null);
      load();
    } catch (err) {
      setError(err.message);
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async () => {
    setSaving(true);
    try {
      await deleteBankAccount(selected.id);
      setModal(null);
      load();
    } catch (err) {
      setError(err.message);
    } finally {
      setSaving(false);
    }
  };

  const logout = () => {
    localStorage.clear();
    navigate("/loginemail");
  };

  const totalBalance = accounts.reduce((s, a) => s + a.balance, 0);

  return (
    <div className="contas-root">
      {/* NAVBAR */}
      <nav className="dash-nav">
        <a href="/dashboard" className="dash-nav-logo">
          <svg width="34" height="34" viewBox="0 0 34 34" fill="none">
            <rect width="34" height="34" rx="7" fill="#0a1a4a" />
            <rect x="4" y="21" width="4" height="9" rx="1" fill="#1a3a6b" />
            <rect x="10" y="16" width="4" height="14" rx="1" fill="#1a3a6b" />
            <rect x="16" y="11" width="4" height="19" rx="1" fill="#1a3a6b" />
            <rect x="22" y="6" width="4" height="24" rx="1" fill="#1a3a6b" />
            <path
              d="M6 19 L12 14 L18 9 L24 4"
              stroke="#e84520"
              strokeWidth="2"
              strokeLinecap="round"
            />
            <circle cx="6" cy="19" r="2" fill="#e84520" />
            <circle cx="12" cy="14" r="2" fill="#e84520" />
            <circle cx="18" cy="9" r="2" fill="#e84520" />
            <circle cx="24" cy="4" r="2" fill="#e84520" />
          </svg>
          <div className="dash-nav-brand">
            <span className="brand-control">Control</span>
            <span className="brand-finance">Finance</span>
          </div>
        </a>
        <div className="dash-nav-links">
          {NAV_LINKS.map((l) => (
            <Link
              key={l.to}
              to={l.to}
              className={l.to === "/contas-bancarias" ? "active" : ""}
            >
              {l.label}
            </Link>
          ))}
        </div>
        <div className="dash-nav-right">
          <span className="dash-nav-cnpj">{user.document ?? "/CNPJ"}</span>
          <button className="dash-logout" onClick={logout}>
            <svg
              width="18"
              height="18"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
            >
              <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
              <polyline points="16 17 21 12 16 7" />
              <line x1="21" y1="12" x2="9" y2="12" />
            </svg>
          </button>
        </div>
      </nav>

      <div className="contas-body">
        {/* HEADER */}
        <div className="contas-header">
          <div>
            <h1 className="contas-title">Contas Bancárias</h1>
            <p className="contas-subtitle">Gerencie suas contas e saldos</p>
          </div>
          <button className="btn-primary" onClick={openCreate}>
            + Nova Conta
          </button>
        </div>

        {/* TOTAL */}
        <div className="contas-total-card">
          <div>
            <div className="contas-total-label">Saldo Total</div>
            <div className="contas-total-value">{fmt(totalBalance)}</div>
          </div>
          <div className="contas-total-count">
            {accounts.length} conta{accounts.length !== 1 ? "s" : ""}
          </div>
        </div>

        {/* LISTA */}
        {loading ? (
          <div className="contas-empty">Carregando...</div>
        ) : accounts.length === 0 ? (
          <div className="contas-empty">
            <div className="contas-empty-icon">🏦</div>
            <p>Nenhuma conta cadastrada ainda.</p>
            <button className="btn-primary" onClick={openCreate}>
              Adicionar primeira conta
            </button>
          </div>
        ) : (
          <div className="contas-grid">
            {accounts.map((acc) => (
              <div key={acc.id} className="conta-card">
                <div className="conta-card-top">
                  <div
                    className="conta-bank-icon"
                    style={{ background: getBankColor(acc.bankCode) }}
                  >
                    {(acc.bankCode ? getBankName(acc.bankCode) : acc.name)
                      .slice(0, 2)
                      .toUpperCase()}
                  </div>
                  <div className="conta-actions">
                    <button
                      className="conta-btn-edit"
                      onClick={() => openEdit(acc)}
                      title="Editar"
                    >
                      ✎
                    </button>
                    <button
                      className="conta-btn-delete"
                      onClick={() => openDelete(acc)}
                      title="Excluir"
                    >
                      ✕
                    </button>
                  </div>
                </div>
                <div className="conta-name">{acc.name}</div>
                <div className="conta-bank">
                  {acc.bankCode ? getBankName(acc.bankCode) : "—"}
                </div>
                <div className="conta-balance">{fmt(acc.balance)}</div>
                <div className="conta-status">
                  {acc.isActive ? "● Ativa" : "○ Inativa"}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* MODAL CRIAR / EDITAR */}
      {(modal === "create" || modal === "edit") && (
        <Modal
          title={modal === "create" ? "Nova Conta" : "Editar Conta"}
          onClose={() => setModal(null)}
        >
          <div className="modal-body">
            {error && <div className="modal-error">{error}</div>}

            <div className="modal-field">
              <label>Nome da conta</label>
              <input
                placeholder="ex: Conta Corrente BB"
                value={form.name}
                onChange={(e) =>
                  setForm((f) => ({ ...f, name: e.target.value }))
                }
              />
            </div>

            <div className="modal-field">
              <label>Banco</label>
              <select
                value={form.bankCode}
                onChange={(e) =>
                  setForm((f) => ({ ...f, bankCode: e.target.value }))
                }
              >
                {BANKS.map((b) => (
                  <option key={b.code} value={b.code}>
                    {b.name}
                  </option>
                ))}
              </select>
            </div>

            <div className="modal-field">
              <label>Saldo atual (R$)</label>
              <input
                placeholder="0,00"
                value={form.balance}
                onChange={(e) =>
                  setForm((f) => ({ ...f, balance: e.target.value }))
                }
                inputMode="decimal"
              />
            </div>

            <div className="modal-actions">
              <button className="btn-secondary" onClick={() => setModal(null)}>
                Cancelar
              </button>
              <button
                className="btn-primary"
                onClick={handleSave}
                disabled={saving}
              >
                {saving ? "Salvando..." : "Salvar"}
              </button>
            </div>
          </div>
        </Modal>
      )}

      {/* MODAL EXCLUIR */}
      {modal === "delete" && (
        <Modal title="Excluir Conta" onClose={() => setModal(null)}>
          <div className="modal-body">
            <p style={{ color: "#001647", marginBottom: 20 }}>
              Tem certeza que deseja excluir a conta{" "}
              <strong>{selected?.name}</strong>? Esta ação não pode ser
              desfeita.
            </p>
            <div className="modal-actions">
              <button className="btn-secondary" onClick={() => setModal(null)}>
                Cancelar
              </button>
              <button
                className="btn-danger"
                onClick={handleDelete}
                disabled={saving}
              >
                {saving ? "Excluindo..." : "Excluir"}
              </button>
            </div>
          </div>
        </Modal>
      )}
    </div>
  );
}
