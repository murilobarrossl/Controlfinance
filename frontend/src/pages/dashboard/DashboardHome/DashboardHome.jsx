import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { getDashboardSummary } from "../../../api/dashboard.js";
import { getIntegrations, getConnectors } from "../../../api/polp.js";
import { useAuth } from "../../../context/AuthContext.jsx";
import Card from "../../../components/ui/Card/Card.jsx";
import Button from "../../../components/ui/Button/Button.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import BarComparisonChart from "../../../components/charts/BarComparisonChart.jsx";
import CategoryDonutChart from "../../../components/charts/CategoryDonutChart.jsx";
import AccountSwitcher from "../../../components/dashboard/AccountSwitcher/AccountSwitcher.jsx";
import { WalletIcon, TrendUpIcon, TrendDownIcon, TargetIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import { formatCurrency } from "../../../utils/financeMath.js";
import "./DashboardHome.css";

const MAX_DONUT_SLICES = 4;

function formatDate(dateStr) {
  return new Date(dateStr).toLocaleDateString("pt-BR");
}

function buildDonutData(categoryExpenses) {
  const sorted = [...categoryExpenses].sort((a, b) => b.amount - a.amount);
  const top = sorted.slice(0, MAX_DONUT_SLICES);
  const rest = sorted.slice(MAX_DONUT_SLICES);
  const restTotal = rest.reduce((sum, c) => sum + c.amount, 0);

  const data = top.map((c) => ({ name: c.categoryName, value: c.amount }));
  if (restTotal > 0) data.push({ name: "Outros", value: restTotal, color: "#808080" });

  return data;
}

export default function DashboardHome() {
  const { user } = useAuth();
  const [accounts, setAccounts] = useState([]);
  const [connectors, setConnectors] = useState([]);
  const [selectedAccountId, setSelectedAccountId] = useState(null);
  const [summary, setSummary] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    // Lista de contas e conectores são só pro seletor de banco: se falhar, o
    // dashboard segue funcionando normalmente com a conta ativa padrão.
    getIntegrations().then(setAccounts).catch(() => {});
    getConnectors().then(setConnectors).catch(() => {});
  }, []);

  useEffect(() => {
    // Guarda contra resposta fora de ordem: se o usuário trocar de conta de novo antes dessa
    // requisição voltar, ignora o resultado, senão a tela pode acabar mostrando o resumo da
    // conta anterior por cima da conta selecionada agora.
    let cancelled = false;

    getDashboardSummary(selectedAccountId)
      .then((data) => {
        if (!cancelled) setSummary(data);
      })
      .catch((err) => {
        if (!cancelled) setError(err.message || "Não foi possível carregar o dashboard.");
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [selectedAccountId]);

  function handleSelectAccount(accountId) {
    setLoading(true);
    setSelectedAccountId(accountId);
  }

  if (loading) return <p className="dashboard-home__hint">Carregando dashboard...</p>;
  if (error) return <p className="dashboard-home__error">{error}</p>;

  if (!summary?.activeAccount) {
    return (
      <Card className="dashboard-home__empty">
        <p>{summary?.message || "Nenhuma conta bancária cadastrada."}</p>
        <Button as={Link} to="/conectar-banco" variant="primary" size="md">
          Conectar banco
        </Button>
      </Card>
    );
  }

  const { activeAccount, totalIncome, totalExpense, pendingTransactions, categoryExpenses, activeCard } = summary;
  const monthlyBalance = totalIncome - totalExpense;
  const upcoming = [...pendingTransactions]
    .sort((a, b) => new Date(a.dueDate) - new Date(b.dueDate))
    .slice(0, 5);
  const donutData = buildDonutData(categoryExpenses);
  const firstName = user?.name?.split(" ")[0];

  return (
    <div className="dashboard-home">
      <div className="dashboard-home__header">
        <SectionHeading
          kicker="Visão geral"
          title={firstName ? `Bem-vindo(a), ${firstName}` : "Bem-vindo(a)"}
          align="left"
        />

        <AccountSwitcher
          accounts={accounts}
          activeAccountId={activeAccount.id}
          connectors={connectors}
          onSelect={handleSelectAccount}
        />
      </div>

      <div className="dashboard-home__stats">
        <Card className="dashboard-home__stat">
          <div className="dashboard-home__stat-header">
            <span className="dashboard-home__stat-icon">
              <WalletIcon />
            </span>
            <span className="dashboard-home__stat-label">Saldo da conta</span>
          </div>
          <span className="dashboard-home__stat-value">{formatCurrency(activeAccount.balance)}</span>
        </Card>
        <Card className="dashboard-home__stat">
          <div className="dashboard-home__stat-header">
            <span className="dashboard-home__stat-icon dashboard-home__stat-icon--income">
              <TrendUpIcon />
            </span>
            <span className="dashboard-home__stat-label">Receitas do mês</span>
          </div>
          <span className="dashboard-home__stat-value dashboard-home__stat-value--income">
            {formatCurrency(totalIncome)}
          </span>
        </Card>
        <Card className="dashboard-home__stat">
          <div className="dashboard-home__stat-header">
            <span className="dashboard-home__stat-icon dashboard-home__stat-icon--expense">
              <TrendDownIcon />
            </span>
            <span className="dashboard-home__stat-label">Despesas do mês</span>
          </div>
          <span className="dashboard-home__stat-value dashboard-home__stat-value--expense">
            {formatCurrency(totalExpense)}
          </span>
        </Card>
        <Card className="dashboard-home__stat">
          <div className="dashboard-home__stat-header">
            <span
              className={`dashboard-home__stat-icon ${
                monthlyBalance >= 0 ? "dashboard-home__stat-icon--income" : "dashboard-home__stat-icon--expense"
              }`}
            >
              <TargetIcon />
            </span>
            <span className="dashboard-home__stat-label">Saldo do mês</span>
          </div>
          <span
            className={`dashboard-home__stat-value ${
              monthlyBalance >= 0 ? "dashboard-home__stat-value--income" : "dashboard-home__stat-value--expense"
            }`}
          >
            {formatCurrency(monthlyBalance)}
          </span>
        </Card>
      </div>

      <div className="dashboard-home__grid">
        <Card title="Receitas x despesas (mês)">
          <BarComparisonChart
            height={180}
            formatValue={formatCurrency}
            data={[
              { name: "Receitas", value: totalIncome, color: "#4ECDC4" },
              { name: "Despesas", value: totalExpense, color: "#ED4A31" },
            ]}
          />
        </Card>

        <Card title="Principais categorias (mês atual)">
          {donutData.length === 0 ? (
            <p className="dashboard-home__hint">Nenhuma despesa categorizada este mês.</p>
          ) : (
            <CategoryDonutChart height={200} formatValue={formatCurrency} data={donutData} />
          )}
        </Card>
      </div>

      <div className="dashboard-home__grid">
        <Card title="Próximos vencimentos">
          {upcoming.length === 0 ? (
            <p className="dashboard-home__hint">Nenhuma conta pendente.</p>
          ) : (
            <ul className="dashboard-home__list">
              {upcoming.map((t) => (
                <li key={t.id} className="dashboard-home__list-item">
                  <div>
                    <span className="dashboard-home__list-name">{t.name}</span>
                    <span className="dashboard-home__list-meta">
                      {t.categoryName || "Sem categoria"} · {formatDate(t.dueDate)}
                    </span>
                  </div>
                  <span
                    className={`dashboard-home__list-amount ${
                      t.type === "Income" ? "dashboard-home__stat-value--income" : "dashboard-home__stat-value--expense"
                    }`}
                  >
                    {formatCurrency(t.amount)}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </Card>

        {activeCard && (
          <Card title={`Cartão ${activeCard.card.name}`}>
            <p className="dashboard-home__card-line">
              Fatura atual: <strong>{formatCurrency(activeCard.currentInvoice)}</strong>
            </p>
            <p className="dashboard-home__card-line">
              Vencimento: <strong>{formatDate(activeCard.invoiceDueDate)}</strong>
            </p>
            <p className="dashboard-home__card-line">
              Limite disponível: <strong>{formatCurrency(activeCard.card.availableLimit)}</strong> de{" "}
              {formatCurrency(activeCard.card.creditLimit)}
            </p>
            <div className="dashboard-home__progress">
              <div
                className="dashboard-home__progress-fill"
                style={{
                  width: `${Math.min(100, (activeCard.card.usedLimit / activeCard.card.creditLimit) * 100)}%`,
                }}
              />
            </div>
          </Card>
        )}
      </div>
    </div>
  );
}
