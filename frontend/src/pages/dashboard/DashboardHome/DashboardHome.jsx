import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate } from "react-router";
import { getDashboardSummary } from "../../../api/dashboard.js";
import { getTransactionAggregates } from "../../../api/transactions.js";
import { getCategories } from "../../../api/categories.js";
import { getCreditCardsSummary, setPrimaryCard } from "../../../api/creditCards.js";
import { isAbortError } from "../../../api/client.js";
import { useAuth } from "../../../context/AuthContext.jsx";
import { useAccount } from "../../../context/AccountContext.jsx";
import Card from "../../../components/ui/Card/Card.jsx";
import Button from "../../../components/ui/Button/Button.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import StatCard from "../../../components/ui/StatCard/StatCard.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import ConfirmDialog from "../../../components/ui/ConfirmDialog/ConfirmDialog.jsx";
import AreaTrendChart from "../../../components/charts/AreaTrendChart.jsx";
import CategoryDonutChart from "../../../components/charts/CategoryDonutChart.jsx";
import AccountSwitcher from "../../../components/dashboard/AccountSwitcher/AccountSwitcher.jsx";
import CreditCardVisual from "../../../components/dashboard/CreditCardVisual/CreditCardVisual.jsx";
import CardPicker from "../../../components/dashboard/CardPicker/CardPicker.jsx";
import { cardBrand, cardPickerOption, connectorsByName } from "../../../utils/cardBrand.js";
import { groupCards } from "../../../utils/cardGroups.js";
import { groupAccounts } from "../../../utils/accountGrouping.js";
import { isCreditCardAccount } from "../../../utils/accountKind.js";
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
  if (restTotal > 0) data.push({ name: "Outros", value: restTotal, color: OTHERS_COLOR });

  return data;
}

function DashboardHomeSkeleton({ firstName }) {
  return (
    <div className="dashboard-home" aria-busy="true">
      <div className="dashboard-home__header">
        <SectionHeading title={firstName ? `Bem-vindo(a), ${firstName}` : "Bem-vindo(a)"} level={1}>
          Visão geral
        </SectionHeading>
        <div className="dashboard-home__account-controls">
          <Skeleton width="12rem" height="2.5rem" />
        </div>
      </div>

      <div className="dashboard-home__stats">
        <StatCard icon={null} label={<Skeleton width="9rem" />} value={<Skeleton width="7rem" height="1.5rem" />} />
        <StatCard icon={<TrendUpIcon />} label="Receitas do mês" value={<Skeleton width="7rem" height="1.5rem" />} />
        <StatCard icon={<TrendDownIcon />} label="Despesas do mês" value={<Skeleton width="7rem" height="1.5rem" />} />
        <StatCard
          icon={<TargetIcon />}
          label="Saldo do mês (receitas - despesas)"
          value={<Skeleton width="7rem" height="1.5rem" />}
          secondaryLines={[{ label: "% da receita gasta", value: <Skeleton width="3rem" /> }]}
        />
        <StatCard
          icon={<CardIcon />}
          label="Tipo de cartão"
          value={<Skeleton width="7rem" height="1.5rem" />}
          secondaryLines={[{ label: <Skeleton width="4rem" />, value: <Skeleton width="6rem" /> }]}
        />
      </div>

      <div className="dashboard-home__grid">
        <Card title={`Receitas x despesas (últimos ${TREND_MONTHS} meses)`}>
          <Skeleton width="100%" height="220px" />
        </Card>
        <Card title="Principais categorias (mês atual)">
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
  const {
    accounts,
    connectors,
    accountsStatus,
    selectedAccountId,
    setSelectedAccountId,
    dataRefreshKey,
    bumpDataRefresh,
    disconnectConnection,
    removeManualAccount,
  } = useAccount();
  const [categories, setCategories] = useState([]);
  const [allCards, setAllCards] = useState([]);
  const [cardSwitchError, setCardSwitchError] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    getCreditCardsSummary({ signal: controller.signal })
      .then(setAllCards)
      .catch(() => setAllCards([]));
    return () => controller.abort();
  }, [dataRefreshKey]);

  async function handlePrimaryChange(cardId) {
    setCardSwitchError("");
    try {
      await setPrimaryCard(cardId);
      bumpDataRefresh();
    } catch (err) {
      setCardSwitchError(err.message || "Não foi possível trocar o cartão.");
    }
  }
  const [result, setResult] = useState(null);
  const requestKey = `${selectedAccountId ?? ""}|${dataRefreshKey}`;
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
    if (accountsStatus !== "ready") return;

    const controller = new AbortController();
    const { signal } = controller;
    const key = `${selectedAccountId ?? ""}|${dataRefreshKey}`;

    const months = getMonthsWindow(TREND_MONTHS);
    const trendRange = monthRange(months[0], months[months.length - 1]);
    const previousRange = previousMonthSamePeriodRange();
    const loadAggregates = (bankAccountId) =>
      Promise.all([
        getTransactionAggregates({ bankAccountId, ...trendRange }, { signal }),
        getTransactionAggregates({ bankAccountId, ...previousRange }, { signal }),
      ]);

    const speculativeAggregates = selectedAccountId ? loadAggregates(selectedAccountId) : null;
    speculativeAggregates?.catch(() => {});

    getDashboardSummary(selectedAccountId, { signal })
      .then(async (summaryData) => {
        const accountId = summaryData?.activeAccount?.id;
        let aggregatesData = null;
        if (accountId) {
          aggregatesData = await (accountId === selectedAccountId ? speculativeAggregates : loadAggregates(accountId));
        }
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
  }, [selectedAccountId, dataRefreshKey, accountsStatus]);

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

  if (loading) return <DashboardHomeSkeleton firstName={user?.name?.split(" ")[0]} />;

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

  const { activeAccount, totalIncome, totalExpense, categoryExpenses, activeCard } = summary;
  const monthlyBalance = roundMoney(totalIncome - totalExpense);
  const percentageSpent = totalIncome > 0 ? (totalExpense / totalIncome) * 100 : null;
  const savingsRate =
    currentMonthTrend?.Receitas > 0
      ? ((currentMonthTrend.Receitas - currentMonthTrend.Despesas) / currentMonthTrend.Receitas) * 100
      : null;
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
  const balanceTrend =
    typeof activeAccount.balance === "number"
      ? computeTrend(activeAccount.balance, startOfMonthBalance, { label: "vs início do mês" })
      : null;

  // Mesmo agrupamento por conexão bancária do seletor de contas abaixo, aqui só pra decidir se
  // mostra o aviso abaixo do título. Sem isso, quem troca pro cartão de crédito no seletor não
  // necessariamente entende que passou a ver os dados só do cartão, separados da conta corrente
  // do mesmo banco.
  const connectorsById = new Map(connectors.map((c) => [c.id, c]));
  const activeGroup = groupAccounts(accounts, connectorsById).find((g) =>
    g.accounts.some((a) => a.id === activeAccount.id)
  );
  const isMultiAccountGroup = (activeGroup?.accounts.length ?? 0) > 1;

  // A conta selecionada agora é a mesma que o sistema reconheceu automaticamente como cartão
  // (ver CreditCardAccountDetector) — fecha o ciclo apontando de volta pra Cartões, já que
  // fatura/limite/parcelas dessa conta só aparecem naquela tela.
  const isActiveAccountLinkedCard = activeCard?.isSynced && activeCard.id === activeAccount.id;
  // O seletor também sabe (pelo tipo que a Polp manda) quando a conta ativa é um cartão, mesmo que
  // não seja o cartão mostrado em "Seu cartão". Nesse caso o "saldo" que o banco manda é do cartão,
  // não dinheiro disponível em conta, e o card de saldo precisa dizer isso.
  const connectorsMap = connectorsByName(connectors);
  const cardOptions = groupCards(allCards)
    .groups.flatMap((group) => group.cards)
    .map((card) => cardPickerOption(card, cardBrand(card, connectorsMap)));

  const isActiveAccountCard =
    isActiveAccountLinkedCard || isCreditCardAccount(accounts.find((a) => a.id === activeAccount.id));

  return (
    <div className="dashboard-home">
      <div className="dashboard-home__header">
        <SectionHeading title={firstName ? `Bem-vindo(a), ${firstName}` : "Bem-vindo(a)"} level={1}>
          Visão geral
        </SectionHeading>

        <div className="dashboard-home__account-controls">
          {isActiveAccountCard ? (
            <span className="dashboard-home__account-hint">
              Você está vendo um cartão de crédito, não uma conta bancária: veja fatura, limite e parcelas em{" "}
              <Link to="/dashboard/cartoes">Cartões</Link>.
            </span>
          ) : (
            isMultiAccountGroup && (
              <span className="dashboard-home__account-hint">
                No seletor ao lado, o cartão de crédito aparece dentro do banco, abaixo da conta corrente:
                escolha um deles pra ver só os dados dele, sem somar os dois.
              </span>
            )
          )}

          <AccountSwitcher
            accounts={accounts}
            activeAccountId={activeAccount.id}
            connectors={connectors}
            onSelect={setSelectedAccountId}
            onDisconnect={disconnectConnection}
            onRemoveManual={removeManualAccount}
            onSynced={bumpDataRefresh}
          />
        </div>
      </div>

      <div className="dashboard-home__stats">
        <StatCard
          icon={isActiveAccountCard ? <CardIcon /> : <WalletIcon />}
          label={isActiveAccountCard ? "Saldo do cartão (informado pelo banco)" : "Saldo da conta corrente"}
          value={formatCurrency(activeAccount.balance)}
          trend={isActiveAccountCard ? undefined : balanceTrend}
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
        <StatCard
          icon={<CardIcon />}
          label="Tipo de cartão"
          value={activeCard ? `Crédito${activeCard.brand ? ` · ${activeCard.brand}` : ""}` : "Nenhum cartão"}
          secondaryLines={
            activeCard
              ? [
                  { label: "Cartão", value: activeCard.name },
                  { label: "Origem", value: activeCard.isSynced ? "Sincronizado pelo banco" : "Cadastrado manualmente" },
                ]
              : [{ label: "Cadastre ou conecte em", value: "Cartões" }]
          }
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
              // Soma das próprias fatias, não totalExpense: despesas sem categoria não entram em
              // categoryExpenses, e o centro mostrava um total maior do que a rosca representava.
              centerLabel={formatCurrency(donutData.reduce((sum, d) => sum + d.value, 0))}
              centerCaption="Categorizado"
            />
          )}
        </Card>
      </div>

      <div className="dashboard-home__grid">
        {activeCard && (
          <Card
            title="Seu cartão"
            action={
              allCards.length > 1 ? (
                <CardPicker
                  id="dashboard-trocar-cartao"
                  options={cardOptions}
                  value={activeCard.id}
                  onChange={(id) => id !== activeCard.id && handlePrimaryChange(id)}
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
            <CreditCardVisual
              card={activeCard}
              cardholderName={user?.name}
              onClick={() => setCardConfirmOpen(true)}
            />
            {cardSwitchError && (
              <p className="dashboard-home__error" role="alert">
                {cardSwitchError}
              </p>
            )}
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
