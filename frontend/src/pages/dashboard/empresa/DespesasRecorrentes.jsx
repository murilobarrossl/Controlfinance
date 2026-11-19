import { useEffect, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router";
import { getRecurringExpenses } from "../../../api/recurringExpenses.js";
import { isAbortError } from "../../../api/client.js";
import { useAccount } from "../../../context/AccountContext.jsx";
import NoBankAccount from "../../../components/dashboard/NoBankAccount/NoBankAccount.jsx";
import RangePicker from "../../../components/ui/RangePicker/RangePicker.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import Pager from "../../../components/ui/Pager/Pager.jsx";
import RecurringLogo from "./RecurringLogo.jsx";
import BusinessModule from "./BusinessModule.jsx";
import { SummaryItem } from "./RegistryParts.jsx";
import { openRow } from "./registryRow.js";
import { formatCurrency } from "../../../utils/financeMath.js";
import { formatDateOnly } from "../../../utils/employees.js";
import { formatMonthLong } from "../../../utils/monthLabel.js";
import { currentMonthStart, monthParam, parseMonthParam } from "../ReceitasDespesas/cashFormat.js";
import { accountLabel, activityLabel, frequencyLabel } from "./recurringExpenseLabels.js";
import { isCreditCardAccount } from "../../../utils/accountKind.js";
import { useDebounced } from "./useRegistryData.js";
import "./RegistryList.css";
import "./RecurringExpenses.css";

const PAGE_SIZE = 25;

export default function DespesasRecorrentes() {
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const lastMonth = currentMonthStart();
  const parsed = parseMonthParam(params.get("mes"));
  const month = parsed && parsed <= lastMonth && parsed.getUTCFullYear() >= 2000 ? parsed : lastMonth;
  const monthKey = monthParam(month);
  const { scopeKey: accountScopeKey, scopeQuery, dataRefreshKey, accountsStatus, connections, accounts } = useAccount();
  const scopeKey = accountScopeKey || (accounts.some(isCreditCardAccount) ? "cards" : "");
  const [result, setResult] = useState(null);
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [retry, setRetry] = useState(0);
  const syncKey = connections.map((c) => `${c.id}:${c.lastSyncedAt ?? ""}:${c.status}`).sort().join("|");
  const query = useDebounced(search.trim(), 300);
  const baseKey = `${scopeKey}|${dataRefreshKey}|${syncKey}|${monthKey}|${retry}`;
  const key = `${baseKey}|${query}|${page}`;
  const year = month.getUTCFullYear();
  const monthNumber = month.getUTCMonth() + 1;

  useEffect(() => {
    if (accountsStatus !== "ready" || !scopeKey) return;
    const controller = new AbortController();
    getRecurringExpenses({ ...scopeQuery, year, month: monthNumber, page, search: query }, { signal: controller.signal })
      .then((data) => setResult({ key, baseKey, data, error: "" }))
      .catch((err) => {
        if (controller.signal.aborted || isAbortError(err)) return;
        setResult({ key, baseKey, data: null, error: err.message || "Não foi possível carregar as despesas recorrentes." });
      });
    return () => controller.abort();
  }, [key, baseKey, scopeKey, scopeQuery, accountsStatus, year, monthNumber, page, query]);

  const ready = result?.key === key ? result : null;
  const data = ready?.data ?? (result?.baseKey === baseKey ? result.data : null);
  const shown = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const totalPages = Math.max(1, Math.ceil(totalCount / (data?.pageSize || PAGE_SIZE)));
  const currentPage = data?.page ?? page;
  const detailPath = (item) => `/dashboard/despesas-recorrentes/${item.id}?mes=${monthKey}`;
  const shift = (delta) => {
    const target = new Date(Date.UTC(year, monthNumber - 1 + delta, 1));
    setParams(monthParam(target) === monthParam(lastMonth) ? {} : { mes: monthParam(target) });
    setPage(1);
  };

  return (
    <div className="recurring-expenses-page">
    <BusinessModule title="Despesas recorrentes" ready>
      <div className="registry recurring-expenses">
        <div className="recurring-expenses__period">
          <RangePicker label={formatMonthLong(month)} onPrev={() => shift(-1)} onNext={() => shift(1)}
            prevDisabled={year === 2000 && monthNumber === 1} nextDisabled={month >= lastMonth} />
        </div>
        {accountsStatus === "error" ? <p className="registry__error" role="alert">Não foi possível carregar as contas. Recarregue a página para tentar novamente.</p>
          : accountsStatus === "ready" && !scopeKey ? <NoBankAccount />
          : ready?.error ? <div role="alert"><p className="registry__error">{ready.error}</p><button type="button" className="recurring-expenses__retry" onClick={() => setRetry((v) => v + 1)}>Tentar novamente</button></div>
          : <>
            <dl className="registry-summary summary-dividers" aria-busy={!data}>
              <SummaryItem label="Cobrado no mês" value={data ? formatCurrency(data.chargedTotal) : <Skeleton width="8rem" height="1.6rem" />} />
              <SummaryItem label="Número de serviços" value={data ? data.serviceCount : <Skeleton width="3rem" height="1.6rem" />} />
            </dl>
            {data?.cardsWithoutDetection > 0 && <p className="recurring-expenses__note" role="status">{data.cardsWithoutDetection === 1 ? "Recorrências indisponíveis em um cartão." : `Recorrências indisponíveis em ${data.cardsWithoutDetection} cartões.`}</p>}
            <section className="registry__list" aria-labelledby="recurring-list-title">
              <h2 id="recurring-list-title" className="visually-hidden">Despesas recorrentes</h2>
              <div className="registry__toolbar">
                <input type="search" className="registry__search" aria-label="Buscar serviço, estabelecimento ou instituição" placeholder="Buscar serviço ou instituição"
                  autoComplete="off" value={search} onChange={(e) => { setSearch(e.target.value); setPage(1); }} />
              </div>
              <div className="registry__table-wrap">
                <table className="biz-table registry__table recurring-expenses__table" aria-busy={!data}>
                  <thead><tr>
                    <th scope="col">Serviço ou estabelecimento</th><th scope="col" className="biz-table__numeric">Última cobrança</th><th scope="col">Frequência</th>
                    <th scope="col">Último registro</th><th scope="col">Próxima estimada</th>
                  </tr></thead>
                  <tbody>
                    {!data ? Array.from({ length: 4 }, (_, i) => <tr key={i} className="registry__placeholder"><td colSpan={5}><Skeleton width="100%" height="1.1rem" /></td></tr>)
                      : shown.length === 0 ? <tr className="biz-table__state"><td colSpan={5}>{search.trim() ? "Nenhuma recorrência encontrada com essa busca." : "Nenhuma despesa recorrente encontrada."}</td></tr>
                      : shown.map((item) => <tr key={item.id} className="registry__row" onClick={(e) => openRow(e, navigate, detailPath(item))}>
                        <td data-label="Serviço"><div className="recurring-expenses__merchant">
                          <span className="recurring-expenses__logo" aria-hidden="true"><RecurringLogo name={item.name} /></span>
                          <div className="recurring-expenses__merchant-text"><Link className="registry__name" to={detailPath(item)}>{item.name}</Link>
                            <span className="registry__secondary">{accountLabel(item)}</span></div>
                        </div></td>
                        <td data-label="Última cobrança" className="biz-table__numeric recurring-expenses__amount">{formatCurrency(item.lastAmount)}</td>
                        <td data-label="Frequência" className="registry__muted">{frequencyLabel(item.periodMonths)}</td>
                        <td data-label="Último registro" className="registry__muted">{formatDateOnly(item.lastChargeDate)}<span className="registry__secondary">{activityLabel(item.activity)}</span></td>
                        <td data-label="Próxima estimada" className="registry__muted">{item.nextEstimatedDate ? formatDateOnly(item.nextEstimatedDate) : "Sem previsão"}</td>
                      </tr>)}
                  </tbody>
                </table>
              </div>
              {data && totalCount > 0 && <Pager label="Páginas de despesas recorrentes" page={currentPage} totalPages={totalPages} totalCount={totalCount}
                countLabel={totalCount === 1 ? "recorrência" : "recorrências"} onChange={setPage} />}
            </section>
          </>}
      </div>
    </BusinessModule>
    </div>
  );
}
