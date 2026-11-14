import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate } from "react-router";
import { getDashboardSummary } from "../../../api/dashboard.js";
import { getTransactionAggregates } from "../../../api/transactions.js";
import { getCategories } from "../../../api/categories.js";
import { getCreditCardsSummary } from "../../../api/creditCards.js";
import { isAbortError } from "../../../api/client.js";
import { useAuth } from "../../../context/AuthContext.jsx";
import { useAccount } from "../../../context/AccountContext.jsx";
import NoBankAccount from "../../../components/dashboard/NoBankAccount/NoBankAccount.jsx";
import Card from "../../../components/ui/Card/Card.jsx";
import Button from "../../../components/ui/Button/Button.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import StatCard from "../../../components/ui/StatCard/StatCard.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import ConfirmDialog from "../../../components/ui/ConfirmDialog/ConfirmDialog.jsx";
import ManualAccountDialog from "../../../components/dashboard/ManualAccountDialog/ManualAccountDialog.jsx";
import AreaTrendChart from "../../../components/charts/AreaTrendChart.jsx";
import CategoryDonutChart from "../../../components/charts/CategoryDonutChart.jsx";
import CardPicker from "../../../components/dashboard/CardPicker/CardPicker.jsx";
import { cardBrand, cardNetwork, cardPickerOption, connectorsByName } from "../../../utils/cardBrand.js";
import { groupCards } from "../../../utils/cardGroups.js";
import { resolveCategoryColor, OTHERS_COLOR } from "../../../utils/categoryColors.js";
import { WalletIcon, TrendUpIcon, TrendDownIcon, TargetIcon, CardIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import { formatCurrency, formatPercentage, roundMoney } from "../../../utils/financeMath.js";
import {
  getMonthsWindow,
  buildMonthlyTrend,
  monthRange,
  previousMonthSamePeriodRange,
} from "../../../utils/monthlyTrend.js";
import { computeTrend } from "../../../utils/trend.js";
import CardOverview from "./CardOverview.jsx";
import "./DashboardHome.css";
import usePageTitle from "../../../hooks/usePageTitle.js";

const MAX_DONUT_SLICES = 4;
const TREND_MONTHS = 6;

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
    color: resolveCategoryColor(c.categoryName, categories),
  }));
  if (restTotal > 0) data.push({ name: "Outros", value: restTotal, color: OTHERS_COLOR, isRest: true });

  return data;
}

function DashboardHomeSkeleton({ firstName }) {
  return (
    <div className="dashboard-home" aria-busy="true">
      <div className="dashboard-home__header">
        <SectionHeading title={firstName ? `Bem-vindo(a), ${firstName}` : "Bem-vindo(a)"} level={1} />
      </div>

      <div className="dashboard-home__stats">
        <StatCard icon={null} label={<Skeleton width="9rem" />} value={<Skeleton width="7rem" height="1.5rem" />} />
        <StatCard icon={<TrendUpIcon />} label="Entradas do mês" value={<Skeleton width="7rem" height="1.5rem" />} />
        <StatCard icon={<TrendDownIcon />} label="Saídas do mês" value={<Skeleton width="7rem" height="1.5rem" />} />
        <StatCard
          icon={<TargetIcon />}
          label="Resultado do mês"
          value={<Skeleton width="7rem" height="1.5rem" />}
          secondaryLines={[{ label: "% da receita gasta", value: <Skeleton width="3rem" /> }]}
        />
        <StatCard
          icon={<CardIcon />}
          label="Cartão"
          value={<Skeleton width="7rem" height="1.5rem" />}
          secondaryLines={[{ label: <Skeleton width="4rem" />, value: <Skeleton width="6rem" /> }]}
        />
      </div>

      <div className="dashboard-home__grid">
        <Card
          title={
            <>
              Receitas x despesas{" "}
              <span className="dashboard-home__title-note">(últimos {TREND_MONTHS} meses)</span>
            </>
          }
        >
          <Skeleton width="100%" height="220px" />
        </Card>
        <Card
          title={
            <>
              Principais categorias <span className="dashboard-home__title-note">(mês atual)</span>
            </>
          }
        >
          <Skeleton width="100%" height="220px" />
        </Card>
      </div>
    </div>
  );
}

export default function DashboardHome() {
  usePageTitle("Dashboard inteligente");
  const { user } = useAuth();
  const navigate = useNavigate();
  const { connectors, accountsStatus, effectiveScope, scopeKey, scopeQuery, dataRefreshKey, bumpDataRefresh } = useAccount();
  const [categories, setCategories] = useState([]);
  const [manualAccountOpen, setManualAccountOpen] = useState(false);
  const [allCards, setAllCards] = useState([]);
  const [shownCardId, setShownCardId] = useState(null);

  useEffect(() => {
    const controller = new AbortController();
    getCreditCardsSummary({ signal: controller.signal })
      .then(setAllCards)
      .catch(() => setAllCards([]));
    return () => controller.abort();
  }, [dataRefreshKey]);

  const [result, setResult] = useState(null);
  const requestKey = `${scopeKey}|${dataRefreshKey}`;
  const loading = accountsStatus !== "ready" || result?.key !== requestKey;
  const [cardConfirmOpen, setCardConfirmOpen] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    getCategories({ signal: controller.signal })
      .then(setCategories)
      .catch(() => {});
    return () => controller.abort();
  }, []);

  useEffect(() => {
    if (accountsStatus !== "ready" || !scopeKey) return;

    const controller = new AbortController();
    const { signal } = controller;
    const key = `${scopeKey}|${dataRefreshKey}`;

    const months = getMonthsWindow(TREND_MONTHS);
    const trendRange = monthRange(months[0], months[months.length - 1]);
    const previousRange = previousMonthSamePeriodRange();
    const aggregates = Promise.all([
      getTransactionAggregates({ ...scopeQuery, ...trendRange }, { signal }),
      getTransactionAggregates({ ...scopeQuery, ...previousRange }, { signal }),
    ]);
    aggregates.catch(() => {});

    getDashboardSummary(scopeQuery.bankAccountId, { signal })
      .then(async (summaryData) => {
        const aggregatesData = summaryData?.account ? await aggregates : null;
        if (signal.aborted) return;
        setResult({
          key,
          summary: summaryData,
          trend: aggregatesData?.[0] ?? null,
          previous: aggregatesData?.[1] ?? null,
          error: "",
        });
      })
      .catch((err) => {
        if (signal.aborted || isAbortError(err)) return;
        setResult({ key, summary: null, trend: null, previous: null, error: err.message || "Não foi possível carregar o dashboard." });
      });

    return () => controller.abort();
  }, [scopeKey, scopeQuery, dataRefreshKey, accountsStatus]);

  const summary = result?.summary ?? null;
  const trend = result?.trend ?? null;
  const previous = result?.previous ?? null;
  const error =
    accountsStatus === "error"
      ? "Não foi possível carregar as contas."
      : result?.key === requestKey
        ? result.error
        : "";

  // Tendência mensal (últimos 6 meses) da mesma conta ativa que os cards acima.
  const monthlyTrend = useMemo(() => buildMonthlyTrend(trend, getMonthsWindow(TREND_MONTHS)), [trend]);
  const currentMonthTrend = monthlyTrend[monthlyTrend.length - 1];
  const previousMonthSamePeriod = useMemo(
    () => (previous ? { Receitas: previous.totalIncome, Despesas: previous.totalExpense } : null),
    [previous]
  );

  if (error) return <p className="dashboard-home__error">{error}</p>;

  if (accountsStatus === "ready" && !scopeKey) return <NoBankAccount />;

  if (loading) return <DashboardHomeSkeleton firstName={user?.name?.split(" ")[0]} />;

  if (!summary?.account) {
    return (
      <>
        <Card className="dashboard-home__empty">
          <p>{summary?.message || "Nenhum banco conectado ainda."}</p>
          <p className="dashboard-home__empty-hint">
            Conectar um banco traz saldo e extrato sozinho. Prefere não conectar? Cadastre uma conta e lance as
            entradas e saídas à mão.
          </p>
          <div className="dashboard-home__empty-actions">
            <Button as={Link} to="/conectar-banco" variant="primary" size="md">
              Conectar banco
            </Button>
            <Button as="button" type="button" variant="outline" size="md" onClick={() => setManualAccountOpen(true)}>
              Cadastrar conta manual
            </Button>
          </div>
        </Card>

        <ManualAccountDialog
          open={manualAccountOpen}
          onClose={() => setManualAccountOpen(false)}
          onCreated={bumpDataRefresh}
        />
      </>
    );
  }

  const { account, totalIncome, totalExpense, categoryExpenses } = summary;
  const activeCard = allCards.find((c) => c.id === shownCardId) ?? summary.activeCard;
  const monthlyBalance = roundMoney(totalIncome - totalExpense);
  const percentageSpent = totalIncome > 0 ? (totalExpense / totalIncome) * 100 : null;
  const savingsRate =
    currentMonthTrend?.Receitas > 0
      ? ((currentMonthTrend.Receitas - currentMonthTrend.Despesas) / currentMonthTrend.Receitas) * 100
      : null;
  const donutData = buildDonutData(categoryExpenses, categories);
  const openCategory = (name, entry) => {
    const params = new URLSearchParams({ periodo: "mes" });
    if (!entry?.isRest) params.set("categoria", name);
    navigate(`/dashboard/categorias?${params}`);
  };
  const firstName = user?.name?.split(" ")[0];

  const incomeTrend = computeTrend(currentMonthTrend?.Receitas, previousMonthSamePeriod?.Receitas, {
    label: "vs mesmo período",
  });
  const expenseTrend = computeTrend(currentMonthTrend?.Despesas, previousMonthSamePeriod?.Despesas, {
    invertTone: true,
    label: "vs mesmo período",
  });

  const isCardView = account.kind === "CreditCard";

  // Não existe saldo histórico salvo, então o saldo do início do mês é reconstruído a partir do
  // atual menos o resultado do mês. Poupança não entra no saldo principal, então fica sem tendência.
  const balanceTrend =
    !isCardView && !account.balanceIncomplete && typeof account.balance === "number" && account.kind !== "Savings"
      ? computeTrend(account.balance, account.balance - monthlyBalance, { label: "vs início do mês" })
      : null;

  const connectorsMap = connectorsByName(connectors);
  const cardOptions = groupCards(allCards)
    .groups.flatMap((group) => group.cards)
    .map((card) => cardPickerOption(card, cardBrand(card, connectorsMap)));

  return (
    <div className="dashboard-home">
      <div className="dashboard-home__header">
        <SectionHeading title={firstName ? `Bem-vindo(a), ${firstName}` : "Bem-vindo(a)"} level={1} />

        {isCardView && (
          <div className="dashboard-home__account-controls">
            <span className="dashboard-home__account-hint">
              Você está vendo um cartão de crédito, não uma conta bancária: veja fatura, limite e parcelas em{" "}
              <Link to="/dashboard/cartoes">Cartões</Link>.
            </span>
          </div>
        )}
        {effectiveScope?.enrichmentPending && (
          <div className="dashboard-home__account-controls">
            <span className="dashboard-home__account-hint">
              O banco ainda está classificando parte das transações. Até terminar, uma transferência entre suas
              contas pode aparecer como entrada e saída.
            </span>
          </div>
        )}
      </div>

      <div className="dashboard-home__stats">
        <StatCard
          icon={isCardView ? <CardIcon /> : <WalletIcon />}
          label={isCardView ? "Saldo do cartão (informado pelo banco)" : "Saldo em conta"}
          value={formatCurrency(account.balance)}
          trend={balanceTrend ?? undefined}
          secondaryLines={account.balanceIncomplete ? [{ label: "Saldo", value: "Aguardando sincronização" }] : []}
        />
        <StatCard
          icon={<TrendUpIcon />}
          label="Entradas do mês"
          value={formatCurrency(totalIncome)}
          valueTone="income"
          trend={incomeTrend}
        />
        <StatCard
          icon={<TrendDownIcon />}
          label="Saídas do mês"
          value={formatCurrency(totalExpense)}
          valueTone="expense"
          trend={expenseTrend}
        />
        <StatCard
          icon={<TargetIcon />}
          label="Resultado do mês"
          value={formatCurrency(monthlyBalance)}
          valueTone={monthlyBalance >= 0 ? "income" : "expense"}
          secondaryLines={[{ label: "% da receita gasta", value: formatPercentage(percentageSpent) }]}
        />
        <StatCard
          icon={<CardIcon />}
          label="Cartão"
          value={activeCard ? ["Crédito", cardNetwork(activeCard.brand)?.label].filter(Boolean).join(" · ") : "Nenhum cartão"}
          secondaryLines={
            activeCard
              ? [{ label: "Cartão", value: activeCard.name }]
              : [{ label: "Cadastre ou conecte em", value: "Cartões" }]
          }
        />
      </div>

      <div className="dashboard-home__grid">
        <Card
          title={
            <>
              Receitas x despesas{" "}
              <span className="dashboard-home__title-note">(últimos {TREND_MONTHS} meses)</span>
            </>
          }
        >
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

        <Card
          title={
            <>
              Principais categorias <span className="dashboard-home__title-note">(mês atual)</span>
            </>
          }
        >
          {donutData.length === 0 ? (
            <p className="dashboard-home__hint">Nenhuma despesa categorizada este mês.</p>
          ) : (
            <CategoryDonutChart
              height={220}
              formatValue={formatCurrency}
              data={donutData}
              // Soma das próprias fatias, não totalExpense: despesas sem categoria não entram em
              // categoryExpenses, e o centro mostrava um total maior do que a rosca representava.
              centerLabel={formatCurrency(donutData.reduce((sum, d) => sum + d.value, 0))}
              centerCaption={null}
              onSliceClick={openCategory}
            />
          )}
        </Card>
      </div>

      {activeCard && (
        <Card
          className="dashboard-home__card-block"
          title="Seu cartão"
          action={
            allCards.length > 1 ? (
              <CardPicker
                id="dashboard-trocar-cartao"
                options={cardOptions}
                value={activeCard.id}
                onChange={setShownCardId}
                triggerLabel="Trocar"
                ariaLabel="Trocar o cartão mostrado aqui"
              />
            ) : (
              <Link to="/dashboard/cartoes" className="dashboard-home__card-switch">
                Trocar
              </Link>
            )
          }
        >
          <CardOverview
            card={activeCard}
            cardholderName={user?.name}
            onOpenCards={() => setCardConfirmOpen(true)}
            refreshKey={dataRefreshKey}
          />
        </Card>
      )}

      <ConfirmDialog
        open={cardConfirmOpen}
        title="Ir para a área de Cartões"
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
