import { useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { getTaxes } from "../../../api/taxes.js";
import { isAbortError } from "../../../api/client.js";
import { useAccount } from "../../../context/AccountContext.jsx";
import NoBankAccount from "../../../components/dashboard/NoBankAccount/NoBankAccount.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import RangePicker from "../../../components/ui/RangePicker/RangePicker.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import { transactionTone } from "../../../utils/financialTone.js";
import { formatCurrency, formatDate, roundMoney } from "../../../utils/financeMath.js";
import { formatMonthLong } from "../../../utils/monthLabel.js";
import usePageTitle from "../../../hooks/usePageTitle.js";
import SummaryMetric from "../ReceitasDespesas/SummaryMetric.jsx";
import { currentMonthStart, monthParam, parseMonthParam } from "../ReceitasDespesas/cashFormat.js";
import "../ReceitasDespesas/ReceitasDespesas.css";
import "./Impostos.css";

const AUTHORITIES = {
  RFB: { name: "Receita Federal", kind: "Tributo federal" },
  INSS: { name: "INSS", kind: "Contribuição previdenciária" },
};

const MONTH_NAME = new Intl.DateTimeFormat("pt-BR", { month: "long", timeZone: "UTC" });

function monthName(year, month) {
  const label = MONTH_NAME.format(new Date(Date.UTC(year, month - 1, 1)));
  return label.charAt(0).toUpperCase() + label.slice(1);
}

function describe(payment) {
  const authority = AUTHORITIES[payment.authority];
  const account = [payment.institutionName, payment.accountName, payment.accountNumber].filter(Boolean).join(" · ");
  if (authority) return { title: authority.name, detail: [authority.kind, "Destinatário informado pelo banco", account].filter(Boolean).join(" · ") };
  return { title: "Tributo sem órgão identificado", detail: ["Convênio de arrecadação", account].filter(Boolean).join(" · ") };
}

function PaymentList({ payments, monthLabel }) {
  if (payments.length === 0) {
    return <p className="receitas-despesas__hint">{`Nenhum pagamento de tributo identificado no banco em ${monthLabel.toLowerCase()}.`}</p>;
  }
  return (
    <ul className="rd-tx taxes__list">
      {payments.map((p) => {
        const { title, detail } = describe(p);
        return (
          <li key={p.id}>
            <Link to={`/dashboard/relatorios/${p.id}`} state={{ fromReport: true }} className="rd-tx__item taxes__row">
              <span className="rd-tx__desc">
                <span className="rd-tx__name">{title}</span>
                <span className="rd-tx__secondary">{detail}</span>
              </span>
              <span className="rd-tx__date">{formatDate(p.date)}</span>
              <span className={`rd-tx__amount rd-tx__amount--${transactionTone("Expense", p.amount)}`}>
                {`−${formatCurrency(Math.abs(p.amount))}`}
              </span>
            </Link>
          </li>
        );
      })}
    </ul>
  );
}

function YearMonths({ year, months, selected, lastMonth, onSelect }) {
  const visible = months.filter((m) => Date.UTC(year, m.month - 1, 1) <= lastMonth.getTime());
  return (
    <ol className="taxes__months">
      {visible.map((m) => {
        const current = m.month === selected;
        return (
          <li key={m.month}>
            <button
              type="button"
              className="taxes__month"
              aria-current={current ? "true" : undefined}
              onClick={() => onSelect(new Date(Date.UTC(year, m.month - 1, 1)))}
            >
              <span className="taxes__month-name">{monthName(year, m.month)}</span>
              <span className="taxes__month-count">
                {m.count === 0 ? "Nenhum identificado" : `${m.count} ${m.count === 1 ? "pagamento" : "pagamentos"}`}
              </span>
              <span className="taxes__month-total">{m.count === 0 ? "—" : formatCurrency(m.total)}</span>
            </button>
          </li>
        );
      })}
    </ol>
  );
}

export default function Impostos() {
  usePageTitle("Impostos");
  const [params, setParams] = useSearchParams();
  const lastMonth = currentMonthStart();
  const month = useMemo(() => {
    const parsed = parseMonthParam(params.get("mes"));
    return parsed && parsed <= lastMonth ? parsed : lastMonth;
  }, [params, lastMonth]);
  const { scopeKey, scopeQuery, dataRefreshKey, accountsStatus } = useAccount();
  const [result, setResult] = useState(null);

  const year = month.getUTCFullYear();
  const monthNumber = month.getUTCMonth() + 1;
  const key = `${scopeKey}|${dataRefreshKey}|${monthParam(month)}`;

  useEffect(() => {
    if (accountsStatus !== "ready" || !scopeKey) return;
    const controller = new AbortController();
    getTaxes({ ...scopeQuery, year, month: monthNumber }, { signal: controller.signal })
      .then((data) => setResult({ key, data, error: "" }))
      .catch((err) => {
        if (controller.signal.aborted || isAbortError(err)) return;
        setResult({ key, data: null, error: err.message || "Não foi possível carregar os impostos." });
      });
    return () => controller.abort();
  }, [key, year, monthNumber, scopeKey, scopeQuery, accountsStatus]);

  if (accountsStatus === "error") return <p className="receitas-despesas__error">Não foi possível carregar as contas.</p>;
  if (accountsStatus === "ready" && !scopeKey) return <NoBankAccount />;

  const goTo = (date) => setParams(monthParam(date) === monthParam(lastMonth) ? {} : { mes: monthParam(date) });
  const shift = (months) => goTo(new Date(Date.UTC(year, monthNumber - 1 + months, 1)));
  const shiftYear = (delta) => {
    const target = new Date(Date.UTC(year + delta, monthNumber - 1, 1));
    goTo(target > lastMonth ? lastMonth : target);
  };

  const ready = result?.key === key ? result : null;
  const data = ready?.data;
  const label = formatMonthLong(month);
  const yearCount = data?.months?.reduce((sum, m) => sum + m.count, 0) ?? 0;
  const yearTotal = roundMoney(data?.months?.reduce((sum, m) => sum + m.total, 0) ?? 0);

  return (
    <div className="receitas-despesas taxes">
      <header className="receitas-despesas__header">
        <SectionHeading title="Impostos" level={1} />
        <RangePicker label={label} onPrev={() => shift(-1)} onNext={() => shift(1)} nextDisabled={month >= lastMonth} />
      </header>

      {ready?.error ? (
        <p className="receitas-despesas__error">{ready.error}</p>
      ) : (
        <>
          <div className="taxes__metrics">
            <SummaryMetric
              label={`Identificado no banco em ${label.toLowerCase()}`}
              value={!data ? <Skeleton width="7rem" height="1.5rem" /> : data.count === 0 ? "Nenhum identificado" : formatCurrency(data.total)}
              tone={data?.count ? transactionTone("Expense", data.total) : "default"}
            />
            <SummaryMetric
              label="Impostos identificados no ano"
              value={!data ? <Skeleton width="7rem" height="1.5rem" /> : yearCount === 0 ? "Nenhum identificado" : formatCurrency(yearTotal)}
              tone={yearCount ? transactionTone("Expense", yearTotal) : "default"}
            />
          </div>

          <section className="cash-month__section" aria-labelledby="taxes-payments">
            <h2 id="taxes-payments" className="cash-analysis__title">
              Pagamentos identificados
              {data && data.count > 0 && <span className="taxes__count">{data.count}</span>}
            </h2>
            {data ? <PaymentList payments={data.payments} monthLabel={label} /> : <Skeleton width="100%" height="10rem" />}
          </section>

          <section className="cash-month__section" aria-labelledby="taxes-year">
            <div className="taxes__year-head">
              <h2 id="taxes-year" className="cash-analysis__title">
                {`Meses de ${year}`}
              </h2>
              <RangePicker
                label={String(year)}
                onPrev={() => shiftYear(-1)}
                onNext={() => shiftYear(1)}
                nextDisabled={year >= lastMonth.getUTCFullYear()}
              />
            </div>
            {data ? (
              <YearMonths year={year} months={data.months} selected={monthNumber} lastMonth={lastMonth} onSelect={goTo} />
            ) : (
              <Skeleton width="100%" height="12rem" />
            )}
          </section>
        </>
      )}
    </div>
  );
}
