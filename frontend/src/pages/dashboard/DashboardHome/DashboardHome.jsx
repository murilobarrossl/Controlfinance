import { financialTone, transactionTone } from "../../../utils/financialTone.js";
import { CHART_COLORS } from "../../../components/charts/chartTheme.js";
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
import Button from "../../../components/ui/Button/Button.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import ConfirmDialog from "../../../components/ui/ConfirmDialog/ConfirmDialog.jsx";
import AreaTrendChart from "../../../components/charts/AreaTrendChart.jsx";
import CategoryDonutChart from "../../../components/charts/CategoryDonutChart.jsx";
import { NEUTRAL_DONUT_SHADES } from "../../../components/charts/chartTheme.js";
import CardPicker from "../../../components/dashboard/CardPicker/CardPicker.jsx";
import { cardBrand, cardPickerOption, connectorsByName } from "../../../utils/cardBrand.js";
import { groupCards } from "../../../utils/cardGroups.js";
import { resolveCategoryColor, OTHERS_COLOR } from "../../../utils/categoryColors.js";
import { formatCurrency, formatPercentage, roundMoney } from "../../../utils/financeMath.js";
import {
  getMonthsWindow,
  buildMonthlyTrend,
  monthRange,
} from "../../../utils/monthlyTrend.js";
import { compareMonth } from "../../../utils/periods.js";
import { computeTrend } from "../../../utils/trend.js";
import DashboardSummaries from "./DashboardSummaries.jsx";
import CardOverview from "./CardOverview.jsx";
import DashboardSection from "../../../components/dashboard/DashboardPanel/DashboardPanel.jsx";
import DashboardMetric from "../../../components/dashboard/DashboardMetric/DashboardMetric.jsx";
import { isBusinessHolder } from "../../../utils/holder.js";
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

  // O sync cria uma categoria real chamada "Outros": quando ela está entre as maiores, a fatia do resto
  // muda de nome para não haver dois itens iguais, e a chave distingue os dois de qualquer forma.
  const data = top.map((c) => ({
    key: `categoria:${c.categoryName}`,
    name: c.categoryName,
    value: c.amount,
    color: resolveCategoryColor(c.categoryName, categories),
  }));
  if (restTotal > 0) {
    const restName = data.some((d) => d.name === "Outros") ? "Demais categorias" : "Outros";
    data.push({ key: "resto", name: restName, value: restTotal, color: OTHERS_COLOR, isRest: true });
  }

  return data;
}

function welcomeTitle(name) {
  const firstName = name?.split(" ")[0];
  return firstName ? `Bem-vindo(a), ${firstName}` : "Bem-vindo(a)";
}

function DashboardHomeSkeleton({ title }) {
  return (
    <div className="dashboard-home" aria-busy="true">
      <div className="dashboard-home__header">
        <SectionHeading title={title} level={1} />
      </div>

      <div className="dashboard-home__stats summary-dividers summary-dividers--four">
        <DashboardMetric label={<Skeleton width="9rem" />} value={<Skeleton width="7rem" height="1.5rem" />} />
        <DashboardMetric label="Entradas do mês" value={<Skeleton width="7rem" height="1.5rem" />} />
        <DashboardMetric label="Saídas do mês" value={<Skeleton width="7rem" height="1.5rem" />} />
        <DashboardMetric
          label="Resultado do mês"
          value={<Skeleton width="7rem" height="1.5rem" />}
          secondaryLines={[{ label: "% da receita gasta", value: <Skeleton width="3rem" /> }]}
        />
      </div>

      <div className="dashboard-home__grid">
        <DashboardSection
          title={
            <>
              Receitas x despesas{" "}
              <span className="dashboard-home__title-note">(últimos {TREND_MONTHS} meses)</span>
            </>
          }
        >
          <Skeleton width="100%" height="220px" />
        </DashboardSection>
        <DashboardSection
          title={
            <>
              Principais categorias <span className="dashboard-home__title-note">(mês atual)</span>
            </>
          }
        >
          <Skeleton width="100%" height="220px" />
        </DashboardSection>
      </div>
    </div>
  );
}

// heading: título no lugar da saudação (o dashboard empresarial usa o nome da empresa).
export default function DashboardHome({ heading } = {}) {
  usePageTitle("Dashboard inteligente");
  const { user } = useAuth();
  const navigate = useNavigate();
  const { connectors, accountsStatus, effectiveScope, scopeKey, scopeQuery, dataRefreshKey } = useAccount();
  const [categories, setCategories] = useState([]);
  const [cardResult, setCardResult] = useState(null);
  const cardsKey = JSON.stringify([user?.id, dataRefreshKey]);
  const [shownCardId, setShownCardId] = useState(null);

  useEffect(() => {
    const controller = new AbortController();
    getCreditCardsSummary({ signal: controller.signal })
      .then((cards) => {
        if (!controller.signal.aborted) setCardResult({ key: cardsKey, cards });
      })
      .catch(() => {
        if (!controller.signal.aborted) setCardResult({ key: cardsKey, cards: null });
      });
    return () => controller.abort();
  }, [cardsKey]);

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
    // Mesma regra de Receitas e despesas: mês atual até hoje × mesmo período do mês anterior.
    const comparison = compareMonth(months[months.length - 1]);
    const aggregates = Promise.all([
      getTransactionAggregates({ ...scopeQuery, ...trendRange }, { signal }),
      getTransactionAggregates({ ...scopeQuery, ...comparison.range }, { signal }),
      getTransactionAggregates({ ...scopeQuery, ...comparison.currentRange }, { signal }),
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
          currentToDate: aggregatesData?.[2] ?? null,
          error: "",
        });
      })
      .catch((err) => {
        if (signal.aborted || isAbortError(err)) return;
        setResult({ key, summary: null, trend: null, previous: null, currentToDate: null, error: err.message || "Não foi possível carregar o dashboard." });
      });

    return () => controller.abort();
  }, [scopeKey, scopeQuery, dataRefreshKey, accountsStatus]);

  const summary = result?.summary ?? null;
  const trend = result?.trend ?? null;
  const previous = result?.previous ?? null;
  const currentToDate = result?.currentToDate ?? null;
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

  const title = heading ?? welcomeTitle(user?.name);
  if (loading) return <DashboardHomeSkeleton title={title} />;

  if (!summary?.account) {
    return (
      <DashboardSection className="dashboard-home__empty">
        <p>{summary?.message || "Nenhum banco conectado ainda."}</p>
        <p className="dashboard-home__empty-hint">Conecte um banco e o saldo e o extrato chegam sozinhos.</p>
        <div className="dashboard-home__empty-actions">
          <Button as={Link} to="/conectar-banco" variant="primary" size="md">
            Conectar banco
          </Button>
        </div>
      </DashboardSection>
    );
  }

  const { account, totalIncome, totalExpense, categoryExpenses } = summary;
  // O Dashboard usa o escopo da conta do cabeçalho. Conta sem conexão
  // (por exemplo, manual) não representa a opção de todos os bancos.
  const allCards = cardResult?.key === cardsKey
    ? cardResult.cards ?? (summary.activeCard ? [summary.activeCard] : [])
    : [];
  const availableCards = scopeQuery.bankAccountId
    ? allCards.filter((card) => effectiveScope?.connectionId && card.connectionId === effectiveScope.connectionId)
    : allCards;
  const activeCard = availableCards.find((card) => card.id === shownCardId)
    ?? availableCards.find((card) => card.id === summary.activeCard?.id)
    ?? availableCards[0];
  const monthlyBalance = roundMoney(totalIncome - totalExpense);
  const percentageSpent = totalIncome > 0 ? (totalExpense / totalIncome) * 100 : null;
  const savingsRate =
    currentMonthTrend?.Receitas > 0
      ? ((currentMonthTrend.Receitas - currentMonthTrend.Despesas) / currentMonthTrend.Receitas) * 100
      : null;
  const donutData = buildDonutData(categoryExpenses, categories);
  // Categorias não existe no ambiente empresarial: lá a rosca fica só como leitura, sem link.
  const openCategory = isBusinessHolder(user) ? undefined : (name, entry) => {
    const params = new URLSearchParams({ periodo: "mes" });
    if (!entry?.isRest) params.set("categoria", name);
    navigate(`/dashboard/categorias?${params}`);
  };

  const incomeTrend = computeTrend(currentToDate?.totalIncome, previousMonthSamePeriod?.Receitas, {
    label: "vs mesmo período",
  });
  const expenseTrend = computeTrend(currentToDate?.totalExpense, previousMonthSamePeriod?.Despesas, {
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
  const cardOptions = groupCards(availableCards)
    .groups.flatMap((group) => group.cards)
    .map((card) => cardPickerOption(card, cardBrand(card, connectorsMap)));

  return (
    <div className="dashboard-home">
      <div className="dashboard-home__header">
        <SectionHeading title={title} level={1} />

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

      <div className="dashboard-home__stats summary-dividers summary-dividers--four">
        <DashboardMetric
          label={isCardView ? "Saldo do cartão (informado pelo banco)" : "Saldo em conta"}
          value={formatCurrency(account.balance)}
          valueTone={isCardView ? "default" : financialTone(account.balance)}
          trend={balanceTrend ?? undefined}
          secondaryLines={account.balanceIncomplete ? [{ label: "Saldo", value: "Aguardando sincronização" }] : []}
        />
        <DashboardMetric
          label="Entradas do mês"
          value={formatCurrency(totalIncome)}
          valueTone={financialTone(totalIncome)}
          trend={incomeTrend}
        />
        <DashboardMetric
          label="Saídas do mês"
          value={formatCurrency(totalExpense)}
          valueTone={transactionTone("Expense", totalExpense)}
          trend={expenseTrend}
        />
        <DashboardMetric
          label="Resultado do mês"
          value={formatCurrency(monthlyBalance)}
          valueTone={financialTone(monthlyBalance)}
          secondaryLines={[{ label: "% da receita gasta", value: formatPercentage(percentageSpent), tone: percentageSpent > 100 ? "negative" : "default" }]}
        />
      </div>

      <div className="dashboard-home__grid">
        <DashboardSection
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
              { key: "Receitas", color: CHART_COLORS.income },
              { key: "Despesas", color: CHART_COLORS.expense, strokeDasharray: "6 4" },
            ]}
            data={monthlyTrend}
            badge={{ value: formatPercentage(savingsRate), label: "Taxa de poupança", tone: financialTone(savingsRate) }}
          />
        </DashboardSection>

        <DashboardSection
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
              palette={NEUTRAL_DONUT_SHADES}
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
        </DashboardSection>
      </div>

      {activeCard && (
        <DashboardSection
          className="dashboard-home__card-block"
          title="Seu cartão"
          action={
            availableCards.length > 1 ? (
              <CardPicker
                id="dashboard-trocar-cartao"
                options={cardOptions}
                value={activeCard.id}
                onChange={setShownCardId}
                triggerLabel="Trocar"
                ariaLabel="Trocar o cartão mostrado aqui"
              />
            ) : null
          }
        >
          <CardOverview
            card={activeCard}
            cardholderName={user?.name}
            onOpenCards={() => setCardConfirmOpen(true)}
            refreshKey={dataRefreshKey}
          />
        </DashboardSection>
      )}

      <DashboardSummaries key={user?.id} personal={!isBusinessHolder(user)} />

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
