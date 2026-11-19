import { useEffect, useState } from "react";
import { Link } from "react-router";
import { getUpcomingRecurring } from "../../../api/recurringExpenses.js";
import { getInvestmentDashboard } from "../../../api/investments.js";
import { getRecentTransactions } from "../../../api/transactions.js";
import { useAccount } from "../../../context/AccountContext.jsx";
import DashboardPanel from "../../../components/dashboard/DashboardPanel/DashboardPanel.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import MerchantLogo from "../../../components/ui/MerchantLogo/MerchantLogo.jsx";
import RecurringLogo from "../empresa/RecurringLogo.jsx";
import { activityLabel } from "../empresa/recurringExpenseLabels.js";
import { formatCurrency, formatDate } from "../../../utils/financeMath.js";
import { transactionTone } from "../../../utils/financialTone.js";
import InvestmentSummary from "./InvestmentSummary.jsx";
import "../empresa/RecurringExpenses.css";
import "./DashboardSummaries.css";

const recent = (bankAccountId, options) => getRecentTransactions({ bankAccountId, limit: 5 }, options);

function Summary({ title, link, action, load, scope, refresh, children }) {
  const [result, setResult] = useState(null);
  const key = JSON.stringify([scope, refresh]);
  useEffect(() => {
    const controller = new AbortController();
    const { signal } = controller;
    load(scope, { signal }).then((data) => {
      if (!signal.aborted) setResult({ key, data });
    }).catch((error) => {
      if (!signal.aborted) setResult({ key, error: error.message || "Não foi possível carregar o resumo." });
    });
    return () => controller.abort();
  }, [key, scope, load]);
  return <DashboardPanel title={title} action={link && <Link className="dh-mini__link" to={link}>{action}</Link>}>
    {result?.key !== key ? <Skeleton width="100%" height="7rem" />
      : result.error ? <p className="dashboard-home__error" role="alert">{result.error}</p> : children(result.data)}
  </DashboardPanel>;
}

export default function DashboardSummaries({ personal }) {
  const { scopeQuery, effectiveConnectionId, dataRefreshKey } = useAccount();
  return <>
    <div className={personal ? "dashboard-home__grid" : undefined}>
      <Summary title="Despesas recorrentes" link="/dashboard/despesas-recorrentes" action="Ver todas"
        load={getUpcomingRecurring} scope={scopeQuery.bankAccountId} refresh={dataRefreshKey}>
        {(items) => !items.length ? <p className="dashboard-home__hint">Nenhuma próxima cobrança estimada.</p> :
          <ul className="dh-mini__list">{items.map((item) => <li key={item.id} className="dh-mini__row">
            <RecurringLogo name={item.name} />
            <div><strong>{item.name}</strong><small>Estimada para {formatDate(item.nextEstimatedDate)} · {activityLabel(item.activity)}</small></div>
            <div className="dh-mini__amount">{formatCurrency(item.lastAmount)}<small>Última cobrança</small></div>
          </li>)}</ul>}
      </Summary>
      {personal && <Summary title="Investimentos" link="/dashboard/investimentos" action="Ver investimentos"
        load={getInvestmentDashboard} scope={effectiveConnectionId} refresh={dataRefreshKey}>
        {(data) => <InvestmentSummary data={data} />}
      </Summary>}
    </div>
    <Summary title="Relatórios"
      load={recent} scope={scopeQuery.bankAccountId} refresh={dataRefreshKey}>
      {(items) => !items.length ? <p className="dashboard-home__hint">Nenhum lançamento nesta seleção.</p> :
        <ul className="dh-mini__list">{items.map((t) => <li key={t.id}>
          <Link className="dh-mini__row" to={`/dashboard/relatorios/${t.id}`} state={{ fromDashboard: true }}>
            <MerchantLogo name={t.name} isTransfer={t.isTransfer} />
            <div><strong>{t.name.replaceAll("|", " · ")}</strong><small>{formatDate(t.dueDate)} · {t.categoryName || "Sem categoria"}</small></div>
            <span className={`dh-mini__amount card-overview__amount--${transactionTone(t.type, t.amount, t.isTransfer)}`}>{t.type === "Income" ? "+" : "−"} {formatCurrency(t.amount)}</span>
          </Link>
        </li>)}</ul>}
    </Summary>
  </>;
}
