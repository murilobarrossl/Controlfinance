import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { getDashboardSummary } from "../../../api/dashboard.js";
import { getTransactions } from "../../../api/transactions.js";
import { getCategories } from "../../../api/categories.js";
import { getIntegrations, getConnectors, syncAllIntegrations } from "../../../api/polp.js";
import { deleteBankAccount } from "../../../api/bankAccounts.js";
import { useAuth } from "../../../context/AuthContext.jsx";
import Card from "../../../components/ui/Card/Card.jsx";
import Button from "../../../components/ui/Button/Button.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import StatCard from "../../../components/ui/StatCard/StatCard.jsx";
import StatusPill from "../../../components/ui/StatusPill/StatusPill.jsx";
import IconAvatar from "../../../components/ui/IconAvatar/IconAvatar.jsx";
import AreaTrendChart from "../../../components/charts/AreaTrendChart.jsx";
import CategoryDonutChart from "../../../components/charts/CategoryDonutChart.jsx";
import AccountSwitcher from "../../../components/dashboard/AccountSwitcher/AccountSwitcher.jsx";
import { WalletIcon, TrendUpIcon, TrendDownIcon, TargetIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import { formatCurrency, formatPercentage } from "../../../utils/financeMath.js";
import { getMonthsWindow, buildMonthlyTrend } from "../../../utils/monthlyTrend.js";
import { computeTrend } from "../../../utils/trend.js";
import "./DashboardHome.css";

const MAX_DONUT_SLICES = 4;
const TREND_MONTHS = 6;
const STATUS_LABELS = { Pending: "Pendente", Paid: "Pago", Overdue: "Atrasado" };

function formatDate(dateStr) {
  return new Date(dateStr).toLocaleDateString("pt-BR");
}

// Usa a mesma cor cadastrada na categoria (a que aparece no gráfico de Categorias), em vez de
// deixar o gráfico sortear uma cor qualquer, pra uma categoria não parecer "outra" de uma tela
// pra outra.
function buildDonutData(categoryExpenses, categories) {
  const sorted = [...categoryExpenses].sort((a, b) => b.amount - a.amount);
  const top = sorted.slice(0, MAX_DONUT_SLICES);
  const rest = sorted.slice(MAX_DONUT_SLICES);
  const restTotal = rest.reduce((sum, c) => sum + c.amount, 0);

  const data = top.map((c) => ({
    name: c.categoryName,
    value: c.amount,
    color: categories.find((cat) => cat.name === c.categoryName)?.color,
  }));
  if (restTotal > 0) data.push({ name: "Outros", value: restTotal, color: "#808080" });

  return data;
}

export default function DashboardHome() {
  const { user } = useAuth();
  const [accounts, setAccounts] = useState([]);
  const [connectors, setConnectors] = useState([]);
  const [categories, setCategories] = useState([]);
  const [selectedAccountId, setSelectedAccountId] = useState(null);
  const [summary, setSummary] = useState(null);
  const [transactions, setTransactions] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [refreshKey, setRefreshKey] = useState(0);

  useEffect(() => {
    // Lista de contas e conectores são só pro seletor de banco: se falhar, o
    // dashboard segue funcionando normalmente com a conta ativa padrão.
    getIntegrations().then(setAccounts).catch(() => {});
    getConnectors().then(setConnectors).catch(() => {});
    getCategories().then(setCategories).catch(() => {});

    // Sincroniza com o banco de verdade ao carregar a página (saldo e transações ficavam
    // congelados no valor de quando a conta foi conectada, sem isso). Não trava o carregamento
    // inicial esperando a Polp responder: dispara em paralelo e só atualiza o resumo (efeito
    // abaixo, via refreshKey) quando a sincronização terminar.
    syncAllIntegrations()
      .then(() => setRefreshKey((key) => key + 1))
      .catch(() => {});
  }, []);

  useEffect(() => {
    // Guarda contra resposta fora de ordem: se o usuário trocar de conta de novo antes dessa
    // requisição voltar, ignora o resultado, senão a tela pode acabar mostrando o resumo da
    // conta anterior por cima da conta selecionada agora.
    let cancelled = false;

    // Busca as transações só depois do resumo responder: o gráfico de tendência precisa saber
    // qual conta ficou ativa (selectedAccountId pode vir nulo, aí o backend escolhe a conta
    // padrão) pra filtrar pela mesma conta dos cards acima — antes buscava transações de todas
    // as contas juntas, e a tendência não batia com "Receitas do mês"/"Despesas do mês".
    getDashboardSummary(selectedAccountId)
      .then((summaryData) => {
        if (cancelled) return;
        setSummary(summaryData);
        const accountId = summaryData?.activeAccount?.id;
        return accountId ? getTransactions({ bankAccountId: accountId }) : [];
      })
      .then((transactionsData) => {
        if (!cancelled) setTransactions(transactionsData ?? []);
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
  }, [selectedAccountId, refreshKey]);

  // Tendência mensal (últimos 6 meses) da mesma conta ativa que os cards acima.
  const monthlyTrend = useMemo(
    () => buildMonthlyTrend(transactions, getMonthsWindow(TREND_MONTHS)),
    [transactions]
  );
  const currentMonthTrend = monthlyTrend[monthlyTrend.length - 1];
  const previousMonthTrend = monthlyTrend[monthlyTrend.length - 2];

  function handleSelectAccount(accountId) {
    setLoading(true);
    setSelectedAccountId(accountId);
  }

  // Desconecta (soft delete) a conta do banco: some da lista, mas o histórico de transações
  // continua no extrato (mesmo comportamento de "conta desativada" já tratado no resto do app).
  // Depois de sair, larga selectedAccountId de volta pra null pra deixar o backend escolher outra
  // conta ativa sozinho, em vez de continuar apontando pra uma conta que não existe mais.
  function handleDisconnectAccount(accountId) {
    setLoading(true);
    deleteBankAccount(accountId)
      .then(() => getIntegrations())
      .then(setAccounts)
      .catch((err) => setError(err.message || "Não foi possível desconectar essa conta."))
      .finally(() => {
        setSelectedAccountId(null);
        setRefreshKey((key) => key + 1);
      });
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
  const percentageSpent = totalIncome > 0 ? (totalExpense / totalIncome) * 100 : 0;
  const savingsRate =
    currentMonthTrend?.Receitas > 0
      ? ((currentMonthTrend.Receitas - currentMonthTrend.Despesas) / currentMonthTrend.Receitas) * 100
      : 0;
  const upcoming = [...pendingTransactions]
    .sort((a, b) => new Date(a.dueDate) - new Date(b.dueDate))
    .slice(0, 5);
  const donutData = buildDonutData(categoryExpenses, categories);
  const firstName = user?.name?.split(" ")[0];

  const incomeTrend = computeTrend(currentMonthTrend?.Receitas, previousMonthTrend?.Receitas);
  const expenseTrend = computeTrend(currentMonthTrend?.Despesas, previousMonthTrend?.Despesas, { invertTone: true });

  // Não existe um saldo histórico salvo (activeAccount.balance é sempre o saldo ao vivo da
  // Polp), então a única comparação honesta é reconstruir o saldo do início do mês a partir
  // do saldo atual menos o que já entrou/saiu neste mês — por isso o rótulo é "início do mês",
  // não "mês anterior" como nos outros cards.
  const startOfMonthBalance = activeAccount.balance - monthlyBalance;
  const balanceTrend = computeTrend(activeAccount.balance, startOfMonthBalance, { label: "vs início do mês" });

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
          onDisconnect={handleDisconnectAccount}
          onSynced={() => setRefreshKey((key) => key + 1)}
        />
      </div>

      <div className="dashboard-home__stats">
        <StatCard
          icon={<WalletIcon />}
          label="Saldo da conta"
          value={formatCurrency(activeAccount.balance)}
          trend={balanceTrend}
        />
        <StatCard
          icon={<TrendUpIcon />}
          label="Receitas do mês"
          value={formatCurrency(totalIncome)}
          valueTone="income"
          trend={incomeTrend}
        />
        <StatCard
          icon={<TrendDownIcon />}
          label="Despesas do mês"
          value={formatCurrency(totalExpense)}
          valueTone="expense"
          trend={expenseTrend}
        />
        <StatCard
          icon={<TargetIcon />}
          label="Saldo do mês (receitas - despesas)"
          value={formatCurrency(monthlyBalance)}
          valueTone={monthlyBalance >= 0 ? "income" : "expense"}
          secondaryLines={[{ label: "% da receita gasta", value: formatPercentage(percentageSpent) }]}
        />
      </div>

      <div className="dashboard-home__grid">
        <Card title={`Receitas x despesas (últimos ${TREND_MONTHS} meses)`}>
          <AreaTrendChart
            height={220}
            formatValue={formatCurrency}
            series={[
              { key: "Receitas", color: "#4ECDC4" },
              { key: "Despesas", color: "#ED4A31" },
            ]}
            data={monthlyTrend}
            badge={{ value: formatPercentage(savingsRate), label: "Taxa de poupança" }}
          />
        </Card>

        <Card title="Principais categorias (mês atual)">
          {donutData.length === 0 ? (
            <p className="dashboard-home__hint">Nenhuma despesa categorizada este mês.</p>
          ) : (
            <CategoryDonutChart
              height={220}
              formatValue={formatCurrency}
              data={donutData}
              centerLabel={formatCurrency(totalExpense)}
            />
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
                  <IconAvatar type={t.type === "Income" ? "income" : "expense"} />
                  <div className="dashboard-home__list-info">
                    <span className="dashboard-home__list-name">{t.name}</span>
                    <span className="dashboard-home__list-meta">
                      {t.categoryName || "Sem categoria"} · {formatDate(t.dueDate)}
                    </span>
                  </div>
                  <StatusPill status={t.status}>{STATUS_LABELS[t.status] || t.status}</StatusPill>
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
