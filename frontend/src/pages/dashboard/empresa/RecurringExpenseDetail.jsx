import { useEffect, useState } from "react";
import { Link, useParams, useSearchParams } from "react-router";
import { getRecurringExpense } from "../../../api/recurringExpenses.js";
import { isAbortError } from "../../../api/client.js";
import { useAccount } from "../../../context/AccountContext.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import Pager from "../../../components/ui/Pager/Pager.jsx";
import RecurringLogo from "./RecurringLogo.jsx";
import { Field, Section } from "./RecordDetailParts.jsx";
import { formatCurrency } from "../../../utils/financeMath.js";
import { formatDateOnly } from "../../../utils/employees.js";
import { formatDateTime } from "../ReceitasDespesas/cashFormat.js";
import { activityLabel, frequencyLabel, paymentMethodLabel } from "./recurringExpenseLabels.js";
import usePageTitle from "../../../hooks/usePageTitle.js";
import "./BusinessModule.css";
import "./RegistryList.css";
import "./RecordDetail.css";
import "./RecurringExpenses.css";

const PAGE_SIZE = 25;

export default function RecurringExpenseDetail() {
  const { recurringId } = useParams();
  const [params] = useSearchParams();
  const { dataRefreshKey, connections } = useAccount();
  const [result, setResult] = useState(null);
  const [retry, setRetry] = useState(0);
  const [page, setPage] = useState(1);
  const syncKey = connections.map((c) => `${c.id}:${c.lastSyncedAt ?? ""}:${c.status}`).sort().join("|");
  const key = `${recurringId}|${dataRefreshKey}|${syncKey}|${retry}`;
  const ready = result?.key === key ? result : null;
  const data = ready?.data;
  const expense = data?.expense;
  usePageTitle(expense?.name ?? "Despesa recorrente");

  useEffect(() => {
    const controller = new AbortController();
    getRecurringExpense(recurringId, { signal: controller.signal })
      .then((value) => setResult({ key, data: value, error: "" }))
      .catch((err) => {
        if (controller.signal.aborted || isAbortError(err)) return;
        setResult({ key, data: null, error: err.status === 404 ? "Despesa recorrente não encontrada." : err.message || "Não foi possível carregar a despesa recorrente." });
      });
    return () => controller.abort();
  }, [key, recurringId]);

  const back = <Link to={`/dashboard/despesas-recorrentes${params.get("mes") ? `?mes=${encodeURIComponent(params.get("mes"))}` : ""}`} className="record-detail__back">‹ Despesas recorrentes</Link>;
  if (ready?.error) return <div className="record-detail">{back}<p className="record-detail__error" role="alert">{ready.error}</p><button className="recurring-expenses__retry" type="button" onClick={() => setRetry((v) => v + 1)}>Tentar novamente</button></div>;
  if (!expense) return <div className="record-detail" aria-busy="true">{back}<Skeleton width="16rem" height="1.8rem" /><Skeleton width="100%" height="8rem" /></div>;
  const totalPages = Math.max(1, Math.ceil(data.charges.length / PAGE_SIZE));
  const currentPage = Math.min(page, totalPages);
  const charges = data.charges.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE);

  return <div className="record-detail recurring-expenses">
    {back}
    <header className="record-detail__header recurring-expenses__header"><div className="record-detail__identity">
      <div className="record-detail__name-row"><h1>{expense.name}</h1></div>
    </div><span className="recurring-expenses__logo recurring-expenses__logo--header"><RecurringLogo name={expense.name} /></span></header>
    <Section id="recurring-identity" title="Detalhes">
      <dl className="record-detail__grid">
        <Field label="Instituição">{expense.institutionName || "Não informada"}</Field>
        <Field label="Meio de pagamento">{paymentMethodLabel(expense)}</Field>
        <Field label="Conta ou cartão">{[expense.accountName, expense.accountNumber].filter(Boolean).join(" · ")}</Field>
        <Field label="Frequência">{frequencyLabel(expense.periodMonths)}</Field>
        <Field label="Última cobrança"><span className="recurring-expenses__amount">{formatCurrency(expense.lastAmount)}</span> · {formatDateOnly(expense.lastChargeDate)}</Field>
        <Field label="Próxima cobrança estimada" muted={!expense.nextEstimatedDate}>{expense.nextEstimatedDate ? formatDateOnly(expense.nextEstimatedDate) : "Sem previsão disponível"}</Field>
        <Field label="Histórico recente">{activityLabel(expense.activity)}</Field>
        <Field label="Dados atualizados em" muted={!expense.dataUpdatedAt}>{expense.dataUpdatedAt ? formatDateTime(expense.dataUpdatedAt) : "Atualização não informada"}</Field>
      </dl>
    </Section>
    <Section id="recurring-history" title="Histórico de cobranças">
      <div className="registry registry__table-wrap"><table className="biz-table registry__table recurring-expenses__history">
        <thead><tr><th scope="col">Data da cobrança</th><th scope="col">Meio de pagamento</th><th scope="col" className="biz-table__numeric">Valor cobrado</th></tr></thead>
        <tbody>{charges.map((charge) => <tr key={charge.id}>
          <td>{formatDateOnly(charge.date)}</td><td className="registry__muted">{charge.cardNumber || expense.accountNumber || expense.accountName}</td>
          <td className="biz-table__numeric recurring-expenses__amount">{formatCurrency(charge.amount)}</td>
        </tr>)}</tbody>
      </table></div>
      {totalPages > 1 && <Pager label="Páginas do histórico de cobranças" page={currentPage} totalPages={totalPages} totalCount={data.charges.length} countLabel="cobranças" onChange={setPage} />}
    </Section>
  </div>;
}
