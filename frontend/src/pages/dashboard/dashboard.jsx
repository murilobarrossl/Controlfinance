import { useState, useEffect } from "react";
import { Link, useNavigate } from "react-router-dom";
import { getBankAccounts, getDashboard } from "../../api/finance.js";
import logoappfinancebranco from "../../assets/images/logoappfinancebranco.png";
import "./dashboard.css";

const fmt = (v) =>
  Number(v).toLocaleString("pt-BR", { style: "currency", currency: "BRL" });

const DONUT_COLORS = ["#110078", "#003d8f", "#001fac", "#0017ea", "#0029ff"];

const MOCK = {
  balance: 0,
  income: 0,
  expense: 0,
  bankName: "Nenhuma conta",
  categories: [],
  bills: { pending: [], paid: [] },
  cards: [],
};

const formatDue = (dateStr, status) => {
  if (status === "Overdue") return "Em Atraso";
  const d = new Date(dateStr);
  const now = new Date();
  const diff = Math.round((d - now) / 86400000);
  if (diff === 0) return "Hoje";
  if (diff === 1) return "Amanhã";
  return d.toLocaleDateString("pt-BR", { day: "2-digit", month: "2-digit" });
};

const mapTx = (t) => ({
  id: t.id,
  name: t.name,
  type:
    t.type === "Income" ? "Receita" : t.type === "Expense" ? "Despesa" : t.type,
  amount: t.amount,
  dueDate: t.dueDate,
  status: t.status,
  isIncome: t.type === "Income",
  due: formatDue(t.dueDate, t.status),
});

const mapCard = (c) => ({
  id: c.card.id,
  name: c.card.name,
  limitAvailable: c.card.availableLimit,
  limitTotal: c.card.creditLimit,
  invoice: c.currentInvoice,
  invoiceDue: c.invoiceDueDate,
  parcels:
    c.installments?.map((i) => ({
      name: i.description,
      info: `Parcela ${i.currentInstallment}/${i.totalInstallments}`,
      value: i.installmentAmount,
      vence: new Date(i.nextDueDate)
        .toLocaleDateString("pt-BR", { day: "2-digit", month: "short" })
        .toUpperCase(),
    })) ?? [],
});

// ── Logo ──────────────────────────────────────────────────────
function LogoIcon() {
  return (
    <img
      src={logoappfinancebranco}
      alt="Control Finance"
      style={{ width: 120, height: 40 }}
    />
  );
}

// ── Donut Chart ───────────────────────────────────────────────
function DonutChart({ data }) {
  const size = 160,
    cx = 80,
    cy = 80,
    R = 58,
    thickness = 22;
  const total = data.reduce((s, d) => s + d.value, 0) || 1;
  const slices = data.reduce((acc, d, i) => {
    const prevAngle = acc.length
      ? acc[acc.length - 1].start + acc[acc.length - 1].sweep
      : -90;
    const sweep = (d.value / total) * 360;
    return [
      ...acc,
      {
        ...d,
        sweep,
        start: prevAngle,
        color: DONUT_COLORS[i % DONUT_COLORS.length],
      },
    ];
  }, []);
  const polar = (deg, r) => {
    const rad = ((deg - 90) * Math.PI) / 180;
    return [cx + r * Math.cos(rad), cy + r * Math.sin(rad)];
  };
  const arc = (start, sweep) => {
    const end = start + sweep - 0.5;
    const [x1, y1] = polar(start, R);
    const [x2, y2] = polar(end, R);
    return `M${x1},${y1} A${R},${R},0,${sweep > 180 ? 1 : 0},1,${x2},${y2}`;
  };

  if (data.length === 0) {
    return (
      <div className="donut-wrap">
        <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`}>
          <circle
            cx={cx}
            cy={cy}
            r={R}
            fill="none"
            stroke="#f0f0f0"
            strokeWidth={thickness}
          />
          <text
            x={cx}
            y={cy + 5}
            textAnchor="middle"
            className="donut-center-text"
          >
            GASTOS
          </text>
        </svg>
        <div className="donut-legend">
          <span style={{ fontSize: 12, color: "#8a9bbf" }}>Sem dados</span>
        </div>
      </div>
    );
  }

  return (
    <div className="donut-wrap">
      <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`}>
        {slices.map((s, i) => (
          <path
            key={i}
            d={arc(s.start, s.sweep)}
            fill="none"
            stroke={s.color}
            strokeWidth={thickness}
          />
        ))}
        <text
          x={cx}
          y={cy + 5}
          textAnchor="middle"
          className="donut-center-text"
        >
          GASTOS
        </text>
      </svg>
      <div className="donut-legend">
        {slices.map((s, i) => (
          <div key={i} className="donut-item">
            <span className="donut-dot" style={{ background: s.color }} />
            <span className="donut-name">{s.name}</span>
            <span className="donut-pct">
              {Math.round((s.value / total) * 100)}%
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}

// ── Bar Chart ─────────────────────────────────────────────────
function BarChart({ income, expense }) {
  const max = Math.max(income, expense, 1);
  const yLabels = [6000, 5000, 4000, 3000, 0];
  return (
    <div className="bar-chart">
      <div className="bar-y">
        {yLabels.map((v) => (
          <span key={v}>R$ {v.toLocaleString("pt-BR")}</span>
        ))}
      </div>
      <div className="bar-cols">
        <div className="bar-col">
          <span className="bar-val">{fmt(income)}</span>
          <div
            className="bar income"
            style={{ height: `${(income / max) * 140}px` }}
          />
          <span className="bar-lbl">Receitas</span>
        </div>
        <div className="bar-col">
          <span className="bar-val">{fmt(expense)}</span>
          <div
            className="bar expense"
            style={{ height: `${(expense / max) * 140}px` }}
          />
          <span className="bar-lbl">Despesas</span>
        </div>
      </div>
    </div>
  );
}

// ── Card Visual ───────────────────────────────────────────────
function CardVisual() {
  return (
    <div className="card-visual">
      <div className="card-visual-top">
        <div className="card-chip">
          <svg width="28" height="22" viewBox="0 0 28 22" fill="none">
            <rect width="28" height="22" rx="3" fill="#d4a93a" />
            <rect
              x="9"
              width="10"
              height="22"
              rx="2"
              fill="#c49020"
              opacity="0.5"
            />
            <rect
              y="7"
              width="28"
              height="8"
              rx="1"
              fill="#c49020"
              opacity="0.4"
            />
          </svg>
        </div>
        <div className="card-nfc">
          <svg
            width="18"
            height="18"
            viewBox="0 0 24 24"
            fill="none"
            stroke="rgba(255,255,255,0.7)"
            strokeWidth="2"
          >
            <path d="M1 6a11 11 0 0 1 0 12" />
            <path d="M5 8a7 7 0 0 1 0 8" />
            <path d="M9 10a3 3 0 0 1 0 4" />
          </svg>
        </div>
      </div>
      <div className="card-number">•••• •••• •••• 4321</div>
      <div className="card-bottom">
        <div>
          <div className="card-label-sm">TITULAR</div>
          <div className="card-holder">BRUNO P. SIQUEIRA</div>
        </div>
        <div className="card-brand">
          <svg width="48" height="30" viewBox="0 0 48 30">
            <circle cx="18" cy="15" r="13" fill="#e84520" opacity="0.9" />
            <circle cx="30" cy="15" r="13" fill="#f5a623" opacity="0.9" />
          </svg>
        </div>
      </div>
    </div>
  );
}

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

export default function Dashboard() {
  const navigate = useNavigate();
  const user = JSON.parse(localStorage.getItem("user") ?? "{}");

  const [data, setData] = useState(MOCK);
  const [accounts, setAccounts] = useState([]);
  const [selectedAcc, setSelectedAcc] = useState(null);
  const [selectedCard, setSelectedCard] = useState(0);
  const [tab, setTab] = useState("pending");
  const [loading, setLoading] = useState(true);

  const loadDashboard = (bankAccountId) => {
    setLoading(true);
    getDashboard(bankAccountId)
      .then((dash) => {
        if (dash && !dash.message) {
          setData({
            balance: dash.activeAccount?.balance ?? 0,
            income: dash.totalIncome ?? 0,
            expense: dash.totalExpense ?? 0,
            bankName: dash.activeAccount?.name ?? "—",
            categories: dash.categoryExpenses?.length
              ? dash.categoryExpenses.map((c) => ({
                  name: c.categoryName,
                  value: c.percentage,
                }))
              : [],
            bills: {
              pending: dash.pendingTransactions?.map(mapTx) ?? [],
              paid: dash.paidTransactions?.map(mapTx) ?? [],
            },
            cards: dash.activeCard ? [mapCard(dash.activeCard)] : [],
          });
        } else {
          setData(MOCK);
        }
      })
      .catch(() => setData(MOCK))
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    getBankAccounts()
      .then((accs) => {
        const arr = Array.isArray(accs) ? accs : [];
        setAccounts(arr);
        if (arr.length) {
          setSelectedAcc(arr[0]);
          loadDashboard(arr[0].id);
        } else {
          setLoading(false);
        }
      })
      .catch(() => setLoading(false));
  }, []);

  const logout = () => {
    localStorage.clear();
    navigate("/loginemail");
  };

  if (loading)
    return (
      <div className="dash-loading">
        <span>Carregando...</span>
      </div>
    );

  const card = data.cards?.[selectedCard];
  const bills = tab === "pending" ? data.bills.pending : data.bills.paid;
  const balance = selectedAcc?.balance ?? data.balance;
  const bankName = selectedAcc?.name ?? data.bankName;
  const daysUntilDue = card
    ? Math.max(
        0,
        Math.round((new Date(card.invoiceDue) - new Date()) / 86400000),
      )
    : 0;

  return (
    <div className="dash-root">
      {/* NAVBAR */}
      <nav className="dash-nav">
        <a href="/dashboard" className="dash-nav-logo">
          <LogoIcon />
        </a>
        <div className="dash-nav-links">
          {NAV_LINKS.map((l) => (
            <Link
              key={l.to}
              to={l.to}
              className={l.to === "/dashboard" ? "active" : ""}
            >
              {l.label}
            </Link>
          ))}
        </div>
        <div className="dash-nav-right">
          <span className="dash-nav-cnpj">{user.document ?? "/CNPJ"}</span>
          <button className="dash-logout" onClick={logout} title="Sair">
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

      <div className="dash-body">
        {/* SELETOR DE CONTA */}
        <div className="dash-account-selector">
          <div className="dash-account-pill">
            <div className="dash-account-toggle">
              <span className="toggle-inner" />
            </div>
            <select
              className="dash-account-select"
              value={selectedAcc?.id ?? ""}
              onChange={(e) => {
                const acc = accounts.find(
                  (a) => String(a.id) === e.target.value,
                );
                setSelectedAcc(acc ?? null);
                if (acc) loadDashboard(acc.id);
              }}
            >
              {accounts.length ? (
                accounts.map((a) => (
                  <option key={a.id} value={a.id}>
                    {a.name}
                  </option>
                ))
              ) : (
                <option value="">Nenhuma conta</option>
              )}
            </select>
            <svg
              width="14"
              height="14"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2.5"
            >
              <polyline points="6 9 12 15 18 9" />
            </svg>
          </div>
        </div>

        {/* SEM CONTA */}
        {accounts.length === 0 && (
          <div
            style={{
              textAlign: "center",
              padding: "60px 20px",
              color: "#8a9bbf",
            }}
          >
            <p style={{ fontSize: 16, marginBottom: 16 }}>
              Nenhuma conta bancária cadastrada.
            </p>
            <Link
              to="/contas-bancarias"
              style={{ color: "#e84520", fontWeight: 700 }}
            >
              + Adicionar conta
            </Link>
          </div>
        )}

        {accounts.length > 0 && (
          <>
            {/* ROW 1 */}
            <div className="dash-row1">
              <div className="dash-card">
                <div className="dash-card-title">Saldo da Conta</div>
                <div className="dash-balance-value">{fmt(balance)}</div>
                <div className="dash-balance-bank">{bankName}</div>
              </div>
              <div className="dash-card">
                <div className="dash-card-title">Receitas e Despesas/Mês</div>
                <BarChart income={data.income} expense={data.expense} />
              </div>
              <div className="dash-card">
                <div className="dash-card-title">Categorias</div>
                <DonutChart data={data.categories} />
              </div>
            </div>

            {/* ROW 2 */}
            <div className="dash-row2">
              {/* CONTAS A PAGAR */}
              <div className="dash-card">
                <div className="dash-bill-header">
                  <div>
                    <div
                      className="dash-card-title"
                      style={{ marginBottom: 10 }}
                    >
                      Contas a Pagar e Receber
                    </div>
                    <div className="dash-bill-tabs">
                      <button
                        className={`dash-tab ${tab === "pending" ? "active" : ""}`}
                        onClick={() => setTab("pending")}
                      >
                        Pendentes ({data.bills.pending.length})
                      </button>
                      <button
                        className={`dash-tab ${tab === "paid" ? "active" : ""}`}
                        onClick={() => setTab("paid")}
                      >
                        Pagos ({data.bills.paid.length})
                      </button>
                    </div>
                  </div>
                  <Link to="/receitas">
                    <button className="dash-add-btn">+ Adicionar</button>
                  </Link>
                </div>

                {bills.length === 0 ? (
                  <div
                    style={{
                      textAlign: "center",
                      padding: "24px 0",
                      color: "#8a9bbf",
                      fontSize: 13,
                    }}
                  >
                    Nenhuma transação encontrada.
                  </div>
                ) : (
                  <table className="dash-bill-table">
                    <thead>
                      <tr>
                        <th></th>
                        <th>Nome</th>
                        <th>Tipo</th>
                        <th>Valor</th>
                        <th>Vencimento</th>
                      </tr>
                    </thead>
                    <tbody>
                      {bills.map((b) => (
                        <tr key={b.id}>
                          <td>
                            <span
                              className={`bill-icon ${b.isIncome ? "plus" : "minus"}`}
                            >
                              {b.isIncome ? "+" : "−"}
                            </span>
                          </td>
                          <td>{b.name}</td>
                          <td className="bill-type">{b.type}</td>
                          <td className="bill-amount">{fmt(b.amount)}</td>
                          <td
                            className={
                              b.due === "Em Atraso" ? "due-overdue" : ""
                            }
                          >
                            {b.due}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </div>

              {/* CARTÕES */}
              {card ? (
                <div className="dash-card">
                  <div className="dash-card-header">
                    <div className="dash-card-title" style={{ margin: 0 }}>
                      Cartões
                    </div>
                    <select
                      className="dash-card-select"
                      value={selectedCard}
                      onChange={(e) => setSelectedCard(Number(e.target.value))}
                    >
                      {data.cards.map((c, i) => (
                        <option key={c.id} value={i}>
                          {c.name}
                        </option>
                      ))}
                    </select>
                    <span className="dash-card-due-link">
                      (Ver fatura completa)
                    </span>
                    <span className="dash-card-due">
                      Vence em {daysUntilDue} Dias
                    </span>
                  </div>
                  <div className="dash-card-body">
                    <div className="dash-card-limit">
                      <h4>Limite Disponível</h4>
                      <div className="dash-card-limit-val">
                        {fmt(card.limitAvailable)}
                      </div>
                      <div className="dash-limit-bar-bg">
                        <div
                          className="dash-limit-bar"
                          style={{
                            width: `${((card.limitTotal - card.limitAvailable) / card.limitTotal) * 100}%`,
                          }}
                        />
                      </div>
                      <div className="dash-limit-total">
                        Limite Total {fmt(card.limitTotal)}
                      </div>
                    </div>
                    <div className="dash-card-invoice">
                      <h4>Fatura Atual (Aberta)</h4>
                      <div className="dash-invoice-val">
                        {fmt(card.invoice)}
                      </div>
                      <div className="dash-invoice-sub">
                        Vencimento:{" "}
                        {new Date(card.invoiceDue).toLocaleDateString("pt-BR")}
                      </div>
                    </div>
                    <div className="dash-card-parcels">
                      <h4>Parcelas a vencer</h4>
                      {card.parcels.map((p, i) => (
                        <div key={i} className="dash-parcel-row">
                          <div>
                            <div className="dash-parcel-name">{p.name}</div>
                            <div className="dash-parcel-info">{p.info}</div>
                          </div>
                          <div style={{ textAlign: "right" }}>
                            <div className="dash-parcel-val">
                              {fmt(p.value)}
                            </div>
                            <div className="dash-parcel-info">
                              Vence {p.vence}
                            </div>
                          </div>
                        </div>
                      ))}
                    </div>
                    <CardVisual />
                  </div>
                </div>
              ) : (
                <div
                  className="dash-card"
                  style={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "center",
                    flexDirection: "column",
                    gap: 12,
                    color: "#8a9bbf",
                  }}
                >
                  <div style={{ fontSize: 32 }}>💳</div>
                  <p style={{ fontSize: 14 }}>Nenhum cartão cadastrado.</p>
                  <Link
                    to="/cartoes"
                    style={{ color: "#e84520", fontWeight: 700, fontSize: 13 }}
                  >
                    + Adicionar cartão
                  </Link>
                </div>
              )}
            </div>
          </>
        )}
      </div>
    </div>
  );
}
