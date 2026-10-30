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
import ConfirmDialog from "../../../components/ui/ConfirmDialog/ConfirmDialog.jsx";
import AreaTrendChart from "../../../components/charts/AreaTrendChart.jsx";
import CategoryDonutChart from "../../../components/charts/CategoryDonutChart.jsx";
import AccountSwitcher from "../../../components/dashboard/AccountSwitcher/AccountSwitcher.jsx";
import CreditCardVisual from "../../../components/dashboard/CreditCardVisual/CreditCardVisual.jsx";
import { groupAccounts } from "../../../utils/accountGrouping.js";
import { isCreditCardAccount } from "../../../utils/accountKind.js";
import { resolveCategoryColor, OTHERS_COLOR } from "../../../utils/categoryColors.js";
import { WalletIcon, TrendUpIcon, TrendDownIcon, TargetIcon, CardIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import { formatCurrency, formatPercentage } from "../../../utils/financeMath.js";
import { getMonthsWindow, buildMonthlyTrend, buildPreviousMonthSamePeriod } from "../../../utils/monthlyTrend.js";
import { computeTrend } from "../../../utils/trend.js";
import "./DashboardHome.css";

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

export default function DashboardHome() {
  const { user } = useAuth();
  const navigate = useNavigate();
  const {
    accounts,
    connectors,
    selectedAccountId,
    setSelectedAccountId,
    dataRefreshKey,
    bumpDataRefresh,
    disconnectConnection,
    removeManualAccount,
  } = useAccount();
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

  const { activeAccount, totalIncome, totalExpense, categoryExpenses, activeCard } = summary;
  const monthlyBalance = totalIncome - totalExpense;
  const percentageSpent = totalIncome > 0 ? (totalExpense / totalIncome) * 100 : 0;
  const savingsRate =
    currentMonthTrend?.Receitas > 0
      ? ((currentMonthTrend.Receitas - currentMonthTrend.Despesas) / currentMonthTrend.Receitas) * 100
      : 0;
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

  // A conta selecionada agora é a mesma que o sistema reconheceu automaticamente como cartão
  // (ver CreditCardAccountDetector) — fecha o ciclo apontando de volta pra Cartões, já que
  // fatura/limite/parcelas dessa conta só aparecem naquela tela.
  const isActiveAccountLinkedCard = activeCard?.isSynced && activeCard.id === activeAccount.id;
  // O seletor também sabe (pelo tipo que a Polp manda) quando a conta ativa é um cartão, mesmo que
  // não seja o cartão mostrado em "Seu cartão". Nesse caso o "saldo" que o banco manda é do cartão,
  // não dinheiro disponível em conta, e o card de saldo precisa dizer isso.
  const isActiveAccountCard =
    isActiveAccountLinkedCard || isCreditCardAccount(accounts.find((a) => a.id === activeAccount.id));

  return (
    <div className="dashboard-home">
      <div className="dashboard-home__header">
        <SectionHeading
          kicker="Visão geral"
          title={firstName ? `Bem-vindo(a), ${firstName}` : "Bem-vindo(a)"}
          align="left"
        />

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

      <Card className="dashboard-home__explainer" title="Conta corrente x cartão de crédito">
        <div className="dashboard-home__explainer-grid">
          <div className="dashboard-home__explainer-item">
            <span className="dashboard-home__explainer-icon dashboard-home__explainer-icon--account">
              <WalletIcon />
            </span>
            <div>
              <strong>Conta corrente</strong>
              <p>
                É o dinheiro que você tem hoje no banco. O saldo sobe com o que entra (salário, Pix recebido) e desce
                com o que sai (pagamentos, transferências, débito).
              </p>
            </div>
          </div>
          <div className="dashboard-home__explainer-item">
            <span className="dashboard-home__explainer-icon dashboard-home__explainer-icon--card">
              <CardIcon />
            </span>
            <div>
              <strong>Cartão de crédito</strong>
              <p>
                São compras feitas com crédito do banco que você paga depois, na fatura. O valor do cartão não é dinheiro
                disponível e nunca é somado ao saldo da conta corrente.
              </p>
            </div>
          </div>
        </div>

        <div className="dashboard-home__cards-callout">
          <span className="dashboard-home__cards-callout-icon" aria-hidden="true">
            <CardIcon />
          </span>
          <p>
            Seus cartões têm uma aba própria: fatura, limite, <strong>próximos vencimentos</strong> e parcelamentos ficam
            em <strong>Cartões</strong>.
          </p>
          <Button as={Link} to="/dashboard/cartoes" variant="outline" size="sm">
            Abrir aba Cartões
          </Button>
        </div>
      </Card>

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
          <Card title="Seu cartão">
            <CreditCardVisual
              card={activeCard}
              cardholderName={user?.name}
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
