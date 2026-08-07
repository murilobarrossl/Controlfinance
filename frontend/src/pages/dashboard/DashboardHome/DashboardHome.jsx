import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate } from "react-router";
import { getDashboardSummary } from "../../../api/dashboard.js";
import { getTransactions } from "../../../api/transactions.js";
import { getCategories } from "../../../api/categories.js";
import { syncAllIntegrations } from "../../../api/polp.js";
import { useAuth } from "../../../context/AuthContext.jsx";
import { useAccount } from "../../../context/AccountContext.jsx";
import Card from "../../../components/ui/Card/Card.jsx";
import Button from "../../../components/ui/Button/Button.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import StatCard from "../../../components/ui/StatCard/StatCard.jsx";
import StatusPill from "../../../components/ui/StatusPill/StatusPill.jsx";
import IconAvatar from "../../../components/ui/IconAvatar/IconAvatar.jsx";
import ConfirmDialog from "../../../components/ui/ConfirmDialog/ConfirmDialog.jsx";
import AreaTrendChart from "../../../components/charts/AreaTrendChart.jsx";
import CategoryDonutChart from "../../../components/charts/CategoryDonutChart.jsx";
import AccountSwitcher from "../../../components/dashboard/AccountSwitcher/AccountSwitcher.jsx";
import CreditCardVisual from "../../../components/dashboard/CreditCardVisual/CreditCardVisual.jsx";
import { groupAccounts } from "../../../utils/accountGrouping.js";
import { WalletIcon, TrendUpIcon, TrendDownIcon, TargetIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import { formatCurrency, formatPercentage, formatDate } from "../../../utils/financeMath.js";
import { getMonthsWindow, buildMonthlyTrend, buildPreviousMonthSamePeriod } from "../../../utils/monthlyTrend.js";
import { computeTrend } from "../../../utils/trend.js";
import "./DashboardHome.css";

const MAX_DONUT_SLICES = 4;
const TREND_MONTHS = 6;
const STATUS_LABELS = { Pending: "Pendente", Paid: "Pago", Overdue: "Atrasado" };

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
  const navigate = useNavigate();
  const { accounts, connectors, selectedAccountId, setSelectedAccountId, dataRefreshKey, bumpDataRefresh, disconnectAccount } =
    useAccount();
  const [categories, setCategories] = useState([]);
  const [summary, setSummary] = useState(null);
  const [transactions, setTransactions] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [cardConfirmOpen, setCardConfirmOpen] = useState(false);

  useEffect(() => {
    getCategories().then(setCategories).catch(() => {});

    // Sincroniza com o banco de verdade ao carregar a página (saldo e transações ficavam
    // congelados no valor de quando a conta foi conectada, sem isso). Não trava o carregamento
    // inicial esperando a Polp responder: dispara em paralelo e só atualiza o resumo (efeito
    // abaixo, via dataRefreshKey) quando a sincronização terminar.
    syncAllIntegrations()
      .then(() => bumpDataRefresh())
      .catch(() => {});
    // eslint-disable-next-line react-hooks/exhaustive-deps -- só dispara na carga inicial da página
  }, []);

  useEffect(() => {
    // Guarda contra resposta fora de ordem: se o usuário trocar de conta de novo antes dessa
    // requisição voltar, ignora o resultado, senão a tela pode acabar mostrando o resumo da
    // conta anterior por cima da conta selecionada agora.
    let cancelled = false;
    setLoading(true);

    // Busca as transações só depois do resumo responder: o gráfico de tendência precisa saber
    // qual conta ficou ativa (selectedAccountId pode vir nulo, aí o backend escolhe a conta
    // padrão) pra filtrar pela mesma conta dos cards acima. Antes buscava transações de todas
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
  }, [selectedAccountId, dataRefreshKey]);

  // Tendência mensal (últimos 6 meses) da mesma conta ativa que os cards acima.
  const monthlyTrend = useMemo(
    () => buildMonthlyTrend(transactions, getMonthsWindow(TREND_MONTHS)),
    [transactions]
  );
  const currentMonthTrend = monthlyTrend[monthlyTrend.length - 1];
  // Compara com o mesmo intervalo de dias do mês passado, não o mês passado inteiro: nos
  // primeiros dias de cada mês, comparar contra um mês inteiro sempre dá uma queda enorme e
  // artificial (ver buildPreviousMonthSamePeriod).
  const previousMonthSamePeriod = useMemo(() => buildPreviousMonthSamePeriod(transactions), [transactions]);

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

  const incomeTrend = computeTrend(currentMonthTrend?.Receitas, previousMonthSamePeriod?.Receitas, {
    label: "vs mesmo período",
  });
  const expenseTrend = computeTrend(currentMonthTrend?.Despesas, previousMonthSamePeriod?.Despesas, {
    invertTone: true,
    label: "vs mesmo período",
  });

  // Não existe um saldo histórico salvo (activeAccount.balance é sempre o saldo ao vivo da
  // Polp), então a única comparação honesta é reconstruir o saldo do início do mês a partir
  // do saldo atual menos o que já entrou/saiu neste mês, por isso o rótulo é "início do mês",
  // não "mês anterior" como nos outros cards.
  const startOfMonthBalance = activeAccount.balance - monthlyBalance;
  const balanceTrend = computeTrend(activeAccount.balance, startOfMonthBalance, { label: "vs início do mês" });

  // Mesmo agrupamento por conexão bancária do seletor de contas abaixo, aqui só pra decidir se
  // mostra o aviso abaixo do título. Sem isso, quem troca pro cartão de crédito no seletor não
  // necessariamente entende que passou a ver os dados só do cartão, separados da conta corrente
  // do mesmo banco.
  const connectorsById = new Map(connectors.map((c) => [c.id, c]));
  const activeGroup = groupAccounts(accounts, connectorsById).find((g) =>
    g.accounts.some((a) => a.id === activeAccount.id)
  );
  const isMultiAccountGroup = (activeGroup?.accounts.length ?? 0) > 1;

  // A conta selecionada agora é a mesma que o usuário vinculou a um CreditCard cadastrado
  // (ver "Sobre os dados desta página" em Cartões) — fecha o ciclo apontando de volta pra lá,
  // já que fatura/limite/parcelas dessa conta só aparecem naquela tela.
  const isActiveAccountLinkedCard = activeCard?.card?.bankAccountId === activeAccount.id;

  return (
    <div className="dashboard-home">
      <div className="dashboard-home__header">
        <SectionHeading
          kicker="Visão geral"
          title={firstName ? `Bem-vindo(a), ${firstName}` : "Bem-vindo(a)"}
          align="left"
        />

        <div className="dashboard-home__account-controls">
          {isActiveAccountLinkedCard ? (
            <span className="dashboard-home__account-hint">
              Esta conta está vinculada a um cartão cadastrado: veja fatura, limite e parcelas em{" "}
              <Link to="/dashboard/cartoes">Cartões</Link>.
            </span>
          ) : (
            isMultiAccountGroup && (
              <span className="dashboard-home__account-hint">
                A conta corrente e o cartão de crédito aparecem separados no seletor ao lado: escolha
                um deles pra ver só os dados dele, sem somar os dois.
              </span>
            )
          )}

          <AccountSwitcher
            accounts={accounts}
            activeAccountId={activeAccount.id}
            connectors={connectors}
            onSelect={setSelectedAccountId}
            onDisconnect={disconnectAccount}
            onSynced={bumpDataRefresh}
          />
        </div>
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
          <Card title="Seu cartão">
            <CreditCardVisual
              card={activeCard.card}
              cardholderName={user?.name}
              currentInvoice={activeCard.currentInvoice}
              invoiceDueDate={activeCard.invoiceDueDate}
              activeInstallmentsCount={activeCard.installments?.length ?? 0}
              linkedAccountName={activeCard.card.bankAccountName}
              onClick={() => setCardConfirmOpen(true)}
            />
          </Card>
        )}
      </div>

      <ConfirmDialog
        open={cardConfirmOpen}
        title="Ir para a área de Cartões"
        message="Você vai ser levado pra área de Cartões, onde pode ver fatura, limite, parcelamentos e vencimentos de cada cartão com mais detalhe. Continuar?"
        confirmLabel="Ir para Cartões"
        cancelLabel="Cancelar"
        onConfirm={() => {
          setCardConfirmOpen(false);
          navigate("/dashboard/cartoes");
        }}
        onClose={() => setCardConfirmOpen(false)}
      />
    </div>
  );
}
